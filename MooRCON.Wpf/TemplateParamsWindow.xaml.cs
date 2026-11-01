using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MooRCON.Core;

namespace MooRCON.Wpf;

/// <summary>
/// Спрашивает значения параметров шаблона (<c>{?Подпись}</c>) перед отправкой команды.
/// Поля собираются по самому шаблону, поэтому окно годится для любой заготовки.
/// </summary>
public partial class TemplateParamsWindow : Window
{
    private readonly List<(TemplateParameter Parameter, Control Input)> _fields = new();

    /// <summary>Введённые значения: подпись параметра → значение.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TemplateParamsWindow(CommandTemplate template,
                                IReadOnlyList<TemplateParameter> parameters,
                                PlayerInfo? player)
    {
        InitializeComponent();

        Title = template.Name;
        TitleText.Text = template.Name;
        TemplateText.Text = template.Template;

        if (player is not null && template.NeedsPlayer)
        {
            PlayerText.Text = $"Игрок: {player.Display}";
            PlayerText.Visibility = Visibility.Visible;
        }

        foreach (var parameter in parameters)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = parameter.Label,
                FontSize = 12,
                Margin = new Thickness(2, 12, 0, 6),
                Foreground = (System.Windows.Media.Brush)FindResource("Brush.Text.Label"),
            });

            Control input = parameter.Options.Count > 0 ? Chooser(parameter) : FreeText(parameter);
            FieldsPanel.Children.Add(input);
            _fields.Add((parameter, input));
        }

        Loaded += (_, _) =>
        {
            if (_fields.Count == 0) return;
            var first = _fields[0].Input;
            first.Focus();
            (first as TextBox)?.SelectAll();
        };
    }

    /// <summary>Параметр с готовыми вариантами — выпадающий список.</summary>
    private ComboBox Chooser(TemplateParameter parameter)
    {
        var combo = new ComboBox
        {
            Style = (Style)FindResource("Field.Combo"),
            ItemsSource = parameter.Options,
            DisplayMemberPath = nameof(TemplateOption.Display),
        };

        combo.SelectedItem = parameter.Options.FirstOrDefault(
            o => string.Equals(o.Value, parameter.Default, StringComparison.Ordinal)) ?? parameter.Options[0];
        return combo;
    }

    private TextBox FreeText(TemplateParameter parameter) => new()
    {
        Style = (Style)FindResource("Field.Text"),
        FontFamily = (System.Windows.Media.FontFamily)FindResource("Font.Mono"),
        Text = parameter.Default,
    };

    private static string ValueOf(Control input) => input switch
    {
        TextBox box => box.Text.Trim(),
        ComboBox combo => (combo.SelectedItem as TemplateOption)?.Value ?? "",
        _ => "",
    };

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Values.Clear();
        foreach (var (parameter, input) in _fields)
        {
            var value = ValueOf(input);
            if (value.Length == 0)
            {
                ErrorText.Text = $"Заполните «{parameter.Label}».";
                ErrorText.Visibility = Visibility.Visible;
                input.Focus();
                return;
            }
            Values[parameter.Label] = value;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnOk(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
