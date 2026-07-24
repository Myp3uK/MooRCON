using Spectre.Console;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Timers;
// Алиасы, а не using всего MooRCON.Core: у консоли свой ServerConfig в namespace MooRCON.
using RconSession = MooRCON.Core.RconSession;
using RconAuthException = MooRCON.Core.RconAuthException;
#nullable enable
namespace MooRCON
{
    internal partial class Program
    {
        private const int MAX_AUTH_ATTEMPTS = 3;
        private const int AUTH_RETRY_DELAY_MS = 1000;
        private const int COMMAND_TIMEOUT_MS = 10000;
        private const int MAX_HISTORY_ENTRIES = 200;
        private const int RETURN_TO_MENU_DELAY_MS = 1500;
        private const string PROMPT_MARKUP = "[lime][[MooRCON]] >[/] ";
        private const string ENC_PREFIX = "enc:";

        // Данные программы храним в %APPDATA%\MooRCON, а не рядом с exe.
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MooRCON");
        private static readonly string ConfigFile = Path.Combine(DataDir, "servers.json");
        private static readonly string HistoryFile = Path.Combine(DataDir, "history.json");

        private static RconSession? rconClient;
        private static volatile bool connected = false;
        private static readonly System.Timers.Timer idleTimer = new(TimeSpan.FromMinutes(5).TotalMilliseconds);
        private static ServerConfig? currentServer;

        // Синхронизация вывода в консоль между REPL, idle-таймером и обработчиком Ctrl+C.
        private static readonly object _consoleLock = new();

        private static readonly List<string> _commandHistory = new();
        private static List<string> _availableCommands = DefaultBuiltinCommands();
        private static int _inputStartTop;
        private static int _lastDrawnRows = 1;

        // Флаг для выхода из вложенного REPL при нажатии Ctrl+C
        private static volatile bool _returnToServerMenu = false;

        // Отличает выход именно по Ctrl+C от обычного (exit/idle) — для показа
        // уведомления и паузы перед возвратом в главное меню.
        private static volatile bool _exitedViaCtrlC = false;

        // Опции сериализации: красивый отступ + кириллица без \uXXXX
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private class ServerConfig
        {
            public string Name { get; set; } = "";
            public string IpHost { get; set; } = "";
            public int RconPort { get; set; }
            public string RconPass { get; set; } = "";
        }

        private enum MenuAction { Connect, Add, Edit, Delete, OpenDataFolder, Exit }

        private class MenuItem
        {
            public MenuAction Action { get; init; }
            public ServerConfig? Server { get; init; }
            public string Display { get; init; } = "";
        }

        static List<string> DefaultBuiltinCommands() =>
            new() { "exit", "quit", "close", "reconnect", "clear" };

        // ===========================================================
        //                            MAIN
        // ===========================================================
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.Clear();
            DrawLogo();
            EnsureDataDir();

            idleTimer.AutoReset = true;
            idleTimer.Elapsed += OnTimedEvent;

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                // Порядок важен: флаг Ctrl+C выставляем ДО _returnToServerMenu, иначе
                // REPL успеет выйти и Main прочитает флаг ещё как false.
                _exitedViaCtrlC = true;
                _returnToServerMenu = true;
                if (rconClient != null && connected)
                {
                    connected = false;
                    idleTimer.Stop();
                    try { rconClient.Disconnect(); }
                    catch { /* возможна гонка с ExecuteCommandAsync — игнорируем */ }
                }
            };

