using System.Security.Cryptography;
using System.Text;

namespace MooRCON.Core;

/// <summary>
/// Шифрование паролей RCON через Windows DPAPI (per-user). В файле значение
/// хранится как "enc:&lt;base64&gt;". Старый plaintext читается как есть и
/// перешифровывается при следующем сохранении.
/// </summary>
public static class PasswordProtector
{
    private const string EncPrefix = "enc:";

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
#pragma warning disable CA1416
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            return EncPrefix + Convert.ToBase64String(bytes);
        }
        catch
        {
            return plain; // DPAPI недоступна (не Windows) — оставляем как есть
        }
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(EncPrefix, StringComparison.Ordinal)) return stored;
        try
        {
            var bytes = Convert.FromBase64String(stored[EncPrefix.Length..]);
#pragma warning disable CA1416
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }
}
