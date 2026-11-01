using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MooRCON.Core;
using MooRCON.Wpf.ViewModels;

namespace MooRCON.Wpf.Views;

public partial class SessionView : UserControl
{
    private INotifyCollectionChanged? _boundEntries;
    private SessionViewModel? _boundVm;

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
        if (_boundVm is not null)
        {
            _boundVm.FocusRequested -= OnFocusRequested;
            _boundVm.ParameterPrompt = null;
            _boundVm.ConfirmPrompt = null;
        }

        OutputBox.Document.Blocks.Clear();
        _boundEntries = null;
        _boundVm = null;

        if (DataContext is SessionViewModel vm)
        {
            foreach (var entry in vm.OutputEntries) AppendParagraph(entry);
            _boundEntries = vm.OutputEntries;
            _boundEntries.CollectionChanged += Entries_CollectionChanged;
            _boundVm = vm;
            _boundVm.FocusRequested += OnFocusRequested;
            // Диалоги — дело вью: модель только просит спросить у пользователя.
            _boundVm.ParameterPrompt = AskParameters;
            _boundVm.ConfirmPrompt = AskConfirm;
            OutputBox.ScrollToEnd();
        }

        FocusInput();
    }

    private void OnFocusRequested() => FocusInput();

    // ==================== Заготовки команд ====================

    /// <summary>Спрашивает значения параметров шаблона; null — пользователь отменил.</summary>
    private Dictionary<string, string>? AskParameters(CommandTemplate template,
                                                      IReadOnlyList<TemplateParameter> parameters)
    {
        var player = (DataContext as SessionViewModel)?.SelectedPlayer;
        var dialog = new TemplateParamsWindow(template, parameters, player);
        return ShowModal(dialog) ? dialog.Values : null;
    }

    private bool AskConfirm(CommandTemplate template, string commands)
    {
        int count = commands.Split('\n').Length;
        var question = count > 1 ? $"Отправить цепочку из {count} команд?" : "Отправить команду?";
        return ShowModal(new ConfirmWindow("Подтверждение", $"{question}\n\n{commands}", "Отправить"));
    }

    private void OnEditTemplates(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;
        if (Window.GetWindow(this) is not MainWindow main) return;
        if (main.DataContext is not MainViewModel mainVm) return;

        var dialog = new CommandTemplatesWindow(mainVm.Templates, vm.Server.Name);
        if (main.ShowModal(dialog) && dialog.Result is not null)
            mainVm.SaveTemplates(dialog.Result);
    }

    /// <summary>
    /// Модальные диалоги показываем через главное окно: пока они открыты, оно блокирует
    /// своё перемещение.
    /// </summary>
    private bool ShowModal(Window dialog)
        => Window.GetWindow(this) is MainWindow main
            ? main.ShowModal(dialog)
            : dialog.ShowDialog() == true;

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            foreach (OutputEntry entry in e.NewItems) AppendParagraph(entry);
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null
                 && e.OldStartingIndex == 0)
        {
            // Вью-модель срезает старые строки сверху (лимит вывода), поэтому снимаем абзацы
            // с начала документа — это дёшево, в отличие от поиска по индексу.
            var blocks = OutputBox.Document.Blocks;
            for (int i = 0; i < e.OldItems.Count && blocks.FirstBlock is not null; i++)
                blocks.Remove(blocks.FirstBlock);
            return; // срез сверху не должен дёргать прокрутку
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
            OutputBox.Document.Blocks.Clear();

        OutputBox.ScrollToEnd();
    }

    // Порядок важен: абзац сначала попадает в документ и только потом получает Run.
    // Если собрать абзац целиком «на стороне», Run не унаследует FontSize от RichTextBox
    // и останется с дефолтными 12 вместо 13 — текст такой вкладки выглядит мельче.
    // Заметно это после закрытия вкладки: TabControl создаёт новый SessionView, и вывод
    // пересобирается, пока вью ещё не в дереве (при обычном переключении вкладок вью
    // переиспользуется и проблема не проявляется).
    private void AppendParagraph(OutputEntry entry)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        OutputBox.Document.Blocks.Add(paragraph);
        paragraph.Inlines.Add(new Run(entry.Text) { Foreground = BrushFor(entry.Kind) });
    }

    private Brush BrushFor(OutputKind kind) => kind switch
    {
        OutputKind.Command => (Brush)FindResource("Brush.Success.Text"),
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
