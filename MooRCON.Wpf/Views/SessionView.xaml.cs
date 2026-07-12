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
        else (DataContext as SessionViewModel)?.CloseSuggestions();
    }

    private void FocusInput()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }), DispatcherPriority.Input);

    private void OutputBox_TextChanged(object sender, TextChangedEventArgs e)
        => OutputBox.ScrollToEnd();

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        => (DataContext as SessionViewModel)?.UpdateCompletions();

    private void SuggestList_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SessionViewModel vm && vm.AcceptSuggestion())
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }
    }

    /// <summary>
    /// Клавиши на уровне вкладки: навигация по выпадающему списку, Tab-автодополнение,
    /// история по стрелкам; любая печатная клавиша возвращает фокус в поле ввода.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;

        switch (e.Key)
        {
            case Key.Tab:
                if (vm.SuggestionsOpen) vm.AcceptSuggestion();
                else vm.UpdateCompletions();
                FocusCaretEnd();
                e.Handled = true;
                return;

            case Key.Up:
                if (vm.SuggestionsOpen) vm.MoveSelection(-1);
                else vm.ShowHistory();
                FocusCaretEnd();
                e.Handled = true;
                return;

            case Key.Down:
                if (vm.SuggestionsOpen) vm.MoveSelection(+1);
                else vm.ShowHistory();
                FocusCaretEnd();
                e.Handled = true;
                return;

            case Key.Enter:
                // В режиме истории Enter выбирает вариант, иначе — отправляет команду.
                if (vm.SuggestionsOpen && vm.SuggestionKind == SuggestionKind.History)
                {
                    vm.AcceptSuggestion();
                    FocusCaretEnd();
                }
                else
                {
                    vm.CloseSuggestions();
                    if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
                }
                e.Handled = true;
                return;

            case Key.Escape:
                if (vm.SuggestionsOpen) { vm.CloseSuggestions(); e.Handled = true; }
                return;
        }

        // Прочие клавиши: если фокус ушёл из поля — вернуть его при печати.
        if (!InputBox.IsKeyboardFocusWithin && IsTypingKey(e.Key))
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }
    }

    private void FocusCaretEnd()
    {
        if (!InputBox.IsKeyboardFocusWithin) InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
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
