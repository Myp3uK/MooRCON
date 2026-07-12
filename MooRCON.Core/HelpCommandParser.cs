using System.Text.RegularExpressions;

namespace MooRCON.Core;

/// <summary>
/// Разбирает вывод команды "help" и достаёт имена команд для автодополнения.
/// Пропускает строки "Commands:" и "Usage: ..." (формат DayZ Enhanced и подобных).
/// </summary>
public static partial class HelpCommandParser
{
    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9_]+)")]
    private static partial Regex CommandRegex();

    public static List<string> Parse(string helpText)
    {
        var commands = new List<string>();
        var lines = helpText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("Commands:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
                continue;

            var match = CommandRegex().Match(trimmed);
            if (match.Success)
            {
                var cmd = match.Groups[1].Value.ToLowerInvariant();
                if (cmd.Length > 0 && cmd.Length <= 30)
                    commands.Add(cmd);
            }
        }

        return commands.Distinct().OrderBy(c => c).ToList();
    }
}
