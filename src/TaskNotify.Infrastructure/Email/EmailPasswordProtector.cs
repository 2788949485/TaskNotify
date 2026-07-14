using System.Security.Cryptography;
using System.Text;

namespace TaskNotify.Infrastructure.Email;

/// <summary>
/// Wraps <see cref="ProtectedData"/> with a base64 string facade so secrets stay
/// opaque in <c>email.json</c>. Scope is <see cref="DataProtectionScope.CurrentUser"/>
/// — ciphertext is only recoverable by the same Windows account that produced it.
///
/// All methods are total: any failure (bad input, DPAPI unavailable, corrupted
/// ciphertext) returns an empty string instead of throwing. Callers treat empty
/// as "no password configured" rather than as an error to surface.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class EmailPasswordProtector
{
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var cipher = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Decrypt(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return string.Empty;
        try
        {
            var cipher = Convert.FromBase64String(base64);
            var bytes = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