            while (true)
            {
                _returnToServerMenu = false;

                var servers = LoadServers();
                var choice = ShowMainMenu(servers);

                switch (choice.Action)
                {
                    case MenuAction.Connect:
                        if (choice.Server is null) break;
                        currentServer = choice.Server;
                        await RunRepl();
                        if (_exitedViaCtrlC)
                        {
                            _exitedViaCtrlC = false;
                            lock (_consoleLock)
                            {
                                Console.WriteLine();
                                AnsiConsole.MarkupLine("[yellow]Отключение по Ctrl+C. Возврат в главное меню...[/]");
                            }
                            await Task.Delay(RETURN_TO_MENU_DELAY_MS);
                        }
                        Console.Clear();
                        DrawLogo();
                        break;

                    case MenuAction.Add:
                        AddServer(servers);
                        Console.Clear();
                        DrawLogo();
                        break;

                    case MenuAction.Edit:
                        EditServer(servers);
                        Console.Clear();
                        DrawLogo();
                        break;

                    case MenuAction.Delete:
                        DeleteServer(servers);
                        Console.Clear();
                        DrawLogo();
                        break;

                    case MenuAction.OpenDataFolder:
                        OpenDataFolder();
                        Console.Clear();
                        DrawLogo();
                        break;

                    case MenuAction.Exit:
                        return;
                }
            }
        }

        private static void DrawLogo() =>
            AnsiConsole.Write(new FigletText("MooRCON").Centered().Color(Color.Red));

        // ===========================================================
        //                       ГЛАВНОЕ МЕНЮ
        // ===========================================================
        private static MenuItem ShowMainMenu(List<ServerConfig> servers)
        {
            var prompt = new SelectionPrompt<MenuItem>()
                .Title("[green]Выберите сервер или действие:[/]")
                .PageSize(20)
                .HighlightStyle(new Style(foreground: Color.Lime))
                .UseConverter(m => m.Display);

            foreach (var s in servers)
            {
                prompt.AddChoice(new MenuItem
                {
                    Action = MenuAction.Connect,
                    Server = s,
                    Display = $"[lime]→[/] {Markup.Escape(s.Name)} [grey]({Markup.Escape(s.IpHost)}:{s.RconPort})[/]"
                });
            }

            prompt.AddChoice(new MenuItem
            {
                Action = MenuAction.Add,
                Display = "[blue]+[/] Добавить сервер"
            });

            if (servers.Count > 0)
            {
                prompt.AddChoice(new MenuItem
                {
                    Action = MenuAction.Edit,
                    Display = "[yellow]~[/] Редактировать сервер"
                });
                prompt.AddChoice(new MenuItem
                {
                    Action = MenuAction.Delete,
                    Display = "[red]x[/] Удалить сервер"
                });
            }

            prompt.AddChoice(new MenuItem
            {
                Action = MenuAction.OpenDataFolder,
                Display = "[aqua]#[/] Открыть папку с данными"
            });

            prompt.AddChoice(new MenuItem
            {
                Action = MenuAction.Exit,
                Display = "[grey]<[/] Выход"
            });

            return AnsiConsole.Prompt(prompt);
        }

        // ===========================================================
        //               УПРАВЛЕНИЕ СПИСКОМ СЕРВЕРОВ
        // ===========================================================
        private static void AddServer(List<ServerConfig> servers)
        {
            AnsiConsole.MarkupLine("[green]── Добавление сервера ──[/]");
            var cfg = PromptForServer(null, servers);
            if (cfg is null)
            {
                AnsiConsole.MarkupLine("[yellow]Добавление отменено[/]");
                WaitForKey();
                return;
            }

            servers.Add(cfg);
            SaveServers(servers);
            AnsiConsole.MarkupLine($"[green]Сервер '{Markup.Escape(cfg.Name)}' добавлен[/]");
            WaitForKey();
        }

        private static void EditServer(List<ServerConfig> servers)
        {
            if (servers.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Нет серверов для редактирования[/]");
                WaitForKey();
                return;
            }

            var target = AnsiConsole.Prompt(
                new SelectionPrompt<ServerConfig>()
                    .Title("[green]Какой сервер редактировать?[/]")
                    .UseConverter(s => $"{Markup.Escape(s.Name)} ({Markup.Escape(s.IpHost)}:{s.RconPort})")
                    .AddChoices(servers));

            AnsiConsole.MarkupLine($"[green]── Редактирование '{Markup.Escape(target.Name)}' ──[/]");
            AnsiConsole.MarkupLine("[grey]Нажмите Enter, чтобы оставить текущее значение[/]");

            var oldName = target.Name;
            var updated = PromptForServer(target, servers);
            if (updated is null)
            {
                AnsiConsole.MarkupLine("[yellow]Редактирование отменено[/]");
                WaitForKey();
                return;
            }

            target.Name = updated.Name;
            target.IpHost = updated.IpHost;
            target.RconPort = updated.RconPort;
            target.RconPass = updated.RconPass;

            // Если имя изменилось — переносим историю на новое имя
            if (!string.Equals(oldName, updated.Name, StringComparison.Ordinal))
                RenameHistory(oldName, updated.Name);

            SaveServers(servers);
            AnsiConsole.MarkupLine($"[green]Сервер сохранён[/]");
            WaitForKey();
        }

        private static void DeleteServer(List<ServerConfig> servers)
        {
            if (servers.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Нет серверов для удаления[/]");
                WaitForKey();
                return;
            }

            var target = AnsiConsole.Prompt(
                new SelectionPrompt<ServerConfig>()
                    .Title("[green]Какой сервер удалить?[/]")
                    .UseConverter(s => $"{Markup.Escape(s.Name)} ({Markup.Escape(s.IpHost)}:{s.RconPort})")
                    .AddChoices(servers));

            var ok = AnsiConsole.Confirm(
                $"Удалить сервер [red]'{Markup.Escape(target.Name)}'[/]? История команд этого сервера также будет удалена.",
                false);

            if (!ok)
            {
                AnsiConsole.MarkupLine("[yellow]Удаление отменено[/]");
                WaitForKey();
                return;
            }

            servers.Remove(target);
            SaveServers(servers);
            DeleteHistoryForServer(target.Name);
            AnsiConsole.MarkupLine($"[green]Сервер '{Markup.Escape(target.Name)}' удалён[/]");
            WaitForKey();
        }

        /// <summary>
        /// Запрашивает у пользователя поля сервера. existing != null — режим редактирования.
        /// servers нужен для проверки уникальности имени.
        /// Возвращает null при отмене (Ctrl+C).
        /// </summary>
        private static ServerConfig? PromptForServer(ServerConfig? existing, List<ServerConfig> servers)
        {
            try
            {
                // === Имя ===
                var namePrompt = BuildStringPrompt("Имя сервера", existing?.Name, allowEmpty: false)
                    .Validate(name =>
                    {
                        if (string.IsNullOrWhiteSpace(name))
                            return ValidationResult.Error("Имя не может быть пустым");
                        if (servers.Any(s => s != existing &&
                                             s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            return ValidationResult.Error("Сервер с таким именем уже существует");
                        return ValidationResult.Success();
                    });
                var name = AnsiConsole.Prompt(namePrompt).Trim();

                // === Адрес ===
                var hostPrompt = BuildStringPrompt("Адрес (IP или hostname)", existing?.IpHost, allowEmpty: false)
                    .Validate(v => !string.IsNullOrWhiteSpace(v)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("Адрес не может быть пустым"));
                var host = AnsiConsole.Prompt(hostPrompt).Trim();

                // === Порт === (число — безопасно для markup)
                var portPrompt = new TextPrompt<int>("[grey]Порт RCON:[/]")
                    .Validate(p => p is > 0 and < 65536
                        ? ValidationResult.Success()
                        : ValidationResult.Error("Порт должен быть от 1 до 65535"));
                portPrompt.DefaultValue(existing?.RconPort ?? 25575);
                var port = AnsiConsole.Prompt(portPrompt);

                // === Пароль === (ввод маскируется, текущее значение не показываем)
                var passPrompt = BuildStringPrompt("Пароль RCON", existing?.RconPass, allowEmpty: true, secret: true);
                var pass = AnsiConsole.Prompt(passPrompt);

                return new ServerConfig
                {
                    Name = name,
                    IpHost = host,
                    RconPort = port,
                    RconPass = pass
                };
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Ошибка ввода: {Markup.Escape(ex.Message)}[/]");
                return null;
            }
        }

        /// <summary>
        /// Безопасный TextPrompt&lt;string&gt;: текущее значение показывается в самом
        /// тексте подсказки через Markup.Escape, а ShowDefaultValue(false) подавляет
        /// печать default'а самим Spectre. Иначе квадратные скобки в имени/пароле
        /// сломают markup-парсер и Spectre упадёт.
        /// </summary>
        private static TextPrompt<string> BuildStringPrompt(string label, string? current, bool allowEmpty, bool secret = false)
        {
            // Для секрета текущее значение НЕ печатаем в открытом виде.
            string text;
            if (secret)
                text = current is not null
                    ? $"[grey]{Markup.Escape(label)} [white](Enter — оставить текущий)[/]:[/]"
                    : $"[grey]{Markup.Escape(label)}:[/]";
            else
                text = current is not null
                    ? $"[grey]{Markup.Escape(label)} (текущее: [white]'{Markup.Escape(current)}'[/]):[/]"
                    : $"[grey]{Markup.Escape(label)}:[/]";

            var prompt = new TextPrompt<string>(text);

            if (secret)
                prompt.Secret('*');

            if (current is not null)
            {
                // DefaultValue хранится для применения при пустом вводе,
                // но НЕ выводится Spectre на экран — поэтому markup-safe.
                prompt.DefaultValue(current).ShowDefaultValue(false);
            }

            if (allowEmpty)
                prompt.AllowEmpty();

            return prompt;
        }

        private static void WaitForKey()
        {
            AnsiConsole.MarkupLine("[grey]Нажмите любую клавишу...[/]");
            try { Console.ReadKey(true); } catch { }
        }

        // ===========================================================
        //                   ПАПКА ДАННЫХ ПРОГРАММЫ
        // ===========================================================
        /// <summary>
        /// Создаёт %APPDATA%\MooRCON. Если в нём ещё нет наших файлов, но они лежат
        /// рядом с exe (старое расположение), — переносит их в AppData и удаляет оригиналы.
        /// </summary>
        private static void EnsureDataDir()
        {
            try
            {
                Directory.CreateDirectory(DataDir);

                bool moved = false;
                moved |= MigrateLegacyFile("servers.json", ConfigFile);
                moved |= MigrateLegacyFile("history.json", HistoryFile);

                if (moved)
                    AnsiConsole.MarkupLine($"[green]Данные перенесены в[/] {Markup.Escape(DataDir)}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Не удалось подготовить папку данных: {Markup.Escape(ex.Message)}[/]");
            }
        }

        /// <summary>
        /// Переносит один файл из папки с exe в AppData: копирует, затем удаляет оригинал.
        /// Возвращает true, если перенос состоялся. Если файл в AppData уже есть, оригинал
        /// рядом с exe не трогаем (актуальные данные — в AppData).
        /// </summary>
        private static bool MigrateLegacyFile(string legacyName, string targetPath)
        {
            try
            {
                var legacyPath = Path.Combine(AppContext.BaseDirectory, legacyName);
                if (!File.Exists(legacyPath)) return false;
                if (File.Exists(targetPath)) return false;

                // Сначала копируем (чтобы при сбое не потерять данные), затем удаляем оригинал.
                File.Copy(legacyPath, targetPath, overwrite: false);
                try { File.Delete(legacyPath); }
                catch { /* оригинал удалить не удалось — данные уже в AppData, не критично */ }
                return true;
            }
            catch
            {
                return false; // миграция не критична — при неудаче начнём с чистого файла
            }
        }

        private static void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = DataDir,
                    UseShellExecute = true
                });
                AnsiConsole.MarkupLine($"[green]Папка данных:[/] {Markup.Escape(DataDir)}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Не удалось открыть папку: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine($"[grey]Путь: {Markup.Escape(DataDir)}[/]");
            }
            WaitForKey();
        }

        // ===========================================================
        //           ШИФРОВАНИЕ ПАРОЛЯ (DPAPI, per-user)
        // ===========================================================
        private static string ProtectPassword(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                // CA1416: DPAPI только под Windows; на других ОС уходим в catch и храним как есть.
#pragma warning disable CA1416
                var bytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                return ENC_PREFIX + Convert.ToBase64String(bytes);
            }
            catch
            {
                // DPAPI недоступна (например, не Windows) — оставляем как есть.
                return plain;
            }
        }

        private static string UnprotectPassword(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(ENC_PREFIX, StringComparison.Ordinal))
                return stored; // старый формат в открытом виде — перешифруется при след. сохранении
            try
            {
                var bytes = Convert.FromBase64String(stored[ENC_PREFIX.Length..]);
#pragma warning disable CA1416
                var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        // ===========================================================
        //               ЗАГРУЗКА / СОХРАНЕНИЕ СЕРВЕРОВ
        // ===========================================================
        private static readonly JsonSerializerOptions JsonReadOpts = new()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static List<ServerConfig> LoadServers()
        {
            if (!File.Exists(ConfigFile))
                return new List<ServerConfig>();

            try
            {
                string json = File.ReadAllText(ConfigFile, Encoding.UTF8);
                var servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, JsonReadOpts)
                              ?? new List<ServerConfig>();
                // Расшифровываем пароли в открытый вид для работы в памяти.
                foreach (var s in servers)
                    s.RconPass = UnprotectPassword(s.RconPass);
                return servers;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Ошибка чтения конфигурации: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine("[yellow]Проверьте формат файла. Убедитесь, что нет лишних запятых.[/]");
                return new List<ServerConfig>();
            }
        }

        private static void SaveServers(List<ServerConfig> servers)
        {
            try
            {
                // Сериализуем копии с зашифрованными паролями, не трогая объекты в памяти.
                var toSave = servers.Select(s => new ServerConfig
                {
                    Name = s.Name,
                    IpHost = s.IpHost,
                    RconPort = s.RconPort,
                    RconPass = ProtectPassword(s.RconPass)
                }).ToList();

                var json = JsonSerializer.Serialize(toSave, JsonOpts);
                File.WriteAllText(ConfigFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Не удалось сохранить серверы: {Markup.Escape(ex.Message)}[/]");
            }
        }

        // ===========================================================
        //                  ИСТОРИЯ МЕЖДУ СЕССИЯМИ
        // ===========================================================
        private static Dictionary<string, List<string>> LoadAllHistory()
        {
            if (!File.Exists(HistoryFile)) return new();
            try
            {
                var json = File.ReadAllText(HistoryFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonReadOpts) ?? new();
            }
            catch
            {
                return [];
            }
        }

        private static void SaveAllHistory(Dictionary<string, List<string>> all)
        {
            try
            {
                var json = JsonSerializer.Serialize(all, JsonOpts);
                File.WriteAllText(HistoryFile, json, Encoding.UTF8);
            }
            catch {/* ignored */}
        }

        private static void LoadHistoryForCurrentServer()
        {
            _commandHistory.Clear();
            if (currentServer is null || string.IsNullOrEmpty(currentServer.Name)) return;

            var all = LoadAllHistory();
            if (all.TryGetValue(currentServer.Name, out var list))
                _commandHistory.AddRange(list);
        }

        private static void SaveHistoryForCurrentServer()
        {
            if (currentServer is null || string.IsNullOrEmpty(currentServer.Name)) return;

            var all = LoadAllHistory();
            var trimmed = _commandHistory.Count > MAX_HISTORY_ENTRIES
                ? [.. _commandHistory.Skip(_commandHistory.Count - MAX_HISTORY_ENTRIES)]
                : _commandHistory.ToList();

            if (trimmed.Count == 0)
                all.Remove(currentServer.Name);
            else
                all[currentServer.Name] = trimmed;

            SaveAllHistory(all);
        }

        private static void RenameHistory(string oldName, string newName)
        {
            var all = LoadAllHistory();
            if (!all.TryGetValue(oldName, out var list)) return;
            all.Remove(oldName);
            all[newName] = list;
            SaveAllHistory(all);
        }

        private static void DeleteHistoryForServer(string name)
        {
            var all = LoadAllHistory();
            if (all.Remove(name)) SaveAllHistory(all);
        }

        // ===========================================================
        //                  ПОДКЛЮЧЕНИЕ + АВТОРИЗАЦИЯ
        // ===========================================================
        private static async Task<bool> ConnectAndAuthAsync()
        {
            if (currentServer == null) return false;

            // Закрываем предыдущее соединение, если оно осталось от прошлой сессии.
            try { rconClient?.Disconnect(); } catch { }
            rconClient = new RconSession();

            for (int attempt = 1; attempt <= MAX_AUTH_ATTEMPTS; attempt++)
            {
                ConsoleWrite($"Попытка подключения {attempt}/{MAX_AUTH_ATTEMPTS}...");
                try
                {
                    await rconClient.ConnectAndAuthAsync(
                        currentServer.IpHost, currentServer.RconPort, currentServer.RconPass);
                    ConsoleWrite("Соединение установлено");
                    ConsoleWrite("Авторизация успешна!");
                    return true;
                }
                catch (RconAuthException ex)
                {
                    // Пароль отвергнут — повторять бессмысленно и чревато баном по IP.
                    ConsoleWrite(ex.Message);
                    ConsoleWrite("Не удалось пройти валидацию, проверьте пароль.");
                    return false;
                }
                catch (Exception ex)
                {
                    ConsoleWrite($"Не удалось подключиться: {ex.Message}");
                }

                if (attempt < MAX_AUTH_ATTEMPTS) await Task.Delay(AUTH_RETRY_DELAY_MS);
            }

            ConsoleWrite("Проверьте корректность введенных данных");
            return false;
        }

        private static async Task TryLoadCommandsAsync()
        {
            if (rconClient is null) return;
            try
            {
                var helpOutput = await rconClient.ExecuteAsync("help");
                var parsed = ParseHelpOutput(helpOutput);
                if (parsed.Count > 0)
                {
                    _availableCommands = parsed;
                    ConsoleWrite($"Загружено команд для автодополнения: {parsed.Count}");
                }
            }
            catch (Exception ex)
            {
                ConsoleWrite($"Не удалось загрузить список команд: {ex.Message}");
                // Сорвавшийся help закрывает сессию (поток пакетов уже не сходится) —
                // отражаем это, иначе REPL будет молча долбиться в мёртвый сокет.
                if (!rconClient.IsConnected) connected = false;
            }
        }

        // ===========================================================
        //                            REPL
        // ===========================================================
        private static async Task RunRepl()
        {
            _availableCommands = DefaultBuiltinCommands();
            LoadHistoryForCurrentServer();
            _returnToServerMenu = false;
            _exitedViaCtrlC = false;

            try
            {
                if (!await ConnectAndAuthAsync())
                {
                    WaitForKey();
                    return;
                }

                connected = true;
                idleTimer.Start();

                await TryLoadCommandsAsync();

                while (connected && !_returnToServerMenu)
                {
                    string command;
                    try
                    {
                        command = ReadLineWithHistory(PROMPT_MARKUP, _availableCommands);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWrite($"Ошибка ввода: {ex.Message}");
                        continue;
                    }

                    if (_returnToServerMenu) break;

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        ConsoleWrite("Команда не может быть пустой, попробуйте еще раз");
                        continue;
                    }

                    command = command.Trim();

                    if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("close", StringComparison.OrdinalIgnoreCase))
                    {
                        ConsoleWrite("Возврат в меню...");
                        _returnToServerMenu = true;
                        break;
                    }

                    if (command.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("cls", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Clear();
                        continue;
                    }

                    if (command.Equals("reconnect", StringComparison.OrdinalIgnoreCase))
                    {
                        idleTimer.Stop();
                        connected = false;
                        if (await ConnectAndAuthAsync())
                        {
                            connected = true;
                            await TryLoadCommandsAsync();
                            idleTimer.Start();
                        }
                        else
                        {
                            break;
                        }
                        continue;
                    }

                    if (!connected || _returnToServerMenu) break;

                    // В историю попадают только реальные команды (не exit/clear/reconnect).
                    if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
                        _commandHistory.Add(command);

                    try
                    {
                        var response = await rconClient!.ExecuteAsync(command, COMMAND_TIMEOUT_MS);

                        if (!string.IsNullOrEmpty(response) && response.Contains("Couldn't find the command"))
                        {
                            ConsoleWrite($"Команда \"{command}\" не найдена, попробуйте help");
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            var text = NormalizeNewlines(response).TrimEnd();
                            lock (_consoleLock) Console.WriteLine(text);
                        }

                        idleTimer.Stop();
                        idleTimer.Start();
                    }
                    catch (OperationCanceledException)
                    {
                        // Ответ не дочитан до конца — поток пакетов рассинхронизирован,
                        // дальше по этому сокету доверять нечему: поднимаем новое соединение.
                        ConsoleWrite("Таймаут выполнения команды (10 сек). Переподключение...");
                        idleTimer.Stop();
                        connected = false;
                        if (!await ConnectAndAuthAsync()) break;
                        connected = true;
                        await TryLoadCommandsAsync();
                        idleTimer.Start();
                    }
                    catch (Exception ex)
                    {
                        ConsoleWrite($"Ошибка: {ex.Message}");
                    }
                }
            }
            finally
            {
                // Сохранение истории происходит при любом сценарии завершения:
                // exit/quit/close, Ctrl+C, idle-таймер, ошибка соединения.
                SaveHistoryForCurrentServer();

                idleTimer.Stop();
                try { rconClient?.Disconnect(); } catch { }
                connected = false;
            }
        }

        private static void OnTimedEvent(object? source, ElapsedEventArgs e)
        {
            connected = false;
            idleTimer.Stop();
            _returnToServerMenu = true;
            lock (_consoleLock)
            {
                Console.WriteLine();
                AnsiConsole.MarkupLine("[lime][[MooRCON]] [/]Соединение разорвано (5 минут без активности). Возврат в меню выбора сервера...");
            }
        }

        private static void ConsoleWrite(string str)
        {
            string prefix;
            if (currentServer != null && !string.IsNullOrEmpty(currentServer.Name))
                prefix = $"[lime][[MooRCON]] [BlueViolet]{Markup.Escape(currentServer.Name)}[/][/] ";
            else
                prefix = "[lime][[MooRCON]][/] ";
            lock (_consoleLock)
                AnsiConsole.MarkupLine($"{prefix}{Markup.Escape(str)}");
        }

        /// <summary>
        /// Ответ сервера может разделять строки одиночным \n (LF). Часть консольных хостов
        /// трактует одиночный LF как «перевод строки без возврата каретки» — тогда вывод идёт
        /// «лесенкой» (каждая строка начинается там, где закончилась предыдущая). Другие хосты
        /// (например, Windows Terminal) выводят такой текст нормально. Приводим все переводы
        /// строк к \r\n, чтобы вывод был корректным в любом терминале.
        /// </summary>
        private static string NormalizeNewlines(string s) =>
            s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

        private static List<string> ParseHelpOutput(string helpText)
        {
            var commands = new List<string>();
            var lines = helpText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) ||
                    trimmed.StartsWith("Commands:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = CommandRegex().Match(trimmed);
                if (match.Success)
                {
                    var cmd = match.Groups[1].Value.ToLowerInvariant();
                    if (cmd.Length > 0 && cmd.Length <= 30)
                        commands.Add(cmd);
                }
            }

            commands.AddRange(DefaultBuiltinCommands());
            return commands.Distinct().OrderBy(c => c).ToList();
        }

        // ===========================================================
        //   РЕДАКТОР СТРОКИ
        // ===========================================================
        private static string ReadLineWithHistory(string prompt, List<string> knownCommands)
        {
            var input = new StringBuilder();
            var cursorPosition = 0;
            var currentHistoryIndex = -1;
            var lastTabMatches = new List<string>();
            var lastTabIndex = -1;

            int promptLeft, promptTop;
            lock (_consoleLock)
            {
                _inputStartTop = Console.CursorTop;
                _lastDrawnRows = 1;
                AnsiConsole.Markup(prompt);
                promptLeft = Console.CursorLeft;
                promptTop = Console.CursorTop;
            }

            while (true)
            {
                // Не блокируемся намертво на ReadKey: опрашиваем ввод, чтобы idle-таймер
                // и Ctrl+C могли вернуть в меню, не дожидаясь нажатия клавиши.
                try
                {
                    while (!Console.KeyAvailable)
                    {
                        if (_returnToServerMenu)
                        {
                            lock (_consoleLock) Console.WriteLine();
                            return string.Empty;
                        }
                        Thread.Sleep(15);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Ввод перенаправлён — KeyAvailable недоступен, читаем блокирующе.
                }

                if (_returnToServerMenu)
                {
                    lock (_consoleLock) Console.WriteLine();
                    return string.Empty;
                }

                ConsoleKeyInfo key;
                try { key = Console.ReadKey(intercept: true); }
                catch { continue; }

                if (key.Key == ConsoleKey.Enter)
                {
                    PositionCursor(promptLeft, promptTop, cursorPosition);
                    Console.WriteLine();
                    return input.ToString();
                }
                else if (key.Key == ConsoleKey.UpArrow)
                {
                    lastTabMatches.Clear(); lastTabIndex = -1;
                    if (_commandHistory.Count == 0) continue;

                    if (currentHistoryIndex == -1)
                        currentHistoryIndex = _commandHistory.Count - 1;
                    else if (currentHistoryIndex > 0)
                        currentHistoryIndex--;

                    input.Clear();
                    input.Append(_commandHistory[currentHistoryIndex]);
                    cursorPosition = input.Length;
                    Redraw(prompt, input.ToString(), cursorPosition, ref promptLeft, ref promptTop);
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    lastTabMatches.Clear(); lastTabIndex = -1;
                    if (_commandHistory.Count == 0 || currentHistoryIndex == -1) continue;

                    if (currentHistoryIndex < _commandHistory.Count - 1)
                    {
                        currentHistoryIndex++;
                        input.Clear();
                        input.Append(_commandHistory[currentHistoryIndex]);
                    }
                    else
                    {
                        currentHistoryIndex = -1;
                        input.Clear();
                    }
                    cursorPosition = input.Length;
                    Redraw(prompt, input.ToString(), cursorPosition, ref promptLeft, ref promptTop);
                }
                else if (key.Key == ConsoleKey.Tab)
                {
                    if (knownCommands.Count == 0) continue;
                    var current = input.ToString();
                    if (current.Contains(' ')) continue;

                    if (lastTabMatches.Count > 1 && lastTabIndex >= 0 &&
                        current.Equals(lastTabMatches[lastTabIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        lastTabIndex = (lastTabIndex + 1) % lastTabMatches.Count;
                        ApplyTabMatch(lastTabMatches[lastTabIndex], input, ref cursorPosition, prompt, ref promptLeft, ref promptTop);
                    }
                    else
                    {
                        var matches = knownCommands
                            .Where(c => c.StartsWith(current, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(c => c)
                            .ToList();

                        if (matches.Count == 1)
                        {
                            ApplyTabMatch(matches[0], input, ref cursorPosition, prompt, ref promptLeft, ref promptTop);
                            lastTabMatches.Clear(); lastTabIndex = -1;
                        }
                        else if (matches.Count > 1)
                        {
                            lastTabMatches = matches;
                            lastTabIndex = 0;
                            ApplyTabMatch(matches[0], input, ref cursorPosition, prompt, ref promptLeft, ref promptTop);
                        }
                        else
                        {
                            lastTabMatches.Clear(); lastTabIndex = -1;
                        }
                    }
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    lastTabMatches.Clear(); lastTabIndex = -1;
                    if (cursorPosition > 0)
                    {
                        input.Remove(cursorPosition - 1, 1);
                        cursorPosition--;
                        Redraw(prompt, input.ToString(), cursorPosition, ref promptLeft, ref promptTop);
                    }
                }
                else if (key.Key == ConsoleKey.Delete)
                {
                    lastTabMatches.Clear(); lastTabIndex = -1;
                    if (cursorPosition < input.Length)
                    {
                        input.Remove(cursorPosition, 1);
                        Redraw(prompt, input.ToString(), cursorPosition, ref promptLeft, ref promptTop);
                    }
                }
                else if (key.Key == ConsoleKey.LeftArrow)
                {
                    if (cursorPosition > 0)
                    {
                        cursorPosition--;
                        PositionCursor(promptLeft, promptTop, cursorPosition);
                    }
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    if (cursorPosition < input.Length)
                    {
                        cursorPosition++;
                        PositionCursor(promptLeft, promptTop, cursorPosition);
                    }
                }
                else if (key.Key == ConsoleKey.Home)
                {
                    cursorPosition = 0;
                    PositionCursor(promptLeft, promptTop, cursorPosition);
                }
                else if (key.Key == ConsoleKey.End)
                {
                    cursorPosition = input.Length;
                    PositionCursor(promptLeft, promptTop, cursorPosition);
                }
                else if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
                {
                    Console.WriteLine();
                    return string.Empty;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    lastTabMatches.Clear(); lastTabIndex = -1;
                    input.Insert(cursorPosition, key.KeyChar);
                    cursorPosition++;
                    Redraw(prompt, input.ToString(), cursorPosition, ref promptLeft, ref promptTop);
                }
            }
        }

        private static void ApplyTabMatch(string match, StringBuilder input, ref int cursorPosition, string prompt, ref int promptLeft, ref int promptTop)
        {
            input.Clear();
            input.Append(match);
            cursorPosition = input.Length;
            Redraw(prompt, input.ToString(), cursorPosition, ref promptLeft, ref promptTop);
        }

        private static void Redraw(string prompt, string text, int cursorPos, ref int promptLeft, ref int promptTop)
        {
            lock (_consoleLock)
            {
                var bufferWidth = SafeBufferWidth();
                ClearRows(_inputStartTop, _lastDrawnRows, bufferWidth);
                SafeSetCursor(0, _inputStartTop);

                AnsiConsole.Markup(prompt);
                promptLeft = Console.CursorLeft;
                promptTop = Console.CursorTop;

                if (text.Length > 0)
                    Console.Write(text);

                AdjustStartTopAfterWrite(text.Length, promptLeft, ref promptTop);

                int totalLen = promptLeft + text.Length;
                _lastDrawnRows = (totalLen == 0) ? 1 : (totalLen / bufferWidth) + 1;

                PositionCursor(promptLeft, promptTop, cursorPos);
            }
        }

        private static void AdjustStartTopAfterWrite(int textLength, int promptLeft, ref int promptTop)
        {
            int totalLen = promptLeft + textLength;
            if (totalLen <= 0) return;
            int bufferWidth = SafeBufferWidth();
            int expectedBottomRow = promptTop + (totalLen / bufferWidth);
            int actualBottomRow = Console.CursorTop;
            if (actualBottomRow < expectedBottomRow)
            {
                int delta = expectedBottomRow - actualBottomRow;
                _inputStartTop -= delta;
                promptTop -= delta;
                if (_inputStartTop < 0) _inputStartTop = 0;
                if (promptTop < 0) promptTop = 0;
            }
        }

        private static void ClearRows(int startRow, int rowCount, int bufferWidth)
        {
            var blank = new string(' ', Math.Max(0, bufferWidth - 1));
            int height = SafeBufferHeight();
            for (int i = 0; i < rowCount; i++)
            {
                int row = startRow + i;
                if (row < 0 || row >= height) continue;
                SafeSetCursor(0, row);
                Console.Write(blank);
            }
            SafeSetCursor(0, Math.Max(0, Math.Min(startRow, height - 1)));
        }

        private static void PositionCursor(int promptLeft, int promptTop, int cursorPos)
        {
            int bufferWidth = SafeBufferWidth();
            int totalOffset = promptLeft + cursorPos;
            int rowOffset = totalOffset / bufferWidth;
            int col = totalOffset % bufferWidth;
            int row = promptTop + rowOffset;
            SafeSetCursor(col, row);
        }

        private static void SafeSetCursor(int left, int top)
        {
            try
            {
                int w = SafeBufferWidth();
                int h = SafeBufferHeight();
                left = Math.Max(0, Math.Min(left, w - 1));
                top = Math.Max(0, Math.Min(top, h - 1));
                Console.SetCursorPosition(left, top);
            }
            catch { }
        }

        private static int SafeBufferWidth()
        {
            try { return Math.Max(1, Console.BufferWidth); }
            catch { return 80; }
        }

        private static int SafeBufferHeight()
        {
            try { return Math.Max(1, Console.BufferHeight); }
            catch { return 25; }
        }

        [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9_]+)")]
        private static partial Regex CommandRegex();
    }
}