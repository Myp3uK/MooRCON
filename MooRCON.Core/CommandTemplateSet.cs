namespace MooRCON.Core;

/// <summary>
/// Содержимое commands.json: общий набор заготовок и, для каждого сервера, список имён
/// показываемых на нём. Сервера без своей записи показывают все заготовки — поэтому
/// новые заготовки появляются везде, пока список для сервера не урезали вручную.
/// </summary>
public sealed class CommandTemplateSet
{
    public List<CommandTemplate> Templates { get; set; } = new();

    /// <summary>Имя сервера → имена показываемых на нём заготовок.</summary>
    public Dictionary<string, List<string>> Servers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Заготовки для сервера, в порядке общего набора.</summary>
    public IEnumerable<CommandTemplate> ForServer(string serverName)
    {
        if (!Servers.TryGetValue(serverName, out var enabled)) return Templates;

        var names = new HashSet<string>(enabled, StringComparer.Ordinal);
        return Templates.Where(t => names.Contains(t.Name));
    }

    public bool IsEnabled(string serverName, string templateName)
        => !Servers.TryGetValue(serverName, out var enabled)
           || enabled.Contains(templateName, StringComparer.Ordinal);

    /// <summary>
    /// Запоминает, какие заготовки показывать на сервере. Если отмечены все —
    /// запись удаляется: «все» должно оставаться «всеми» и после добавления новых.
    /// </summary>
    public void SetEnabled(string serverName, IEnumerable<string> enabledNames)
    {
        var enabled = enabledNames.Distinct(StringComparer.Ordinal).ToList();
        if (enabled.Count == Templates.Count) Servers.Remove(serverName);
        else Servers[serverName] = enabled;
    }

    public void RenameServer(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        if (Servers.Remove(oldName, out var enabled)) Servers[newName] = enabled;
    }

    public void RemoveServer(string serverName) => Servers.Remove(serverName);

    /// <summary>Выбрасывает из списков серверов заготовку, которую удалили из общего набора.</summary>
    public void ForgetTemplate(string templateName)
    {
        foreach (var enabled in Servers.Values)
            enabled.RemoveAll(n => string.Equals(n, templateName, StringComparison.Ordinal));
    }

    /// <summary>Переименовывает заготовку в списках всех серверов, где она отмечена.</summary>
    public void RenameTemplate(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        foreach (var enabled in Servers.Values)
            for (int i = 0; i < enabled.Count; i++)
                if (string.Equals(enabled[i], oldName, StringComparison.Ordinal))
                    enabled[i] = newName;
    }

    public CommandTemplateSet Clone() => new()
    {
        Templates = Templates.Select(t => t.Clone()).ToList(),
        Servers = Servers.ToDictionary(p => p.Key, p => new List<string>(p.Value), StringComparer.Ordinal),
    };

    /// <summary>Атрибуты Conan Exiles — имена статов для SetStat.</summary>
    private static readonly string[] Attributes =
    {
        "AttributeMight",
        "AttributeAthleticism",
        "AttributeHealth",
        "AttributeLeadership",
        "AttributeStamina",
        "AttributeEncumbrance",
    };

    /// <summary>Тип инвентаря — третий аргумент команд SetInventoryItem*Stat.</summary>
    private const string InventoryChoice = "{?Инвентарь=2|0:Рюкзак|1:Экипировка|2:Быстрая панель}";

    /// <summary>
    /// Набор по умолчанию — под Conan Exiles. Кик, бан и прочее сюда не кладём: у каждой
    /// сборки сервера они называются по-своему, точные имена видно в выводе help.
    /// </summary>
    public static CommandTemplateSet Default() => new()
    {
        Templates = new List<CommandTemplate>
        {
            new()
            {
                Name = "Список игроков",
                Template = "listplayers",
                Comment = "Тот же запрос, что и кнопка обновления списка, но с выводом в консоль.",
            },
            new()
            {
                Name = "Выдать предмет",
                Template = "con {idx} spawnitem {?ID предмета} {?Количество=1}",
                Comment = "Предмет попадёт в инвентарь выбранного игрока.",
            },
            new()
            {
                Name = "Команда от игрока",
                Template = "con {idx} {?Команда}",
                Comment = "Выполняет консольную команду так, как будто её ввёл сам игрок.",
            },
            new()
            {
                Name = "Спавн раба в инвентарь",
                Template = "con {idx} dc spawn rabbit thrall silent\n"
                         + "con {idx} dc spawn {?Раб=Boss_ArenaChampion"
                         + "|Boss_ArenaChampion:Чемпион арены"
                         + "|ladagara_daughter_of_ymir:Ладагара"
                         + "|KhitanSlaverThrall:Кхитайский рабовладелец"
                         + "|Companion_Sorcerer_Follower:Лю фей"
                         + "|Companion_Warrior_Follower:Фрея"
                         + "|Boss_HeadlessJudge:Судья"
                         + "|Boss_Hanumanpriest:Валис верный}",
                Comment = "Спавнит указанного спутника в инвентарь игроку",
            },
            new()
            {
                Name = "Урон оружия",
                // Слот и тип инвентаря спрашиваем один раз и подставляем во все части.
                // Второй стат — тот же урон плюс 30, так что и урон спрашиваем один раз.
                Template = $"con {{idx}} SetInventoryItemIntStat {{?Слот=0}} 6 {{?Урон=200}} {InventoryChoice} | "
                         + "SetInventoryItemIntStat {Слот} 7 {Урон+30} {Инвентарь} | "
                         + "SetInventoryItemFloatStat {Слот} 11 1 {Инвентарь} | "
                         + "SetInventoryItemFloatStat {Слот} 7 -1 {Инвентарь} | "
                         + "SetInventoryItemFloatStat {Слот} 8 -1 {Инвентарь}",
                Comment = "Второй урон считается сам: урон + 30.",
            },
            new()
            {
                Name = "Сила сбора",
                Template = $"con {{idx}} SetInventoryItemIntStat {{?Слот=0}} 14 {{?Сила сбора=100}} {InventoryChoice} | "
                         + "SetInventoryItemFloatStat {Слот} 7 -1 {Инвентарь} | "
                         + "SetInventoryItemFloatStat {Слот} 8 -1 {Инвентарь}",
            },
            new()
            {
                Name = "Все атрибуты",
                // Одна цифра на все шесть строк: повторный {?Очки=30} спрашивается один раз.
                Template = string.Join("\n", Attributes.Select(
                    stat => $"con {{idx}} SetStat {stat} {{?Очки=30}}")),
                Comment = "Ставит одно и то же значение всем шести атрибутам.",
            },
        },
    };
}
