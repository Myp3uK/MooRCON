using System.Collections.ObjectModel;
using MooRCON.Core;
using MooRCON.Wpf.Mvvm;

namespace MooRCON.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ServerStore _serverStore = new();
    private readonly HistoryStore _historyStore = new();
    private readonly CommandTemplateStore _templateStore = new();
    private readonly UiStateStore _uiStore = new();

    /// <summary>Заготовки команд: один набор на программу, вкладки берут из него свой срез.</summary>
    public CommandTemplateSet Templates { get; private set; }

    /// <summary>Состояние интерфейса между запусками (геометрия окна, открыта ли панель).</summary>
    public UiState Ui { get; }

    /// <summary>
    /// Панель с игроками и заготовками. Общая для всех вкладок: открывается и закрывается
    /// она вместе с шириной окна, и разное состояние по вкладкам дёргало бы окно при
    /// каждом переключении.
    /// </summary>
    public bool PanelOpen
    {
        get => Ui.PanelOpen;
        set
        {
            if (Ui.PanelOpen == value) return;
            Ui.PanelOpen = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ServerConfig> Servers { get; } = new();
    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    private SessionViewModel? _selectedSession;
    public SessionViewModel? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    public bool HasSessions => Sessions.Count > 0;

    public RelayCommand ConnectCommand { get; }
    public RelayCommand RefreshServersCommand { get; }
    public RelayCommand CloseAllCommand { get; }
    public RelayCommand TogglePanelCommand { get; }

    public MainViewModel()
    {
        Templates = _templateStore.Load();
        Ui = _uiStore.Load();
        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSessions));
        LoadServers();

        ConnectCommand = new RelayCommand(async p => await OpenSessionAsync(p as ServerConfig));
        RefreshServersCommand = new RelayCommand(_ => LoadServers());
        CloseAllCommand = new RelayCommand(async _ => await CloseAllAsync(), _ => HasSessions);
        TogglePanelCommand = new RelayCommand(_ => PanelOpen = !PanelOpen);
    }

    /// <summary>Сохраняет состояние интерфейса — вызывается при выходе.</summary>
    public void SaveUi() => _uiStore.Save(Ui);

    private async Task CloseAllAsync()
    {
        foreach (var vm in Sessions.ToList())
        {
            Sessions.Remove(vm);
            await vm.DisposeAsync();
        }
        SelectedSession = null;
    }

    /// <summary>
    /// Закрывает сокеты всех сессий при выходе из программы — синхронно и мгновенно,
    /// чтобы сервер увидел нормальное отключение, а не разрыв. Окно при этом закрывается
    /// без задержки. Всё обёрнуто в try/catch: сбой отключения не должен мешать выходу.
    /// </summary>
    public void DisconnectAllOnExit()
    {
        foreach (var s in Sessions)
        {
            try { s.CloseConnection(); } catch { /* выходу это мешать не должно */ }
        }
    }

    /// <summary>
    /// Ошибка чтения servers.json, о которой ещё не сообщили пользователю (окно покажет
    /// её один раз и сбросит). Сам файл к этому моменту уже сохранён копией.
    /// </summary>
    public string? ServersLoadError { get; set; }

    private void LoadServers()
    {
        Servers.Clear();
        foreach (var s in _serverStore.Load())
            Servers.Add(s);
        if (_serverStore.LoadError is not null)
        {
            ServersLoadError = _serverStore.LoadError;
            OnPropertyChanged(nameof(ServersLoadError));
        }
    }

    public IEnumerable<string> ServerNames => Servers.Select(s => s.Name);

    public void AddServer(ServerConfig cfg)
    {
        Servers.Add(cfg);
        _serverStore.Save(Servers);
    }

    /// <summary>Сохраняет правки заготовок и раздаёт новый набор открытым вкладкам.</summary>
    public void SaveTemplates(CommandTemplateSet templates)
    {
        Templates = templates;
        _templateStore.Save(templates);
        foreach (var session in Sessions) session.ApplyTemplates(templates);
    }

    public void UpdateServer(ServerConfig original, ServerConfig updated)
    {
        var oldName = original.Name;

        // Заготовки тоже привязаны к серверу по имени — переносим настройку вслед.
        if (!string.Equals(oldName, updated.Name, StringComparison.Ordinal))
        {
            Templates.RenameServer(oldName, updated.Name);
            _templateStore.Save(Templates);
        }

        original.Name = updated.Name;
        original.IpHost = updated.IpHost;
        original.RconPort = updated.RconPort;
        original.RconPass = updated.RconPass;
        _serverStore.Save(Servers);
        // История привязана к имени — переносим её вслед за переименованием. Открытая
        // вкладка держит тот же объект original, так что допишет историю уже под новым именем.
        _historyStore.Rename(oldName, updated.Name);
        LoadServers(); // пересобираем список, чтобы сайдбар показал новые значения
    }

    public async Task DeleteServerAsync(ServerConfig server)
    {
        // Открытую вкладку удаляемого сервера закрываем без сохранения истории —
        // иначе при закрытии она записала бы историю обратно под удалённым именем.
        var session = FindSession(server.Name);
        if (session is not null)
        {
            session.DiscardHistory();
            Sessions.Remove(session);
            await session.DisposeAsync();
        }

        Servers.Remove(server);
        _serverStore.Save(Servers);
        _historyStore.Delete(server.Name); // история этого сервера тоже удаляется

        Templates.RemoveServer(server.Name); // и его список показываемых заготовок
        _templateStore.Save(Templates);
    }

    private SessionViewModel? FindSession(string serverName) => Sessions.FirstOrDefault(
        s => string.Equals(s.Server.Name, serverName, StringComparison.Ordinal));

    private async Task OpenSessionAsync(ServerConfig? server)
    {
        if (server is null) return;

        // Вкладка на этот сервер уже открыта — переиспользуем её, не плодим дубли.
        var existing = FindSession(server.Name);
        if (existing is not null)
        {
            SelectedSession = existing;
            existing.RequestFocus(); // повторный выбор — вернуть фокус в поле ввода
            return;
        }

        var vm = new SessionViewModel(server, Templates);
        vm.CloseRequested += async s => await CloseSessionAsync(s);
        Sessions.Add(vm);
        SelectedSession = vm;
        await vm.ConnectAsync();
    }

    private async Task CloseSessionAsync(SessionViewModel vm)
    {
        Sessions.Remove(vm);
        await vm.DisposeAsync();
    }
}
