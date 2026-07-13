using System.Windows;

namespace MooRCON.Wpf;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirmText = "Удалить")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
