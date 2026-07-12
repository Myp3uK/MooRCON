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

    public List<ServerConfig> Load()
    {
        if (!File.Exists(AppPaths.ServersFile)) return new();
        try
        {
            var json = File.ReadAllText(AppPaths.ServersFile, Encoding.UTF8);
            var servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, ReadOpts) ?? new();
            foreach (var s in servers)
                s.RconPass = PasswordProtector.Unprotect(s.RconPass);
            return servers;
        }
        catch
        {
            return new();
        }
    }

    public void Save(IEnumerable<ServerConfig> servers)
    {
        AppPaths.EnsureCreated();
        // Сериализуем копии с зашифрованными паролями, не трогая объекты в памяти.
        var toSave = servers.Select(s => new ServerConfig
        {
            Name = s.Name,
            IpHost = s.IpHost,
            RconPort = s.RconPort,
            RconPass = PasswordProtector.Protect(s.RconPass)
        }).ToList();

        var json = JsonSerializer.Serialize(toSave, WriteOpts);
        File.WriteAllText(AppPaths.ServersFile, json, Encoding.UTF8);
    }
}
