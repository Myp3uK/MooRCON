using MooRCON.Core;

namespace MooRCON.Core.Tests;

public class PlayerListParserTests
{
    private const string ConanOutput =
        "Idx | Char name | Player name | User ID | Platform ID | Platform Name\r\n" +
        "0 | Myp3uK | myp3uk | 1 | 76561198000000001 | Steam\r\n" +
        "1 | Vasya the Bold | vasya2000 | 2 | 76561198000000002 | Steam\r\n" +
        "2 players connected\r\n";

    [Fact]
    public void Parse_ReadsConanTable()
    {
        var players = PlayerListParser.Parse(ConanOutput);

        Assert.Equal(2, players.Count); // строка «2 players connected» — не игрок
        Assert.Equal(0, players[0].Idx);
        Assert.Equal("Myp3uK", players[0].CharName);
        Assert.Equal("myp3uk", players[0].PlayerName);
        Assert.Equal("76561198000000001", players[0].PlatformId);
        Assert.Equal("Steam", players[0].Platform);
        Assert.Equal("Vasya the Bold", players[1].CharName);
    }

    [Fact]
    public void Parse_FindsColumnsByHeaderNotPosition()
    {
        var players = PlayerListParser.Parse(
            "Char name | Platform Name | Idx | Platform ID\nMyp3uK | Xbox | 7 | 2535400000\n");

        var p = Assert.Single(players);
        Assert.Equal(7, p.Idx);
        Assert.Equal("Myp3uK", p.CharName);
        Assert.Equal("Xbox", p.Platform);
        Assert.Equal("2535400000", p.PlatformId);
    }

    [Fact]
    public void Parse_HandlesFramedRows()
    {
        var players = PlayerListParser.Parse(
            "| Idx | Char name | Player name | User ID | Platform ID | Platform Name |\n" +
            "|-----|-----------|-------------|---------|-------------|---------------|\n" +
            "|   0 | Myp3uK    | myp3uk      |       1 | 7656119     | Steam         |\n");

        var p = Assert.Single(players); // линейка из дефисов игроком не считается
        Assert.Equal(0, p.Idx);
        Assert.Equal("Myp3uK", p.CharName);
    }

    [Fact]
    public void Parse_FallsBackToConanColumnOrderWithoutHeader()
    {
        var players = PlayerListParser.Parse("0 | Myp3uK | myp3uk | 1 | 7656119 | Steam\n");

        var p = Assert.Single(players);
        Assert.Equal("Myp3uK", p.CharName);
        Assert.Equal("7656119", p.PlatformId);
    }

    [Fact]
    public void Parse_IgnoresNonTableOutput()
    {
        Assert.Empty(PlayerListParser.Parse("Command not found. Try 'help'.\n"));
        Assert.Empty(PlayerListParser.Parse(""));
    }

    [Fact]
    public void Key_PrefersPlatformIdThenUserIdThenName()
    {
        Assert.Equal("765", new PlayerInfo { PlatformId = "765", UserId = "1", CharName = "A" }.Key);
        Assert.Equal("1", new PlayerInfo { UserId = "1", CharName = "A" }.Key);
        Assert.Equal("A", new PlayerInfo { CharName = "A" }.Key);
    }
}
