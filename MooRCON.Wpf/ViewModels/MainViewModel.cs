using System.Collections.ObjectModel;
using MooRCON.Core;
using MooRCON.Wpf.Mvvm;

namespace MooRCON.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ServerStore _serverStore = new();
    private readonly HistoryStore _historyStore = new();

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

    public MainViewModel()
    {
        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSessions));
        LoadServers();

        ConnectCommand = new RelayCommand(async p => await OpenSessionAsync(p as ServerConfig));
        RefreshServersCommand = new RelayCommand(_ => LoadServers());
        CloseAllCommand = new RelayCommand(async _ => await CloseAllAsync(), _ => HasSessions);
    }

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

    public void UpdateServer(ServerConfig original, ServerConfig updated)
    {
        var oldName = original.Name;
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

        var vm = new SessionViewModel(server);
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
