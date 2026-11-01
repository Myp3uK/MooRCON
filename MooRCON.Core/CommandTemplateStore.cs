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

    private readonly string _path;

    public CommandTemplateStore(string? path = null) => _path = path ?? AppPaths.CommandsFile;

    /// <summary>Путь копии нечитаемого файла, если он был сделан при последней загрузке.</summary>
    public string? CorruptBackup { get; private set; }

    public CommandTemplateSet Load()
    {
        if (!File.Exists(_path))
        {
            // Пишем набор по умолчанию сразу: так файл видно и его можно править руками.
            var defaults = CommandTemplateSet.Default();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
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
            // Файл не прочитан — откладываем копию: следующее сохранение затрёт оригинал,
            // а в нём могут быть заготовки, которые ещё можно поправить руками.
            CorruptBackup = FileSafety.BackupCorrupt(_path);
            return CommandTemplateSet.Default();
        }
    }

    public void Save(CommandTemplateSet set)
    {
        try
        {
            FileSafety.WriteAllTextAtomic(_path, JsonSerializer.Serialize(set, WriteOpts));
        }
        catch { /* не критично: заготовки останутся в памяти до следующего запуска */ }
    }
}
