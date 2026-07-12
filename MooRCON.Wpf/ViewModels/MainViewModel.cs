using System.Collections.ObjectModel;
using MooRCON.Core;
using MooRCON.Wpf.Mvvm;

namespace MooRCON.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ServerStore _serverStore = new();

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

    public MainViewModel()
    {
        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSessions));
        LoadServers();

        ConnectCommand = new RelayCommand(async p => await OpenSessionAsync(p as ServerConfig));
        RefreshServersCommand = new RelayCommand(_ => LoadServers());
    }

    private void LoadServers()
    {
        Servers.Clear();
        foreach (var s in _serverStore.Load())
            Servers.Add(s);
    }

    private async Task OpenSessionAsync(ServerConfig? server)
    {
        if (server is null) return;

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
