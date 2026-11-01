namespace MooRCON.Core;

/// <summary>Пути к данным программы в %APPDATA%\MooRCON (общие с консольной версией).</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MooRCON");

    public static string ServersFile { get; } = Path.Combine(DataDir, "servers.json");
    public static string HistoryFile { get; } = Path.Combine(DataDir, "history.json");

    /// <summary>Заготовки команд — только у десктоп-редакции.</summary>
    public static string CommandsFile { get; } = Path.Combine(DataDir, "commands.json");

    /// <summary>Состояние окна и панелей — только у десктоп-редакции.</summary>
    public static string UiFile { get; } = Path.Combine(DataDir, "ui.json");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDir);
}
