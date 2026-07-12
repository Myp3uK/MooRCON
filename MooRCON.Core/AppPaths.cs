namespace MooRCON.Core;

/// <summary>Пути к данным программы в %APPDATA%\MooRCON (общие с консольной версией).</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MooRCON");

    public static string ServersFile { get; } = Path.Combine(DataDir, "servers.json");
    public static string HistoryFile { get; } = Path.Combine(DataDir, "history.json");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDir);
}
