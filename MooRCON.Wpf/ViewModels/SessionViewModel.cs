using System.Windows.Threading;
using MooRCON.Core;
using MooRCON.Wpf.Mvvm;

namespace MooRCON.Wpf.ViewModels;

/// <summary>
/// Одна вкладка = одно независимое RCON-подключение: свой сокет, свой вывод,
/// своя история и свой keep-alive-таймер.
/// </summary>
public sealed class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RconSession _session = new();
    private readonly HistoryStore _historyStore = new();
    private readonly DispatcherTimer _keepAlive;
    private readonly List<string> _history;
    private int _historyIndex = -1;

    private List<string> _availableCommands = new();
    private readonly List<string> _tabMatches = new();
    private int _tabIndex = -1;

    public ServerConfig Server { get; }
    public string Header => Server.Name;
    public string Endpoint => $"{Server.IpHost}:{Server.RconPort}";

    private string _output = "";
    public string Output { get => _output; private set => SetProperty(ref _output, value); }

    private string _input = "";
    public string Input { get => _input; set => SetProperty(ref _input, value); }

    private string _status = "Отключено";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    private bool _keepAliveEnabled = true;
    public bool KeepAliveEnabled
    {
        get => _keepAliveEnabled;
        set { if (SetProperty(ref _keepAliveEnabled, value)) UpdateKeepAlive(); }
    }

    public RelayCommand SendCommand { get; }
    public RelayCommand ReconnectCommand { get; }
    public RelayCommand CloseCommand { get; }

    public event Action<SessionViewModel>? CloseRequested;

    public SessionViewModel(ServerConfig server)
    {
        Server = server;
        _history = _historyStore.Load(server.Name);

        _keepAlive = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _keepAlive.Tick += async (_, _) => await KeepAliveTickAsync();

        SendCommand = new RelayCommand(async _ => await SendAsync(),
                                       _ => IsConnected && !string.IsNullOrWhiteSpace(Input));
        ReconnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected);
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this));
    }

    public async Task ConnectAsync()
    {
        Status = "Подключение...";
        AppendLine($"Подключение к {Endpoint}...");
        try
        {
            await _session.ConnectAndAuthAsync(Server.IpHost, Server.RconPort, Server.RconPass);
            IsConnected = true;
            Status = "Подключено";
            AppendLine("Авторизация успешна.");
            UpdateKeepAlive();
            await LoadCommandsAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Status = "Ошибка подключения";
            AppendLine("Ошибка: " + ex.Message);
        }
    }

    private async Task SendAsync()
    {
        var cmd = Input.Trim();
        if (cmd.Length == 0 || !IsConnected) return;

        Input = "";
        if (_history.Count == 0 || _history[^1] != cmd) _history.Add(cmd);
        _historyIndex = -1;
        AppendLine($"> {cmd}");

        try
        {
            var resp = await _session.ExecuteAsync(cmd);
            if (!string.IsNullOrWhiteSpace(resp))
                AppendLine(TextFormatting.NormalizeNewlines(resp).TrimEnd());
        }
        catch (OperationCanceledException)
        {
            AppendLine("Таймаут выполнения команды (10 сек).");
        }
        catch (Exception ex)
        {
            AppendLine("Ошибка: " + ex.Message);
            SyncConnectionState();
        }
        _historyStore.Save(Server.Name, _history);
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
            SyncConnectionState();
            if (!IsConnected)
                AppendLine("[keep-alive] соединение потеряно.");
        }
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
        catch { /* автодополнение не критично */ }
    }

    // --- Автодополнение по Tab (вызывается из code-behind) ---
    public void Autocomplete()
    {
        var current = Input;
        if (current.Contains(' ')) return;

        // Повторный Tab — циклируем по найденным вариантам.
        if (_tabMatches.Count > 1 && _tabIndex >= 0 &&
            string.Equals(current, _tabMatches[_tabIndex], StringComparison.OrdinalIgnoreCase))
        {
            _tabIndex = (_tabIndex + 1) % _tabMatches.Count;
            Input = _tabMatches[_tabIndex];
            return;
        }

        var matches = _availableCommands
            .Where(c => c.StartsWith(current, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c)
            .ToList();

        if (matches.Count == 0) { ResetCompletion(); return; }

        _tabMatches.Clear();
        _tabMatches.AddRange(matches);
        _tabIndex = 0;
        Input = matches[0];
        if (matches.Count == 1) ResetCompletion();
    }

    public void ResetCompletion()
    {
        _tabMatches.Clear();
        _tabIndex = -1;
    }

    // --- История ввода (вызывается из code-behind по стрелкам ↑/↓) ---
    public void HistoryPrev()
    {
        if (_history.Count == 0) return;
        if (_historyIndex == -1) _historyIndex = _history.Count - 1;
        else if (_historyIndex > 0) _historyIndex--;
        Input = _history[_historyIndex];
    }

    public void HistoryNext()
    {
        if (_history.Count == 0 || _historyIndex == -1) return;
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            Input = _history[_historyIndex];
        }
        else
        {
            _historyIndex = -1;
            Input = "";
        }
    }

    private void SyncConnectionState()
    {
        if (IsConnected != _session.IsConnected)
            IsConnected = _session.IsConnected;
        if (!IsConnected)
        {
            Status = "Соединение потеряно";
            _keepAlive.Stop();
        }
    }

    private void UpdateKeepAlive()
    {
        if (KeepAliveEnabled && IsConnected) _keepAlive.Start();
        else _keepAlive.Stop();
    }

    private void AppendLine(string text)
        => Output += (Output.Length == 0 ? "" : Environment.NewLine) + text;

    public async ValueTask DisposeAsync()
    {
        _keepAlive.Stop();
        _historyStore.Save(Server.Name, _history);
        await _session.DisposeAsync();
    }
}
