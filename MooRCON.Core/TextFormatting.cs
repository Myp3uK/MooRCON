namespace MooRCON.Core;

public static class TextFormatting
{
    /// <summary>
    /// Приводит любые переводы строк к \r\n. Серверы (напр. DayZ Enhanced) часто
    /// разделяют строки одиночным \n — в WPF это не критично, но нормализуем для
    /// единообразия вывода.
    /// </summary>
    public static string NormalizeNewlines(string s) =>
        s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
}
