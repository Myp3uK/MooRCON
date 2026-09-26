using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MooRCON.Core;

/// <summary>История команд по каждому серверу (history.json), общая с консольной версией.</summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 200;

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _path;

    public HistoryStore(string? path = null) => _path = path ?? AppPaths.HistoryFile;

    private Dictionary<string, List<string>> LoadAll()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, ReadOpts) ?? new();
        }
        catch
        {
            // Следующее сохранение перезапишет файл историей одного сервера — копию
            // остальных откладываем рядом.
            FileSafety.BackupCorrupt(_path);
            return new();
        }
    }

    private void SaveAll(Dictionary<string, List<string>> all)
    {
        try
        {
            FileSafety.WriteAllTextAtomic(_path, JsonSerializer.Serialize(all, WriteOpts));
        }
        catch { /* ignored */ }
    }

    public List<string> Load(string serverName)
    {
        if (string.IsNullOrEmpty(serverName)) return new();
        return LoadAll().TryGetValue(serverName, out var list) ? new List<string>(list) : new();
    }

    public void Save(string serverName, IEnumerable<string> history)
    {
        if (string.IsNullOrEmpty(serverName)) return;
        var all = LoadAll();
        var trimmed = history.TakeLast(MaxEntries).ToList();
        if (trimmed.Count == 0) all.Remove(serverName);
        else all[serverName] = trimmed;
        SaveAll(all);
    }

    /// <summary>Переносит историю на новое имя сервера (при переименовании).</summary>
    public void Rename(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return;
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        var all = LoadAll();
        if (!all.Remove(oldName, out var list)) return;
        all[newName] = list;
        SaveAll(all);
    }

    public void Delete(string serverName)
    {
        if (string.IsNullOrEmpty(serverName)) return;
        var all = LoadAll();
        if (all.Remove(serverName)) SaveAll(all);
    }
}
