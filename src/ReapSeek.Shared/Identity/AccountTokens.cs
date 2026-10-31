using System.Security.Cryptography;
using System.Text;

namespace ReapSeek.Shared.Identity;

public static class AccountEmailKinds
{
    public const string PasswordReset = AccountTokens.PasswordReset;
    public const string EmailVerification = AccountTokens.EmailVerification;
    public const string PasswordChanged = "password_changed";
}

public static class AccountTokens
{
    public const string PasswordReset = "password_reset";
    public const string EmailVerification = "email_verification";
    public const int MaxRequestsPerHour = 3;

    public static TimeSpan Lifetime(string purpose) => purpose switch
    {
        PasswordReset => TimeSpan.FromMinutes(30),
        EmailVerification => TimeSpan.FromDays(3),
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };

    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool LooksValid(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
