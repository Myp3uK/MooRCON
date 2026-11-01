using System.Collections.ObjectModel;
using System.Windows.Threading;
using MooRCON.Core;
using MooRCON.Wpf.Mvvm;

namespace MooRCON.Wpf.ViewModels;

public enum SuggestionKind { Completion, History }

/// <summary>
/// Одна вкладка = одно независимое RCON-подключение: свой сокет, свой вывод,
/// своя история и свой keep-alive-таймер.
/// </summary>
public sealed class SessionViewModel : ObservableObject, IAsyncDisposable
{
    // У RCON Conan бывает, что верный пароль отвергается или соединение срывается на
    // ровном месте — поэтому подключение повторяем несколько раз, включая отказ авторизации.
    private const int MaxConnectAttempts = 3;
    private const int RetryDelayMs = 1000;

    // Сколько строк вывода держать во вкладке: без предела вкладка, открытая сутками,
    // раздувает RichTextBox и окно начинает тормозить.
    private const int MaxOutputLines = 5000;

    private readonly RconSession _session = new();
    private readonly HistoryStore _historyStore = new();
    private readonly DispatcherTimer _keepAlive;
    private readonly List<string> _history;
    private List<string> _availableCommands = new();
    private bool _suppressCompletion;

    // Отменяется при закрытии вкладки: останавливает повторы подключения.
    private readonly CancellationTokenSource _lifetime = new();
    // Идёт подключение — повторный запуск (keep-alive, таймаут, кнопка) пропускаем.
    private bool _connecting;
    // Сервер удалён — историю больше не сохраняем, иначе она воскреснет в history.json.
    private bool _historyDiscarded;

    public ServerConfig Server { get; }
    public string Header => Server.Name;
    public string Endpoint => $"{Server.IpHost}:{Server.RconPort}";

    public ObservableCollection<OutputEntry> OutputEntries { get; } = new();

    private string _input = "";
    public string Input { get => _input; set => SetProperty(ref _input, value); }

