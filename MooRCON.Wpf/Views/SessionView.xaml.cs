using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MooRCON.Wpf.ViewModels;

namespace MooRCON.Wpf.Views;

public partial class SessionView : UserControl
{
    public SessionView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => FocusInput();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) FocusInput();
    }

    private void FocusInput()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }), DispatcherPriority.Input);

    private void OutputBox_TextChanged(object sender, TextChangedEventArgs e)
        => OutputBox.ScrollToEnd();

    /// <summary>
    /// Ловим клавиши на уровне всей вкладки: Tab — автодополнение; любая «печатная»
    /// клавиша возвращает фокус в поле ввода, чтобы он не терялся.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;

        if (e.Key == Key.Tab)
        {
            vm.Autocomplete();
            InputBox.CaretIndex = InputBox.Text.Length;
            e.Handled = true; // не уводим фокус по табуляции
            return;
        }

        // Любая другая клавиша сбрасывает цикл автодополнения.
        vm.ResetCompletion();

        // Если фокус ушёл из поля ввода — возвращаем его при печати.
        if (!InputBox.IsKeyboardFocusWithin && IsTypingKey(e.Key))
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;

        if (e.Key == Key.Up)
        {
            vm.HistoryPrev();
            InputBox.CaretIndex = InputBox.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            vm.HistoryNext();
            InputBox.CaretIndex = InputBox.Text.Length;
            e.Handled = true;
        }
    }

    private static bool IsTypingKey(Key k)
    {
        if (k is >= Key.A and <= Key.Z) return true;
        if (k is >= Key.D0 and <= Key.D9) return true;
        if (k is >= Key.NumPad0 and <= Key.Divide) return true;
        return k is Key.Space or Key.Back or Key.OemMinus or Key.OemPlus
                 or Key.OemComma or Key.OemPeriod or Key.OemQuestion or Key.OemTilde
                 or Key.Oem1 or Key.Oem2 or Key.Oem3 or Key.Oem4
                 or Key.Oem5 or Key.Oem6 or Key.Oem7 or Key.OemBackslash;
    }
}
