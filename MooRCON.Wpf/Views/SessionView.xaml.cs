using System.Windows.Controls;
using System.Windows.Input;
using MooRCON.Wpf.ViewModels;

namespace MooRCON.Wpf.Views;

public partial class SessionView : UserControl
{
    public SessionView() => InitializeComponent();

    private void OutputBox_TextChanged(object sender, TextChangedEventArgs e)
        => OutputBox.ScrollToEnd();

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
}
