namespace MooRCON.Core;

/// <summary>Описание RCON-сервера. Формат совместим с servers.json консольной версии.</summary>
public sealed class ServerConfig
{
    public string Name { get; set; } = "";
    public string IpHost { get; set; } = "";
    public int RconPort { get; set; } = 25575;
    public string RconPass { get; set; } = "";
}
