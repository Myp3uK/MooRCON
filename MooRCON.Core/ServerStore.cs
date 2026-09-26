using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MooRCON.Core;

/// <summary>Загрузка/сохранение списка серверов (servers.json) с шифрованием паролей.</summary>
public sealed class ServerStore
{
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

    public ServerStore(string? path = null) => _path = path ?? AppPaths.ServersFile;

    /// <summary>
    /// Текст ошибки последней загрузки или null, если файл прочитан (или его нет).
    /// При ошибке нечитаемый файл уже сохранён копией — UI стоит об этом сообщить.
    /// </summary>
    public string? LoadError { get; private set; }

    public List<ServerConfig> Load()
    {
        LoadError = null;
        if (!File.Exists(_path)) return new();
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            var servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, ReadOpts) ?? new();
            foreach (var s in servers)
                s.RconPass = PasswordProtector.Unprotect(s.RconPass);
            return servers;
        }
        catch (Exception ex)
        {
            // Пустой список ниже при первом же сохранении перезапишет файл, поэтому
            // сначала откладываем копию — руками поправить её проще, чем вспоминать серверы.
            var backup = FileSafety.BackupCorrupt(_path);
            LoadError = backup is null
                ? $"Не удалось прочитать {Path.GetFileName(_path)}: {ex.Message}"
                : $"Не удалось прочитать {Path.GetFileName(_path)}: {ex.Message}\nКопия файла сохранена: {backup}";
            return new();
        }
    }

    public void Save(IEnumerable<ServerConfig> servers)
    {
        // Сериализуем копии с зашифрованными паролями, не трогая объекты в памяти.
        var toSave = servers.Select(s => new ServerConfig
        {
            Name = s.Name,
            IpHost = s.IpHost,
            RconPort = s.RconPort,
            RconPass = PasswordProtector.Protect(s.RconPass)
        }).ToList();

        FileSafety.WriteAllTextAtomic(_path, JsonSerializer.Serialize(toSave, WriteOpts));
    }
}