    private string _status = "Отключено";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }

    private bool _keepAliveEnabled = true;
    public bool KeepAliveEnabled
    {
        get => _keepAliveEnabled;
        set { if (SetProperty(ref _keepAliveEnabled, value)) UpdateKeepAlive(); }
    }

    // --- Выпадающий список: автодополнение команд и история ввода ---
    public ObservableCollection<string> Suggestions { get; } = new();

    private bool _suggestionsOpen;
    public bool SuggestionsOpen { get => _suggestionsOpen; private set => SetProperty(ref _suggestionsOpen, value); }

    private int _suggestionIndex = -1;
    public int SuggestionIndex { get => _suggestionIndex; set => SetProperty(ref _suggestionIndex, value); }

    public SuggestionKind SuggestionKind { get; private set; }

    // --- Игроки на сервере и заготовки команд ---
    public ObservableCollection<PlayerInfo> Players { get; } = new();

    private PlayerInfo? _selectedPlayer;
    public PlayerInfo? SelectedPlayer
    {
        get => _selectedPlayer;
        set { if (SetProperty(ref _selectedPlayer, value)) OnPropertyChanged(nameof(TemplatesHint)); }
    }

    /// <summary>
    /// Строка под кнопками заготовок: к кому они сейчас применятся. Текст меняется, но
    /// сама строка есть всегда — иначе при выборе игрока список над ней прыгал бы.
    /// </summary>
    public string TemplatesHint => SelectedPlayer is null
        ? "Выберите игрока — команды по игроку включатся."
        : $"Команды применятся к: {SelectedPlayer.Display}";

    public ObservableCollection<CommandTemplate> Templates { get; } = new();

    private string _playersStatus = "нет данных";
    public string PlayersStatus { get => _playersStatus; private set => SetProperty(ref _playersStatus, value); }

    /// <summary>Выполняется заготовка — чтобы второй клик не отправил команду дважды.</summary>
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>Вью спрашивает у пользователя значения параметров шаблона; null — отмена.</summary>
    public Func<CommandTemplate, IReadOnlyList<TemplateParameter>, Dictionary<string, string>?>? ParameterPrompt { get; set; }

    /// <summary>Вью просит подтвердить отправку уже собранной команды.</summary>
    public Func<CommandTemplate, string, bool>? ConfirmPrompt { get; set; }

    public RelayCommand SendCommand { get; }
    public RelayCommand ReconnectCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand RefreshPlayersCommand { get; }
    public RelayCommand RunTemplateCommand { get; }

    public event Action<SessionViewModel>? CloseRequested;

    /// <summary>Просьба вью вернуть фокус в поле ввода (напр. при повторном выборе сервера).</summary>
    public event Action? FocusRequested;
    public void RequestFocus() => FocusRequested?.Invoke();

    public SessionViewModel(ServerConfig server, CommandTemplateSet templates)
    {
        Server = server;
        _history = _historyStore.Load(server.Name);

        _keepAlive = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _keepAlive.Tick += async (_, _) => await KeepAliveTickAsync();

        SendCommand = new RelayCommand(async _ => await SendAsync(),
                                       _ => IsConnected && !string.IsNullOrWhiteSpace(Input));
        ReconnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected);
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this));
        RefreshPlayersCommand = new RelayCommand(async _ => await RefreshPlayersAsync(),
                                                 _ => IsConnected && !IsBusy);
        RunTemplateCommand = new RelayCommand(async p => await RunTemplateAsync(p as CommandTemplate),
                                              p => CanRunTemplate(p as CommandTemplate));

        ApplyTemplates(templates);
    }

    public async Task ConnectAsync()
    {
        if (_connecting || IsClosed) return;
        _connecting = true;
        try
        {
            await ConnectWithRetriesAsync();
        }
        finally
        {
            _connecting = false;
        }
    }

    private bool IsClosed => _lifetime.IsCancellationRequested;

    private async Task ConnectWithRetriesAsync()
    {
        Status = "Подключение...";
        AppendLine($"Подключение к {Endpoint}...");

        for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            try
            {
                await _session.ConnectAndAuthAsync(Server.IpHost, Server.RconPort, Server.RconPass);
                if (IsClosed) return; // вкладку закрыли, пока шла авторизация
                IsConnected = true;
                Status = "Подключено";
                AppendLine("Авторизация успешна.");
                UpdateKeepAlive();
                await LoadCommandsAsync();
                await RefreshPlayersAsync();
                return;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                if (IsClosed) return; // сессия закрыта — ошибка ожидаема, повторять некуда
                if (attempt < MaxConnectAttempts)
                {
                    AppendLine($"Попытка {attempt}/{MaxConnectAttempts} не удалась: {ex.Message}. Повтор...");
                    try { await Task.Delay(RetryDelayMs, _lifetime.Token); }
                    catch (OperationCanceledException) { return; }
                }
                else
                {
                    Status = "Ошибка подключения";
                    AppendLine($"Не удалось подключиться за {MaxConnectAttempts} попыт.: {ex.Message}");
                }
            }
        }
    }

    private async Task SendAsync()
    {
        var cmd = Input.Trim();
        if (cmd.Length == 0 || !IsConnected) return;

        CloseSuggestions();
        Input = "";
        AddToHistory(cmd);
        await ExecuteAsync(cmd, echo: true);
        SaveHistory();
    }

    /// <summary>
    /// Общий путь отправки команды — и для ручного ввода, и для заготовок, и для
    /// служебных запросов вроде listplayers. Возвращает ответ сервера или null, если
    /// команда не ушла. При <paramref name="echo"/> = false ответ в вывод не попадает:
    /// служебные запросы не должны засорять консоль.
    /// </summary>
    private async Task<string?> ExecuteAsync(string cmd, bool echo)
    {
        if (!IsConnected) return null;
        if (echo) AppendLine($"> {cmd}", OutputKind.Command);

        try
        {
            var resp = await _session.ExecuteAsync(cmd);
            if (echo && !string.IsNullOrWhiteSpace(resp))
                AppendLine(TextFormatting.NormalizeNewlines(resp).TrimEnd(), OutputKind.Response);
            return resp;
        }
        catch (OperationCanceledException)
        {
            // Ответ не дочитан до конца — поток пакетов рассинхронизирован, дальше
            // по этому сокету доверять нечему: поднимаем соединение заново.
            AppendLine("Таймаут выполнения команды (10 сек).");
            SaveHistory();
            await ReconnectAfterFailureAsync();
            return null;
        }
        catch (Exception ex)
        {
            AppendLine("Ошибка: " + ex.Message);
            SyncConnectionState();
            return null;
        }
    }

    private void AddToHistory(string cmd)
    {
        if (_history.Count == 0 || _history[^1] != cmd) _history.Add(cmd);
    }

    /// <summary>Сбрасывает сломанное соединение и поднимает новое, не теряя вкладку.</summary>
    private async Task ReconnectAfterFailureAsync()
    {
        if (_connecting || IsClosed) return; // переподключение уже идёт или вкладка закрыта
        _keepAlive.Stop();
        _session.Disconnect();
        IsConnected = false;
        await ConnectAsync();
    }

    private async Task LoadCommandsAsync()
    {
        try
        {
            var help = await _session.ExecuteAsync("help", 5000);
            var cmds = HelpCommandParser.Parse(help);
            if (cmds.Count > 0)
            {
                _availableCommands = cmds;
                AppendLine($"Загружено команд для автодополнения: {cmds.Count}");
            }
        }
        catch (Exception ex)
        {
            // Само автодополнение некритично, но сорвавшийся help закрывает сессию:
            // поток пакетов после недочитанного ответа уже не сходится.
            if (!_session.IsConnected)
            {
                AppendLine("Не удалось загрузить список команд: " + ex.Message);
                SyncConnectionState();
            }
        }
    }

    private async Task KeepAliveTickAsync()
    {
        if (!IsConnected || !KeepAliveEnabled) return;
        try
        {
            // Молчаливый health-check: держим сессию живой, вывод не засоряем.
            await _session.ExecuteAsync("help", 5000);
        }
        catch
        {
            AppendLine("[keep-alive] соединение потеряно, переподключение...");
            await ReconnectAfterFailureAsync();
        }
    }

    // ==================== Игроки и заготовки команд ====================

    /// <summary>Перезаполняет кнопки заготовок набором, разрешённым для этого сервера.</summary>
    public void ApplyTemplates(CommandTemplateSet templates)
    {
        Templates.Clear();
        foreach (var template in templates.ForServer(Server.Name)) Templates.Add(template);
    }

    /// <summary>
    /// Перечитывает список игроков. listplayers в вывод не пишем: список обновляется
    /// часто (в том числе перед каждой командой по игроку), а консоль нужна для команд
    /// пользователя.
    /// </summary>
    public async Task<List<PlayerInfo>> RefreshPlayersAsync()
    {
        var response = await ExecuteAsync("listplayers", echo: false);
        if (response is null)
        {
            PlayersStatus = IsConnected ? "не удалось обновить" : "нет связи";
            return new();
        }

        var players = PlayerListParser.Parse(response);
        var chosen = SelectedPlayer;

        Players.Clear();
        foreach (var player in players) Players.Add(player);

        // Выбор держим за самим игроком, а не за позицией в списке: позиции сдвигаются.
        SelectedPlayer = chosen is null ? null : Players.FirstOrDefault(p => p.SameAs(chosen));
        PlayersStatus = players.Count > 0 ? $"{players.Count} онлайн" : "никого";

        return players;
    }

    private bool CanRunTemplate(CommandTemplate? template)
        => template is not null && IsConnected && !IsBusy
           && (!template.NeedsPlayer || SelectedPlayer is not null);

    /// <summary>
    /// Выполняет заготовку: спрашивает параметры, перечитывает listplayers и берёт оттуда
    /// свежий индекс выбранного игрока. Индекс летучий — брать его из показанного списка
    /// нельзя, за это время игроки могли зайти или выйти, и команда ушла бы не тому.
    /// </summary>
    private async Task RunTemplateAsync(CommandTemplate? template)
    {
        if (template is null || !CanRunTemplate(template)) return;

        var parameters = CommandTemplateRenderer.FindParameters(template.Template);
        Dictionary<string, string>? args = null;
        if (parameters.Count > 0)
        {
            args = ParameterPrompt?.Invoke(template, parameters);
            if (args is null) return; // пользователь отменил ввод
        }

        IsBusy = true;
        try
        {
            PlayerInfo? player = null;
            if (template.NeedsPlayer)
            {
                var chosen = SelectedPlayer;
                if (chosen is null) return;

                var fresh = await RefreshPlayersAsync();
                player = fresh.FirstOrDefault(p => p.SameAs(chosen));
                if (player is null)
                {
                    AppendLine($"Игрок «{chosen.Display}» больше не в сети — команда не отправлена.");
                    return;
                }
            }

            var commands = CommandTemplateRenderer.RenderLines(template.Template, player, args);
            if (commands.Count == 0) return;

            // Подтверждение показываем на уже собранных командах — видно, что именно уйдёт.
            if (template.Confirm && ConfirmPrompt?.Invoke(template, string.Join("\n", commands)) == false)
                return;

            // Команды цепочки уходят по очереди: следующая — только после ответа на
            // предыдущую. Сорвалась одна — дальше не идём, продолжение уже бессмысленно.
            for (int i = 0; i < commands.Count; i++)
            {
                AddToHistory(commands[i]);
                if (await ExecuteAsync(commands[i], echo: true) is null)
                {
                    if (commands.Count > 1)
                        AppendLine($"Цепочка прервана: выполнено {i} из {commands.Count}.");
                    break;
                }
            }

            SaveHistory();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== Выпадающий список ====================

    /// <summary>Обновляет список автодополнения по текущему вводу (вызывается при печати).</summary>
    public void UpdateCompletions()
    {
        if (_suppressCompletion) { _suppressCompletion = false; return; }

        var token = Input;
        if (string.IsNullOrEmpty(token) || token.Contains(' ') || _availableCommands.Count == 0)
        {
            CloseSuggestions();
            return;
        }

        var matches = _availableCommands
            .Where(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c, token, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        if (matches.Count == 0) { CloseSuggestions(); return; }
        Show(matches, SuggestionKind.Completion);
    }

    /// <summary>Показывает историю недавних команд (вызывается по стрелке вверх).</summary>
    public void ShowHistory()
    {
        if (_history.Count == 0) return;

        // Уникальные; самые свежие — внизу списка (ближе к полю ввода).
        var items = new List<string>();
        for (int i = _history.Count - 1; i >= 0 && items.Count < 15; i--)
            if (!items.Contains(_history[i])) items.Add(_history[i]);
        items.Reverse();

        Show(items, SuggestionKind.History, selectLast: true);
    }

    private void Show(IReadOnlyList<string> items, SuggestionKind kind, bool selectLast = false)
    {
        SuggestionKind = kind;
        Suggestions.Clear();
        foreach (var it in items) Suggestions.Add(it);
        SuggestionIndex = Suggestions.Count == 0 ? -1 : (selectLast ? Suggestions.Count - 1 : 0);
        SuggestionsOpen = Suggestions.Count > 0;
    }

    public void MoveSelection(int delta)
    {
        if (!SuggestionsOpen || Suggestions.Count == 0) return;
        int idx = SuggestionIndex + delta;
        if (idx < 0) idx = Suggestions.Count - 1;
        else if (idx >= Suggestions.Count) idx = 0;
        SuggestionIndex = idx;
    }

    /// <summary>Подставляет выбранный вариант в поле ввода.</summary>
    public bool AcceptSuggestion()
    {
        if (!SuggestionsOpen || SuggestionIndex < 0 || SuggestionIndex >= Suggestions.Count)
            return false;

        _suppressCompletion = true; // не переоткрывать список от программной смены Input
        Input = Suggestions[SuggestionIndex];
        CloseSuggestions();
        return true;
    }

    public void CloseSuggestions()
    {
        SuggestionsOpen = false;
        SuggestionIndex = -1;
    }

    // ==========================================================

    private void SyncConnectionState()
    {
        if (IsConnected != _session.IsConnected)
            IsConnected = _session.IsConnected;
        if (!IsConnected)
        {
            Status = "Соединение потеряно";
            PlayersStatus = "нет связи";
            _keepAlive.Stop();
        }
    }

    private void UpdateKeepAlive()
    {
        if (KeepAliveEnabled && IsConnected) _keepAlive.Start();
        else _keepAlive.Stop();
    }

    private void AppendLine(string text, OutputKind kind = OutputKind.System)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            OutputEntries.Add(new OutputEntry(line, kind));

        while (OutputEntries.Count > MaxOutputLines)
            OutputEntries.RemoveAt(0);
    }

    private void SaveHistory()
    {
        if (!_historyDiscarded) _historyStore.Save(Server.Name, _history);
    }

    /// <summary>Сервер удаляется: историю этой вкладки больше не сохранять.</summary>
    public void DiscardHistory() => _historyDiscarded = true;

    /// <summary>
    /// Синхронное закрытие при выходе из программы: сохраняем историю и штатно закрываем
    /// сокет (сервер видит нормальное отключение). Сессию не диспозим — она остаётся
    /// пригодной, если окно почему-то не закроется.
    /// </summary>
    public void CloseConnection()
    {
        _keepAlive.Stop();
        SaveHistory();
        _session.Disconnect();
        IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _keepAlive.Stop();
        SaveHistory();
        await _session.DisposeAsync();
    }
}
