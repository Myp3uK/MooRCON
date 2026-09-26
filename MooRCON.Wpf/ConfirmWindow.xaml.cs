using System.Windows;

namespace MooRCON.Wpf;

public partial class ConfirmWindow : Window
{
    /// <param name="showCancel">false — окно-уведомление с одной кнопкой.</param>
    public ConfirmWindow(string title, string message, string confirmText = "Удалить", bool showCancel = true)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (!showCancel)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            // Уведомления бывают длиннее вопроса (напр. с путём к файлу) — даём место.
            Width = 540;
            Height = 290;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
