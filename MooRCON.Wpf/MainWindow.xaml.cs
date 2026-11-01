using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using MooRCON.Core;

namespace MooRCON.Wpf;

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = ""; // Segoe MDL2 «развернуть»
    private const string RestoreGlyph = "";  // Segoe MDL2 «восстановить»

    /// <summary>Ширина боковой панели вместе с отступом до консоли (см. SessionView.xaml).</summary>
    private const double PanelWidth = 312;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeGlyph();

        if (DataContext is ViewModels.MainViewModel vm)
        {
            RestoreGeometry(vm.Ui);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ViewModels.MainViewModel.PanelOpen))
                    ResizeForPanel(vm.PanelOpen);
                else if (args.PropertyName == nameof(ViewModels.MainViewModel.ServersLoadError))
                    ShowServersLoadError(vm);
            };

            // Ошибка могла случиться ещё до показа окна — проверяем, когда оно появилось.
            Loaded += (_, _) => ShowServersLoadError(vm);
        }
    }

    private void ShowServersLoadError(ViewModels.MainViewModel vm)
    {
        var error = vm.ServersLoadError;
        if (error is null) return;
        vm.ServersLoadError = null;

        ShowModal(new ConfirmWindow("Ошибка списка серверов", error, "OK", showCancel: false));
    }

    /// <summary>
    /// Возвращает окно туда, где его закрыли. Если сохранённый прямоугольник больше не
    /// попадает на экран (монитор отключили, сменилось разрешение), открываемся как обычно —
    /// по центру.
    /// </summary>
    private void RestoreGeometry(UiState ui)
    {
        if (!ui.HasGeometry) return;

        var saved = new Rect(ui.WindowLeft!.Value, ui.WindowTop!.Value,
                             Math.Max(ui.WindowWidth!.Value, MinWidth),
                             Math.Max(ui.WindowHeight!.Value, MinHeight));
        if (!IsOnScreen(saved)) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = saved.Left;
        Top = saved.Top;
        Width = saved.Width;
        Height = saved.Height;
        if (ui.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void CaptureGeometry(UiState ui)
    {
        // У развёрнутого и свёрнутого окна Left/Top/Width/Height — не та геометрия, в которую
        // нужно возвращаться: RestoreBounds даёт размеры нормального состояния.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        ui.WindowLeft = bounds.Left;
        ui.WindowTop = bounds.Top;
        ui.WindowWidth = bounds.Width;
        ui.WindowHeight = bounds.Height;
        ui.WindowMaximized = WindowState == WindowState.Maximized;
    }

    /// <summary>
    /// Раздвигает окно под открывшуюся панель и собирает обратно при её закрытии: иначе
    /// панель отъедала бы место у консоли. Развёрнутое окно не трогаем — там места хватает.
    /// </summary>
    private void ResizeForPanel(bool open)
    {
        if (WindowState != WindowState.Normal) return;

        var screens = VirtualScreen;
        double width = Math.Max(MinWidth, Width + (open ? PanelWidth : -PanelWidth));
        Width = Math.Min(width, screens.Width);

        // Если правый край уехал за пределы экранов — подвинем окно левее.
        if (Left + Width > screens.Right) Left = Math.Max(screens.Left, screens.Right - Width);
    }

    private static bool IsOnScreen(Rect window)
    {
        window.Intersect(VirtualScreen);
        return window.Width >= 200 && window.Height >= 100;
    }

    private static Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

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

    /// <summary>
    /// Показывает модальный диалог поверх окна. Пока он открыт, основное окно не даёт
    /// себя двигать (см. <see cref="WndProc"/>) — отсюда учёт глубины.
    /// </summary>
    internal bool ShowModal(Window dialog)
    {
        dialog.Owner = this;
        _modalDepth++;
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            _modalDepth--;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Синхронно и мгновенно закрываем сокеты (сервер видит нормальное отключение,
        // а не разрыв) и сразу даём окну закрыться — закрытие не отменяем.
        if (DataContext is ViewModels.MainViewModel vm)
        {
            CaptureGeometry(vm.Ui);
            vm.SaveUi();
            vm.DisconnectAllOnExit();
        }
        base.OnClosing(e);
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnAddServer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;

        var dialog = new AddServerWindow(vm.ServerNames);
        if (ShowModal(dialog) && dialog.Result is not null)
            vm.AddServer(dialog.Result);
    }

    private void OnEditServer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ServerConfig server) return;

        var dialog = new AddServerWindow(vm.ServerNames, server);
        if (ShowModal(dialog) && dialog.Result is not null)
            vm.UpdateServer(server, dialog.Result);
    }

    private async void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ServerConfig server) return;

        var confirm = new ConfirmWindow(
            "Удалить сервер",
            $"Удалить сервер «{server.Name}»?\nИстория команд этого сервера тоже будет удалена.");

        if (ShowModal(confirm))
            await vm.DeleteServerAsync(server);
    }

    private void UpdateMaximizeGlyph()
        => MaximizeButton.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
}
