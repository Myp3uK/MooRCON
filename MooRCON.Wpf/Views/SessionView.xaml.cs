using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MooRCON.Wpf.ViewModels;

namespace MooRCON.Wpf.Views;

public partial class SessionView : UserControl
{
    private INotifyCollectionChanged? _boundEntries;

    public SessionView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => FocusInput();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) FocusInput();
        else (DataContext as SessionViewModel)?.CloseSuggestions();
    }

    // TabControl переиспользует один SessionView, меняя DataContext при переключении/открытии
    // вкладок. Здесь пересобираем вывод под новую сессию и возвращаем фокус в поле ввода.
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_boundEntries is not null)
            _boundEntries.CollectionChanged -= Entries_CollectionChanged;

        OutputBox.Document.Blocks.Clear();
        _boundEntries = null;

        if (DataContext is SessionViewModel vm)
        {
            foreach (var entry in vm.OutputEntries) AppendParagraph(entry);
            _boundEntries = vm.OutputEntries;
            _boundEntries.CollectionChanged += Entries_CollectionChanged;
            OutputBox.ScrollToEnd();
        }

        FocusInput();
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            foreach (OutputEntry entry in e.NewItems) AppendParagraph(entry);
        else if (e.Action == NotifyCollectionChangedAction.Reset)
            OutputBox.Document.Blocks.Clear();

        OutputBox.ScrollToEnd();
    }

    private void AppendParagraph(OutputEntry entry)
    {
        var run = new Run(entry.Text) { Foreground = BrushFor(entry.Kind) };
        OutputBox.Document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
    }

    private Brush BrushFor(OutputKind kind) => kind switch
    {
        OutputKind.Command => (Brush)FindResource("Brush.Accent"),
        OutputKind.System => (Brush)FindResource("Brush.Text.Muted"),
        _ => (Brush)FindResource("Brush.Text.Log"),
    };

    private void FocusInput()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            InputBox.Focus();
            Keyboard.Focus(InputBox);
            InputBox.CaretIndex = InputBox.Text.Length;
        }), DispatcherPriority.Input);

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

    private void SuggestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestList.SelectedItem != null)
            SuggestList.ScrollIntoView(SuggestList.SelectedItem);
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
                // Если список открыт — подставляем ВЫБРАННУЮ команду, затем сразу отправляем.
                if (vm.SuggestionsOpen)
                {
                    vm.AcceptSuggestion();
                    FocusCaretEnd();
                }
                if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
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
