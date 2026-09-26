using MooRCON.Core;

namespace MooRCON.Core.Tests;

public class HelpCommandParserTests
{
    [Fact]
    public void Parse_SkipsHeadersAndDeduplicates()
    {
        var help = "Commands:\r\nListPlayers\nkickplayer <id>\nUsage: foo\n\n  broadcast text\nlistplayers\n";

        var cmds = HelpCommandParser.Parse(help);

        Assert.Equal(new[] { "broadcast", "kickplayer", "listplayers" }, cmds);
    }

    [Fact]
    public void Parse_IgnoresLinesNotStartingWithCommand()
    {
        var cmds = HelpCommandParser.Parse("123abc\n- item\nx\n");
        Assert.Empty(cmds); // "x" короче двух символов, остальное не команды
    }
}
