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
    private readonly RconSession _session = new();
    private readonly HistoryStore _historyStore = new();
    private readonly DispatcherTimer _keepAlive;
    private readonly List<string> _history;
    private List<string> _availableCommands = new();
    private bool _suppressCompletion;

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
    public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }

    private bool _keepAliveEnabled = true;
    public bool KeepAliveEnabled
    {
        get => _keepAliveEnabled;
        set { if (SetProperty(ref _keepAliveEnabled, value)) UpdateKeepAlive(); }
    }

    private bool _wrapOutput;
    public bool WrapOutput { get => _wrapOutput; set => SetProperty(ref _wrapOutput, value); }

    // --- Выпадающий список: автодополнение команд и история ввода ---
    public ObservableCollection<string> Suggestions { get; } = new();

    private bool _suggestionsOpen;
    public bool SuggestionsOpen { get => _suggestionsOpen; private set => SetProperty(ref _suggestionsOpen, value); }

    private int _suggestionIndex = -1;
    public int SuggestionIndex { get => _suggestionIndex; set => SetProperty(ref _suggestionIndex, value); }

    public SuggestionKind SuggestionKind { get; private set; }

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

        CloseSuggestions();
        Input = "";
        if (_history.Count == 0 || _history[^1] != cmd) _history.Add(cmd);
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
