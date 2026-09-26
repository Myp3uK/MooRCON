using System.Text;

namespace MooRCON.Core;

/// <summary>
/// Защита файлов данных (servers.json, history.json) от порчи и потери.
/// </summary>
public static class FileSafety
{
    /// <summary>
    /// Атомарная запись: сначала во временный файл рядом, затем подмена оригинала.
    /// Сбой посреди записи (вылет, выключение ПК) оставит старый файл целым, а не обрезанный.
    /// </summary>
    public static void WriteAllTextAtomic(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Копирует нечитаемый файл в "&lt;имя&gt;.corrupt-&lt;дата&gt;" рядом с оригиналом, чтобы
    /// следующее сохранение не уничтожило данные, которые ещё можно поправить руками.
    /// Возвращает путь копии или null, если скопировать не удалось.
    /// </summary>
    public static string? BackupCorrupt(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backup, overwrite: true);
            return backup;
        }
        catch
        {
            return null;
        }
    }
}
