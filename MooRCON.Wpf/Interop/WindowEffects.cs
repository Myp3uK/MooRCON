using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MooRCON.Wpf.Interop;

/// <summary>
/// Присоединяемые свойства для тонкой настройки окна через DWM.
/// Применяются в стиле Window.Main, поэтому распространяются на все окна сразу.
/// </summary>
public static class WindowEffects
{
    // Windows 11 по умолчанию скругляет углы всех верхнеуровневых окон на уровне DWM.
    // Отключаем это, сохраняя тень (её даёт GlassFrameThickness в WindowChrome).
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static readonly DependencyProperty SquareCornersProperty =
        DependencyProperty.RegisterAttached(
            "SquareCorners", typeof(bool), typeof(WindowEffects),
            new PropertyMetadata(false, OnSquareCornersChanged));

    public static void SetSquareCorners(DependencyObject o, bool value) => o.SetValue(SquareCornersProperty, value);
    public static bool GetSquareCorners(DependencyObject o) => (bool)o.GetValue(SquareCornersProperty);

    private static void OnSquareCornersChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not Window window || !(bool)e.NewValue) return;

        // Хендла окна ещё может не быть, пока оно не инициализировано, — применяем по готовности.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int pref = DWMWCP_DONOTROUND;
        // На Windows 10 атрибут не поддерживается — вызов просто вернёт ошибку, игнорируем.
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }
}
