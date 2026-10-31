using ReapSeek.Shared.Identity;
using System.Text.RegularExpressions;
using ReapSeek.Shared;

namespace ReapSeek.Api.Identity;

public sealed record Registration(string Email, string Name, string Password);

public static partial class IdentityRules
{
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 100;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email) => email.Length <= MaxEmailLength && EmailPattern().IsMatch(email);

    public static Registration Validate(RegisterRequest req)
    {
        var email = NormalizeEmail(req.Email ?? "");
        Ensure.That(IsValidEmail(email), "A valid email address is required");
        var name = Ensure.NotBlank(req.Name, "name", MaxNameLength);
        Ensure.That(PasswordHasher.IsValidLength(req.Password), PasswordHasher.LengthRule);
        return new Registration(email, name, req.Password);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
