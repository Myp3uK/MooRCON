using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MooRCON.Core;

/// <summary>
/// Заготовки команд (commands.json). Файл только у десктоп-редакции, консоль его не
/// трогает — поэтому её сохранения не могут потерять настройки заготовок.
/// </summary>
public sealed class CommandTemplateStore
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true
    };

    public CommandTemplateSet Load()
    {
        if (!File.Exists(AppPaths.CommandsFile))
        {
            // Пишем набор по умолчанию сразу: так файл видно и его можно править руками.
            var defaults = CommandTemplateSet.Default();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.CommandsFile, Encoding.UTF8);
            var set = JsonSerializer.Deserialize<CommandTemplateSet>(json, ReadOpts);
            if (set is null) return CommandTemplateSet.Default();

            // Битый или урезанный файл не должен ронять программу.
            set.Templates ??= new();
            set.Servers ??= new(StringComparer.Ordinal);
            set.Templates.RemoveAll(t => string.IsNullOrWhiteSpace(t.Name));
            return set;
        }
        catch
        {
            // Чужой файл не перезаписываем: пусть лучше заготовки не загрузятся,
            // чем пропадут вместе с правками пользователя.
            return CommandTemplateSet.Default();
        }
    }

    public void Save(CommandTemplateSet set)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.CommandsFile,
                              JsonSerializer.Serialize(set, WriteOpts), Encoding.UTF8);
        }
        catch { /* не критично: заготовки останутся в памяти до следующего запуска */ }
    }
}
