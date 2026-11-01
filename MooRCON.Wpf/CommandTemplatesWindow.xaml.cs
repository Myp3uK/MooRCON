using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MooRCON.Core;
using MooRCON.Wpf.Mvvm;

namespace MooRCON.Wpf;

/// <summary>
/// Редактор заготовок команд. Работает на копии набора, поэтому «Отмена» отменяет всё
/// сразу — включая добавление и удаление заготовок.
/// </summary>
public partial class CommandTemplatesWindow : Window
{
    private readonly ObservableCollection<TemplateRow> _rows = new();
    private readonly CommandTemplateSet _set;
    private readonly List<string> _originalNames;
    private readonly string _serverName;

    private TemplateRow? _current;
    private bool _loading;

    /// <summary>Отредактированный набор; заполняется при сохранении.</summary>
    public CommandTemplateSet? Result { get; private set; }

    public CommandTemplatesWindow(CommandTemplateSet set, string serverName)
    {
        InitializeComponent();

        _set = set.Clone();
        _serverName = serverName;
        _originalNames = _set.Templates.Select(t => t.Name).ToList();

        foreach (var template in _set.Templates)
            _rows.Add(new TemplateRow(template, _set.IsEnabled(serverName, template.Name), template.Name));

        TemplateList.ItemsSource = _rows;
        ServerCheck.Content = $"Показывать на сервере «{serverName}»";

        Loaded += (_, _) =>
        {
            if (_rows.Count > 0) TemplateList.SelectedIndex = 0;
            else LoadCurrent();
        };
    }

    private void OnTemplateSelected(object sender, SelectionChangedEventArgs e)
    {
        _current = TemplateList.SelectedItem as TemplateRow;
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        _loading = true;
        try
        {
            FormPanel.IsEnabled = _current is not null;
            NameBox.Text = _current?.Model.Name ?? "";
            TemplateBox.Text = _current?.Model.Template ?? "";
            CommentBox.Text = _current?.Model.Comment ?? "";
            ConfirmCheck.IsChecked = _current?.Model.Confirm ?? false;
            ServerCheck.IsChecked = _current?.ShowOnServer ?? false;
            HideError();
        }
        finally
        {
            _loading = false;
        }
    }

    // Правки сразу уходят в модель: список слева показывает их, пока пользователь печатает.
    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.Model.Name = NameBox.Text;
        _current.Refresh();
    }

    private void OnTemplateChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.Model.Template = TemplateBox.Text;
        _current.Refresh();
    }

    private void OnCommentChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        var comment = CommentBox.Text.Trim();
        _current.Model.Comment = comment.Length == 0 ? null : comment;
    }

    private void OnConfirmChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.Model.Confirm = ConfirmCheck.IsChecked == true;
    }

    private void OnServerCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.ShowOnServer = ServerCheck.IsChecked == true;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var row = new TemplateRow(new CommandTemplate { Name = UniqueName(), Template = "" }, true, null);
        _rows.Add(row);
        TemplateList.SelectedItem = row;
        TemplateList.ScrollIntoView(row);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        int index = _rows.IndexOf(_current);
        _rows.Remove(_current);
        TemplateList.SelectedIndex = _rows.Count == 0 ? -1 : Math.Min(index, _rows.Count - 1);
        if (_rows.Count == 0) { _current = null; LoadCurrent(); }
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AppPaths.CommandsFile))
        {
            ShowError("Файл заготовок ещё не создан — сохраните изменения.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.CommandsFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Не удалось открыть файл: " + ex.Message);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            var name = row.Model.Name.Trim();
            if (name.Length == 0) { Fail(row, "У заготовки должно быть название."); return; }
            if (row.Model.Template.Trim().Length == 0)
            {
                Fail(row, $"У заготовки «{name}» пустой шаблон команды.");
                return;
            }
            if (!names.Add(name)) { Fail(row, $"Заготовка «{name}» уже есть — названия должны различаться."); return; }

            row.Model.Name = name;
            row.Model.Template = row.Model.Template.Trim();
        }

        // Удалённые и переименованные заготовки убираем из списков всех серверов,
        // иначе там останутся имена, которым больше ничего не соответствует.
        var kept = _rows.Where(r => r.OriginalName is not null).Select(r => r.OriginalName!).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in _originalNames.Where(n => !kept.Contains(n)))
            _set.ForgetTemplate(removed);
        foreach (var row in _rows.Where(r => r.OriginalName is not null))
            _set.RenameTemplate(row.OriginalName!, row.Model.Name);

        _set.Templates = _rows.Select(r => r.Model).ToList();
        _set.SetEnabled(_serverName, _rows.Where(r => r.ShowOnServer).Select(r => r.Model.Name));

        Result = _set;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private string UniqueName()
    {
        const string baseName = "Новая команда";
        if (_rows.All(r => !string.Equals(r.Model.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (_rows.All(r => !string.Equals(r.Model.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void Fail(TemplateRow row, string message)
    {
        TemplateList.SelectedItem = row;
        ShowError(message);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}

/// <summary>Строка списка в редакторе: заготовка плюс отметка «показывать на этом сервере».</summary>
internal sealed class TemplateRow : ObservableObject
{
    /// <param name="originalName">Имя из файла; null — заготовка создана только что.</param>
    public TemplateRow(CommandTemplate model, bool showOnServer, string? originalName)
    {
        Model = model;
        ShowOnServer = showOnServer;
        OriginalName = originalName;
    }

    public CommandTemplate Model { get; }

    /// <summary>Имя, под которым заготовка лежала в файле; null — заготовка новая.</summary>
    public string? OriginalName { get; }

    public bool ShowOnServer { get; set; }

    public string Name => Model.Name;

    /// <summary>Шаблон одной строкой: многострочная цепочка иначе разносит строку списка.</summary>
    public string Preview => string.Join("  ·  ", Model.Template
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0));

    /// <summary>Перерисовать строку списка после правки в форме.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Preview));
    }
}
