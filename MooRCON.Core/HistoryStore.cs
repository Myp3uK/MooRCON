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

    private Dictionary<string, List<string>> LoadAll()
    {
        if (!File.Exists(AppPaths.HistoryFile)) return new();
        try
        {
            var json = File.ReadAllText(AppPaths.HistoryFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, ReadOpts) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveAll(Dictionary<string, List<string>> all)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(all, WriteOpts), Encoding.UTF8);
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
}
