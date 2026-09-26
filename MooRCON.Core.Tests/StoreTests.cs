using MooRCON.Core;

namespace MooRCON.Core.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moorcon-tests-" + Guid.NewGuid().ToString("N"));

    public StoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    public void ServerStore_RoundTrips()
    {
        var store = new ServerStore(PathOf("servers.json"));
        store.Save([new ServerConfig { Name = "Москва", IpHost = "127.0.0.1", RconPort = 25575, RconPass = "secret" }]);

        var loaded = store.Load();

        Assert.Null(store.LoadError);
        var s = Assert.Single(loaded);
        Assert.Equal("Москва", s.Name);
        Assert.Equal("secret", s.RconPass);
        Assert.False(File.Exists(PathOf("servers.json.tmp")));
    }

    [Fact]
    public void ServerStore_CorruptFile_IsBackedUpAndReported()
    {
        var path = PathOf("servers.json");
        File.WriteAllText(path, "[ { \"Name\": \"A\", oops ]");

        var store = new ServerStore(path);
        var loaded = store.Load();

        Assert.Empty(loaded);
        Assert.NotNull(store.LoadError);
        var backup = Assert.Single(Directory.GetFiles(_dir, "servers.json.corrupt-*"));
        Assert.Equal("[ { \"Name\": \"A\", oops ]", File.ReadAllText(backup));
    }

    [Fact]
    public void HistoryStore_RenameMovesHistory()
    {
        var store = new HistoryStore(PathOf("history.json"));
        store.Save("old", ["listplayers", "help"]);

        store.Rename("old", "new");

        Assert.Empty(store.Load("old"));
        Assert.Equal(new[] { "listplayers", "help" }, store.Load("new"));
    }

    [Fact]
    public void HistoryStore_KeepsLast200()
    {
        var store = new HistoryStore(PathOf("history.json"));
        store.Save("s", Enumerable.Range(0, 250).Select(i => $"cmd{i}"));

        var loaded = store.Load("s");

        Assert.Equal(200, loaded.Count);
        Assert.Equal("cmd50", loaded[0]);
        Assert.Equal("cmd249", loaded[^1]);
    }

    [Fact]
    public void HistoryStore_CorruptFile_IsBackedUp()
    {
        var path = PathOf("history.json");
        File.WriteAllText(path, "{ broken");

        new HistoryStore(path).Save("s", ["x"]);

        Assert.Single(Directory.GetFiles(_dir, "history.json.corrupt-*"));
        Assert.Equal(new[] { "x" }, new HistoryStore(path).Load("s"));
    }

    [Fact]
    public void WriteAllTextAtomic_ReplacesExistingFile()
    {
        var path = PathOf("a.json");
        File.WriteAllText(path, "old");

        FileSafety.WriteAllTextAtomic(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
