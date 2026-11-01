using System.Text;
using System.Text.Json;

namespace MooRCON.Core;

/// <summary>Состояние интерфейса (ui.json). Только у десктоп-редакции.</summary>
public sealed class UiStateStore
{
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public UiStateStore(string? path = null) => _path = path ?? AppPaths.UiFile;

    public UiState Load()
    {
        if (!File.Exists(_path)) return new();

        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<UiState>(json, ReadOpts) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(UiState state)
    {
        try
        {
            FileSafety.WriteAllTextAtomic(_path, JsonSerializer.Serialize(state, WriteOpts));
        }
        catch { /* не критично: окно просто откроется на дефолтном месте */ }
    }
}
