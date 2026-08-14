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

    /// <summary>Есть ли что закрывать штатно при выходе (живые подключения).</summary>
    public bool HasActiveConnections => Sessions.Any(s => s.IsConnected);

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
    /// Штатно закрывает все сессии при выходе из программы (сокеты закрываются
    /// корректно — сервер видит нормальное отключение, а не разрыв). Если отключение
    /// зависло, ждём ограниченное время и выходим — остаток оборвётся сам.
    /// </summary>
    public async Task ShutdownAsync(int timeoutMs = 3000)
    {
        // Сразу убираем вкладки из коллекции: после DisposeAsync сессия непригодна
        // (её SemaphoreSlim уничтожен), и переиспользовать её при повторном выборе
        // сервера нельзя — иначе первая же команда упадёт на disposed-объекте.
        var sessions = Sessions.ToList();
        Sessions.Clear();
        SelectedSession = null;

        var closing = Task.WhenAll(sessions.Select(s => s.DisposeAsync().AsTask()));
        await Task.WhenAny(closing, Task.Delay(timeoutMs));
    }

    private void LoadServers()
    {
        Servers.Clear();
        foreach (var s in _serverStore.Load())
            Servers.Add(s);
    }

    public IEnumerable<string> ServerNames => Servers.Select(s => s.Name);

    public void AddServer(ServerConfig cfg)
    {
        Servers.Add(cfg);
        _serverStore.Save(Servers);
    }

    public void UpdateServer(ServerConfig original, ServerConfig updated)
    {
        original.Name = updated.Name;
        original.IpHost = updated.IpHost;
        original.RconPort = updated.RconPort;
        original.RconPass = updated.RconPass;
        _serverStore.Save(Servers);
        LoadServers(); // пересобираем список, чтобы сайдбар показал новые значения
    }

    public void DeleteServer(ServerConfig server)
    {
        Servers.Remove(server);
        _serverStore.Save(Servers);
        _historyStore.Delete(server.Name); // история этого сервера тоже удаляется
    }

    private async Task OpenSessionAsync(ServerConfig? server)
    {
        if (server is null) return;

        // Вкладка на этот сервер уже открыта — переиспользуем её, не плодим дубли.
        var existing = Sessions.FirstOrDefault(
            s => string.Equals(s.Server.Name, server.Name, StringComparison.Ordinal));
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
