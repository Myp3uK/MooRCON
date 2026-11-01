using System.Text.Json.Serialization;

namespace MooRCON.Core;

/// <summary>
/// Заготовка команды: подпись для кнопки и шаблон с подстановками
/// (см. <see cref="CommandTemplateRenderer"/>).
/// </summary>
public sealed class CommandTemplate
{
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";

    /// <summary>Спрашивать подтверждение перед отправкой — для необратимого (кик, бан, вайп).</summary>
    public bool Confirm { get; set; }

    /// <summary>Необязательная заметка, показывается подсказкой на кнопке.</summary>
    public string? Comment { get; set; }

    /// <summary>Шаблону нужен выбранный игрок: в нём есть подстановка вида {idx}.</summary>
    [JsonIgnore]
    public bool NeedsPlayer => CommandTemplateRenderer.UsesPlayer(Template);

    public CommandTemplate Clone()
        => new() { Name = Name, Template = Template, Confirm = Confirm, Comment = Comment };
}

/// <summary>
/// Параметр шаблона: <c>{?Подпись}</c>, <c>{?Подпись=значение по умолчанию}</c> или
/// с готовыми вариантами — <c>{?Стат=6|6:Основной урон|7:Второй урон}</c>.
/// </summary>
public sealed class TemplateParameter
{
    public TemplateParameter(string label, string defaultValue, IReadOnlyList<TemplateOption> options)
    {
        Label = label;
        Default = defaultValue;
        Options = options;
    }

    public string Label { get; }
    public string Default { get; }

    /// <summary>Готовые варианты значения. Пусто — поле со свободным вводом.</summary>
    public IReadOnlyList<TemplateOption> Options { get; }
}

/// <summary>Вариант значения параметра: что уйдёт в команду и что видит человек.</summary>
public sealed class TemplateOption
{
    public TemplateOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>Подставляется в команду.</summary>
    public string Value { get; }

    /// <summary>Человеческая подпись; пустая — показываем само значение.</summary>
    public string Label { get; }

    public string Display =>
        Label.Length == 0 || string.Equals(Label, Value, StringComparison.Ordinal)
            ? Value
            : $"{Value} — {Label}";

    public override string ToString() => Display;
}
