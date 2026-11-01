namespace MooRCON.Core;

/// <summary>
/// Одна строка вывода listplayers.
///
/// <see cref="Idx"/> летучий: он меняется, когда игроки заходят и выходят, поэтому
/// запоминать игрока между запросами нужно по <see cref="Key"/>, а индекс перечитывать
/// заново прямо перед отправкой команды — иначе команда уйдёт не тому игроку.
/// </summary>
public sealed class PlayerInfo
{
    public int Idx { get; init; }
    public string CharName { get; init; } = "";
    public string PlayerName { get; init; } = "";
    public string UserId { get; init; } = "";
    public string PlatformId { get; init; } = "";
    public string Platform { get; init; } = "";

    /// <summary>
    /// Устойчивый признак игрока: id платформы (Steam и прочие) надёжнее всего, дальше —
    /// id пользователя, в крайнем случае имя персонажа.
    /// </summary>
    public string Key =>
        !string.IsNullOrWhiteSpace(PlatformId) ? PlatformId :
        !string.IsNullOrWhiteSpace(UserId) ? UserId : CharName;

    /// <summary>Что показывать в списке игроков.</summary>
    public string Display =>
        !string.IsNullOrWhiteSpace(CharName) ? CharName :
        !string.IsNullOrWhiteSpace(PlayerName) ? PlayerName : $"#{Idx}";

    /// <summary>Вторая строка в списке: аккаунт и платформа, если они отличаются от имени.</summary>
    public string Details
    {
        get
        {
            var parts = new List<string> { $"idx {Idx}" };
            if (!string.IsNullOrWhiteSpace(PlayerName) &&
                !string.Equals(PlayerName, CharName, StringComparison.Ordinal))
                parts.Add(PlayerName);
            if (!string.IsNullOrWhiteSpace(Platform)) parts.Add(Platform);
            return string.Join(" • ", parts);
        }
    }

    public bool SameAs(PlayerInfo other) =>
        string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
}
