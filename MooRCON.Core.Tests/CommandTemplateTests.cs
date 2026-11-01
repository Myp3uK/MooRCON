using MooRCON.Core;

namespace MooRCON.Core.Tests;

public class CommandTemplateTests
{
    private static readonly PlayerInfo Player = new()
    {
        Idx = 3,
        CharName = "Myp3uK",
        PlayerName = "myp3uk",
        UserId = "1",
        PlatformId = "76561198000000001",
        Platform = "Steam",
    };

    [Fact]
    public void Render_SubstitutesPlayerFields()
    {
        var command = CommandTemplateRenderer.Render(
            "con {idx} say {char} / {player} / {userid} / {platformid} / {platform}", Player);

        Assert.Equal("con 3 say Myp3uK / myp3uk / 1 / 76561198000000001 / Steam", command);
    }

    [Fact]
    public void Render_LeavesUnknownPlaceholderAsIs()
    {
        // Опечатка должна быть видна в выводе, а не исчезать молча.
        Assert.Equal("con {indx} test", CommandTemplateRenderer.Render("con {indx} test", Player));
    }

    [Fact]
    public void FindParameters_AsksRepeatedLabelOnce()
    {
        var template = "con {idx} SetStat AttributeMight {?Очки=30}\n"
                     + "con {idx} SetStat AttributeHealth {?Очки=30}";

        var parameters = CommandTemplateRenderer.FindParameters(template);

        var p = Assert.Single(parameters);
        Assert.Equal("Очки", p.Label);
        Assert.Equal("30", p.Default);
    }

    [Fact]
    public void Render_ReusesAnsweredValueWithShift()
    {
        var args = new Dictionary<string, string> { ["Урон"] = "200" };

        var command = CommandTemplateRenderer.Render("stat 6 {?Урон=100} / 7 {Урон+30} / 8 {Урон}", null, args);

        Assert.Equal("stat 6 200 / 7 230 / 8 200", command);
    }

    [Fact]
    public void Render_KeepsValueWhenShiftIsNotApplicable()
    {
        var args = new Dictionary<string, string> { ["Имя"] = "Мурзик" };

        Assert.Equal("say Мурзик", CommandTemplateRenderer.Render("say {Имя+10}", null, args));
    }

    [Fact]
    public void FindParameters_ReadsOptionsWithLabels()
    {
        var parameters = CommandTemplateRenderer.FindParameters(
            "cmd {?Стат=7|6:Основной урон|7:Второй урон|14:Сила сбора}");

        var p = Assert.Single(parameters);
        Assert.Equal("7", p.Default);
        Assert.Equal(3, p.Options.Count);
        Assert.Equal("14", p.Options[2].Value);
        Assert.Equal("14 — Сила сбора", p.Options[2].Display);
    }

    [Fact]
    public void FindParameters_TakesFirstOptionWhenNoDefaultGiven()
    {
        var p = Assert.Single(CommandTemplateRenderer.FindParameters("cmd {?Тип|2:Быстрая панель|0:Рюкзак}"));

        Assert.Equal("2", p.Default);
        Assert.Equal("0", p.Options[1].Value);

        // Вариант без подписи показывается самим значением.
        var plain = CommandTemplateRenderer.FindParameters("cmd {?Слот=0|0|1}")[0].Options;
        Assert.Equal("1", plain[1].Display);
    }

    [Fact]
    public void RenderLines_SplitsChainAndDropsEmptyLines()
    {
        var template = "con {idx} first\n\n   \ncon {idx} second\nsaveworld";

        var lines = CommandTemplateRenderer.RenderLines(template, Player);

        Assert.Equal(["con 3 first", "con 3 second", "saveworld"], lines);
    }

    [Fact]
    public void NeedsPlayer_OnlyWhenTemplateUsesPlayerField()
    {
        Assert.True(new CommandTemplate { Template = "con {idx} kick" }.NeedsPlayer);
        Assert.True(new CommandTemplate { Template = "ban {platformid}" }.NeedsPlayer);
        Assert.False(new CommandTemplate { Template = "listplayers" }.NeedsPlayer);
        Assert.False(new CommandTemplate { Template = "broadcast {?Текст}" }.NeedsPlayer);
    }

    [Fact]
    public void ForServer_ShowsEverythingUntilListIsNarrowed()
    {
        var set = new CommandTemplateSet
        {
            Templates =
            [
                new CommandTemplate { Name = "Первая", Template = "a" },
                new CommandTemplate { Name = "Вторая", Template = "b" },
            ],
        };

        Assert.Equal(2, set.ForServer("PvP").Count());

        set.SetEnabled("PvP", ["Первая"]);
        Assert.Equal("Первая", Assert.Single(set.ForServer("PvP")).Name);

        // Отмечены все — запись убирается, чтобы новые заготовки появлялись сами.
        set.SetEnabled("PvP", ["Первая", "Вторая"]);
        Assert.Empty(set.Servers);
    }

    [Fact]
    public void RenameAndForget_KeepServerListsConsistent()
    {
        var set = new CommandTemplateSet
        {
            Templates = [new CommandTemplate { Name = "Кик", Template = "kick {idx}" }],
            Servers = { ["PvP"] = ["Кик"] },
        };

        set.RenameTemplate("Кик", "Кикнуть");
        Assert.Equal("Кикнуть", set.Servers["PvP"][0]);

        set.RenameServer("PvP", "PvE");
        Assert.True(set.Servers.ContainsKey("PvE"));

        set.ForgetTemplate("Кикнуть");
        Assert.Empty(set.Servers["PvE"]);
    }

    [Fact]
    public void Default_IsReadyToSendForConan()
    {
        var set = CommandTemplateSet.Default();

        Assert.NotEmpty(set.Templates);
        Assert.All(set.Templates, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Template));
        });

        var all = set.Templates.Single(t => t.Name == "Все атрибуты");
        var values = CommandTemplateRenderer.FindParameters(all.Template)
            .ToDictionary(p => p.Label, p => p.Default);
        var lines = CommandTemplateRenderer.RenderLines(all.Template, Player, values);

        Assert.Equal(6, lines.Count); // шесть атрибутов, одно спрошенное значение
        Assert.Equal("con 3 SetStat AttributeMight 30", lines[0]);
        Assert.Equal("con 3 SetStat AttributeEncumbrance 30", lines[5]);
    }
}
