namespace MooRCON.Core;

/// <summary>
/// Разбирает вывод listplayers в список игроков. Ожидается таблица с разделителем "|"
/// (формат Conan Exiles):
///
/// <code>
/// Idx | Char name | Player name | User ID | Platform ID | Platform Name
///   0 | Myp3uK    | Myp3uK      | 1       | 76561198... | Steam
/// </code>
///
/// Колонки находятся по шапке, поэтому их порядок и состав могут отличаться от Conan.
/// Если шапки нет — читаем по порядку колонок Conan. Строки, где на месте Idx не число
/// (итоговое «N players connected», линейки из дефисов), пропускаются.
/// </summary>
public static class PlayerListParser
{
    /// <summary>Порядок колонок Conan — он же запасной вариант, когда шапки нет.</summary>
    private static readonly string[] DefaultOrder =
        { "idx", "charname", "playername", "userid", "platformid", "platformname" };

    /// <summary>Варианты написания шапки → наше имя колонки.</summary>
    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.Ordinal)
    {
        ["idx"] = "idx",
        ["index"] = "idx",
        ["char"] = "charname",
        ["charname"] = "charname",
        ["charactername"] = "charname",
        ["player"] = "playername",
        ["playername"] = "playername",
        ["userid"] = "userid",
        ["platformid"] = "platformid",
        ["steamid"] = "platformid",
        ["platform"] = "platformname",
        ["platformname"] = "platformname",
    };

    public static List<PlayerInfo> Parse(string text)
    {
        var players = new List<PlayerInfo>();
        if (string.IsNullOrWhiteSpace(text)) return players;

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < DefaultOrder.Length; i++) columns[DefaultOrder[i]] = i;
        bool headerFound = false;

        foreach (var line in text.Split('\n'))
        {
            var cells = SplitRow(line);
            if (cells is null) continue;

            if (!headerFound && TryReadHeader(cells, columns))
            {
                headerFound = true;
                continue;
            }

            if (!int.TryParse(Cell(cells, columns, "idx"), out int idx)) continue;

            players.Add(new PlayerInfo
            {
                Idx = idx,
                CharName = Cell(cells, columns, "charname"),
                PlayerName = Cell(cells, columns, "playername"),
                UserId = Cell(cells, columns, "userid"),
                PlatformId = Cell(cells, columns, "platformid"),
                Platform = Cell(cells, columns, "platformname"),
            });
        }

        return players;
    }

    /// <summary>Строка таблицы по ячейкам, или null — если это не строка таблицы.</summary>
    private static List<string>? SplitRow(string line)
    {
        if (!line.Contains('|')) return null;

        var cells = line.Split('|').Select(c => c.Trim()).ToList();

        // Таблица может быть обрамлена ("| 0 | Myp3uK |") — пустые крайние ячейки убираем
        // и у шапки, и у строк, иначе колонки разъедутся на одну позицию.
        if (cells[0].Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);

        return cells.Count >= 2 ? cells : null;
    }

    /// <summary>
    /// Если строка — шапка таблицы (есть колонка Idx), перезаписывает позиции колонок
    /// по ней и возвращает true.
    /// </summary>
    private static bool TryReadHeader(List<string> cells, Dictionary<string, int> columns)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < cells.Count; i++)
            if (HeaderAliases.TryGetValue(Normalize(cells[i]), out var name) && !found.ContainsKey(name))
                found[name] = i;

        if (!found.ContainsKey("idx")) return false;

        columns.Clear();
        foreach (var (name, i) in found) columns[name] = i;
        return true;
    }

    private static string Cell(List<string> cells, Dictionary<string, int> columns, string name)
        => columns.TryGetValue(name, out int i) && i < cells.Count ? cells[i] : "";

    /// <summary>«Char name» → «charname»: регистр и любые разделители игнорируем.</summary>
    private static string Normalize(string s)
        => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
