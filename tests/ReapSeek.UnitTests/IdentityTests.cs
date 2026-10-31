using ReapSeek.Shared.Identity;
using Microsoft.AspNetCore.Http;
using ReapSeek.Api.Identity;
using ReapSeek.Shared;

namespace ReapSeek.UnitTests;

public class IdentityTests
{
    [Fact]
    public void Password_hash_verifies_only_the_original_password()
    {
        var hash = PasswordHasher.Hash("correct horse battery");
        Assert.StartsWith("pbkdf2-sha256$600000$", hash);
        Assert.True(PasswordHasher.Verify("correct horse battery", hash));
        Assert.False(PasswordHasher.Verify("correct horse battery!", hash));
        Assert.NotEqual(hash, PasswordHasher.Hash("correct horse battery"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("md5$1$abc$def")]
    public void Malformed_stored_hashes_never_verify(string? stored) =>
        Assert.False(PasswordHasher.Verify("whatever-password", stored));

    [Theory]
    [InlineData("  Jane.Doe@Example.COM ", "jane.doe@example.com")]
    [InlineData("a@b.example", "a@b.example")]
    public void Emails_are_trimmed_and_lowercased(string input, string expected) =>
        Assert.Equal(expected, IdentityRules.NormalizeEmail(input));

    [Theory]
    [InlineData("jane@example.com", true)]
    [InlineData("jane+shop@mail.example.org", true)]
    [InlineData("jane@example", false)]
    [InlineData("jane example@example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("jane@@example.com", false)]
    public void Email_format_is_checked(string email, bool valid) =>
        Assert.Equal(valid, IdentityRules.IsValidEmail(email));

    [Fact]
    public void Registration_is_normalized()
    {
        var r = IdentityRules.Validate(new RegisterRequest(" Jane@Example.com", "  Jane Doe ", "secret-pass"));
        Assert.Equal(new Registration("jane@example.com", "Jane Doe", "secret-pass"), r);
    }

    [Theory]
    [InlineData("not-an-email", "Jane", "secret-pass", "email")]
    [InlineData("jane@example.com", " ", "secret-pass", "name")]
    [InlineData("jane@example.com", "Jane", "short", "Password")]
    [InlineData("jane@example.com", "Jane", null, "Password")]
    public void Invalid_registrations_are_rejected(string email, string name, string? password, string mentions)
    {
        var ex = Assert.Throws<ValidationException>(() => IdentityRules.Validate(new RegisterRequest(email, name, password)));
        Assert.Contains(mentions, ex.Message);
    }

    [Fact]
    public void Names_longer_than_the_limit_are_rejected() =>
        Assert.Throws<ValidationException>(() => IdentityRules.Validate(new RegisterRequest("jane@example.com", new string('a', 101), "secret-pass")));

    [Fact]
    public void Storefront_and_back_office_sessions_use_separate_cookies()
    {
        Assert.NotEqual(SessionAudience.Storefront.CookieName, SessionAudience.Backoffice.CookieName);
        Assert.Equal(SameSiteMode.Strict, SessionAudience.Backoffice.SameSite);
        Assert.True(SessionAudience.Backoffice.Lifetime < SessionAudience.Storefront.Lifetime);
    }
}
