using System.Globalization;
using System.Text.RegularExpressions;

namespace MooRCON.Core;

/// <summary>
/// Подстановки в шаблонах команд. Всё, что в фигурных скобках, — подстановка:
///
/// <list type="bullet">
/// <item><c>{idx}</c> — индекс игрока в listplayers;</item>
/// <item><c>{char}</c>, <c>{player}</c> — имя персонажа и имя аккаунта;</item>
/// <item><c>{userid}</c>, <c>{platformid}</c>, <c>{platform}</c> — идентификаторы и платформа;</item>
/// <item><c>{?Подпись}</c>, <c>{?Подпись=по умолчанию}</c> — спросить значение перед отправкой;</item>
/// <item><c>{?Подпись=6|6:Основной урон|7:Второй урон}</c> — то же, но значение выбирается
/// из готового списка: через <c>|</c> варианты, после <c>:</c> человеческая подпись;</item>
/// <item><c>{Подпись}</c>, <c>{Подпись+30}</c> — уже спрошенное значение, в том числе
/// со сдвигом на число (<c>+</c>, <c>-</c>, <c>*</c>): спрашиваем один раз, подставляем сколько нужно.</item>
/// </list>
///
/// Неизвестная подстановка остаётся в команде как есть: пусть лучше будет видна в выводе,
/// чем молча исчезнет (например, при опечатке в имени).
/// </summary>
public static partial class CommandTemplateRenderer
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderRegex();

    private static readonly HashSet<string> PlayerFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "idx", "index", "char", "charname", "player", "playername",
        "userid", "platformid", "steamid", "platform",
    };

    /// <summary>Есть ли в шаблоне хоть одна подстановка из данных игрока.</summary>
    public static bool UsesPlayer(string template)
    {
        if (string.IsNullOrEmpty(template)) return false;
        foreach (var m in PlaceholderRegex().Matches(template).Cast<Match>())
            if (PlayerFields.Contains(m.Groups[1].Value.Trim())) return true;
        return false;
    }

    /// <summary>
    /// Параметры шаблона в порядке появления. Повторы по подписи отбрасываются: один и тот
    /// же <c>{?Очки=30}</c> можно писать в каждой строке — спросят всё равно один раз,
    /// и описание возьмётся из первого вхождения.
    /// </summary>
    public static List<TemplateParameter> FindParameters(string template)
    {
        var parameters = new List<TemplateParameter>();
        if (string.IsNullOrEmpty(template)) return parameters;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in PlaceholderRegex().Matches(template).Cast<Match>())
        {
            var parameter = ParseParameter(m.Groups[1].Value);
            if (parameter is null || !seen.Add(parameter.Label)) continue;
            parameters.Add(parameter);
        }

        return parameters;
    }

    /// <summary>
    /// Собирает готовую команду. Данные игрока берутся из <paramref name="player"/>
    /// (null — подставляется пустая строка), значения параметров — из
    /// <paramref name="args"/> по подписи, а чего там нет — из значений по умолчанию.
    /// </summary>
    public static string Render(string template, PlayerInfo? player,
                               IReadOnlyDictionary<string, string>? args = null)
    {
        if (string.IsNullOrEmpty(template)) return "";

        return PlaceholderRegex().Replace(template, m =>
        {
            var body = m.Groups[1].Value;

            var parameter = ParseParameter(body);
            if (parameter is not null)
                return args is not null && args.TryGetValue(parameter.Label, out var value)
                    ? value
                    : parameter.Default;

            return PlayerField(body.Trim(), player) ?? Reference(body, args) ?? m.Value;
        }).Trim();
    }

    private static readonly char[] Operators = { '+', '-', '*' };

    /// <summary>
    /// Ссылка на уже спрошенный параметр: <c>{Урон}</c> или <c>{Урон+30}</c>. Если значение
    /// не число, подставляем его как есть — пусть будет видно в выводе, что пошло не так.
    /// </summary>
    private static string? Reference(string body, IReadOnlyDictionary<string, string>? args)
    {
        if (args is null) return null;

        var trimmed = body.Trim();
        if (args.TryGetValue(trimmed, out var direct)) return direct;

        // Имя параметра может само содержать «+» или «-», поэтому точное совпадение
        // проверено выше, а здесь берём последний оператор.
        int at = trimmed.LastIndexOfAny(Operators);
        if (at <= 0) return null;

        if (!args.TryGetValue(trimmed[..at].Trim(), out var raw)) return null;
        if (!long.TryParse(trimmed[(at + 1)..].Trim(), NumberStyles.Integer,
                           CultureInfo.InvariantCulture, out long operand)) return null;
        if (!long.TryParse(raw.Trim(), NumberStyles.Integer,
                           CultureInfo.InvariantCulture, out long value)) return raw;

        long result = trimmed[at] switch
        {
            '+' => value + operand,
            '-' => value - operand,
            '*' => value * operand,
            _ => value,
        };
        return result.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Готовая цепочка команд: каждая непустая строка шаблона — отдельная команда.
    /// Подстановки общие на весь шаблон, поэтому значение спрашивается один раз на всю цепочку.
    /// </summary>
    public static List<string> RenderLines(string template, PlayerInfo? player,
                                           IReadOnlyDictionary<string, string>? args = null)
        => Render(template, player, args)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>
    /// Разбирает <c>?Подпись=значение|вариант|значение:подпись</c>.
    /// null — это не параметр, а что-то другое в скобках.
    /// </summary>
    private static TemplateParameter? ParseParameter(string body)
    {
        if (!body.StartsWith('?')) return null;

        var parts = body[1..].Split('|');

        var head = parts[0];
        int eq = head.IndexOf('=');
        var label = (eq < 0 ? head : head[..eq]).Trim();
        if (label.Length == 0) return null;
        var def = eq < 0 ? "" : head[(eq + 1)..].Trim();

        var options = new List<TemplateOption>();
        for (int i = 1; i < parts.Length; i++)
        {
            var option = parts[i].Trim();
            if (option.Length == 0) continue;

            int colon = option.IndexOf(':');
            options.Add(colon < 0
                ? new TemplateOption(option, "")
                : new TemplateOption(option[..colon].Trim(), option[(colon + 1)..].Trim()));
        }

        // Значение по умолчанию не указано, но варианты есть — берём первый.
        if (def.Length == 0 && options.Count > 0) def = options[0].Value;

        return new TemplateParameter(label, def, options);
    }

    /// <summary>Значение поля игрока, или null — если такого поля нет.</summary>
    private static string? PlayerField(string name, PlayerInfo? p) => name.ToLowerInvariant() switch
    {
        "idx" or "index" => p is null ? "" : p.Idx.ToString(CultureInfo.InvariantCulture),
        "char" or "charname" => p?.CharName ?? "",
        "player" or "playername" => p?.PlayerName ?? "",
        "userid" => p?.UserId ?? "",
        "platformid" or "steamid" => p?.PlatformId ?? "",
        "platform" => p?.Platform ?? "",
        _ => null,
    };
}
