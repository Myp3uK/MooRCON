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

    public UiState Load()
    {
        if (!File.Exists(AppPaths.UiFile)) return new();

        try
        {
            var json = File.ReadAllText(AppPaths.UiFile, Encoding.UTF8);
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
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.UiFile, JsonSerializer.Serialize(state, WriteOpts), Encoding.UTF8);
        }
        catch { /* не критично: окно просто откроется на дефолтном месте */ }
    }
}
