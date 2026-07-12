using System.Windows;

namespace MooRCON.Wpf;

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = ""; // Segoe MDL2 «развернуть»
    private const string RestoreGlyph = "";  // Segoe MDL2 «восстановить»

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnAddServer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;

        var dialog = new AddServerWindow(vm.ServerNames) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
            vm.AddServer(dialog.Result);
    }

    private void UpdateMaximizeGlyph()
        => MaximizeButton.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
}
