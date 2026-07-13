using System;
using System.Windows;
using System.Windows.Interop;
using MooRCON.Core;

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

    // > 0, пока открыт модальный диалог: блокируем перемещение основного окна.
    private int _modalDepth;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MOVE = 0xF010;
        if (_modalDepth > 0 && msg == WM_SYSCOMMAND && (int)(wParam.ToInt64() & 0xFFF0) == SC_MOVE)
            handled = true; // не даём двигать окно, пока открыт модальный диалог
        return IntPtr.Zero;
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
        _modalDepth++;
        try
        {
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                vm.AddServer(dialog.Result);
        }
        finally
        {
            _modalDepth--;
        }
    }

    private void OnEditServer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ServerConfig server) return;

        var dialog = new AddServerWindow(vm.ServerNames, server) { Owner = this };
        _modalDepth++;
        try
        {
            if (dialog.ShowDialog() == true && dialog.Result is not null)
                vm.UpdateServer(server, dialog.Result);
        }
        finally
        {
            _modalDepth--;
        }
    }

    private void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ServerConfig server) return;

        var confirm = new ConfirmWindow(
            "Удалить сервер",
            $"Удалить сервер «{server.Name}»?\nИстория команд этого сервера тоже будет удалена.")
        {
            Owner = this
        };

        _modalDepth++;
        try
        {
            if (confirm.ShowDialog() == true)
                vm.DeleteServer(server);
        }
        finally
        {
            _modalDepth--;
        }
    }

    private void UpdateMaximizeGlyph()
        => MaximizeButton.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
}
