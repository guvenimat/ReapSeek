using System.Security.Cryptography;
using System.Text;

namespace ReapSeek.Marketing.Application;

internal static class AlertSignature
{
    public const string Prefix = "sha256=";

    public static bool IsValid(string? header, byte[] body, string secret)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.Ordinal) || string.IsNullOrEmpty(secret)) return false;
        byte[] given;
        try
        {
            given = Convert.FromHexString(header[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body), given);
    }
}
