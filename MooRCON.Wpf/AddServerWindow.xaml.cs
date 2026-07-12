using System.Windows;
using MooRCON.Core;

namespace MooRCON.Wpf;

public partial class AddServerWindow : Window
{
    private readonly HashSet<string> _existingNames;

    public ServerConfig? Result { get; private set; }

    public AddServerWindow(IEnumerable<string> existingNames)
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var host = HostBox.Text.Trim();
        var portText = PortBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name)) { ShowError("Введите имя сервера."); return; }
        if (_existingNames.Contains(name)) { ShowError("Сервер с таким именем уже существует."); return; }
        if (string.IsNullOrWhiteSpace(host)) { ShowError("Введите адрес сервера."); return; }
        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        {
            ShowError("Порт должен быть числом от 1 до 65535.");
            return;
        }

        Result = new ServerConfig
        {
            Name = name,
            IpHost = host,
            RconPort = port,
            RconPass = PassBox.Password
        };
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
