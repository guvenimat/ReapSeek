using ReapSeek.Shared.Identity;

namespace ReapSeek.UnitTests;

public class AccountTokenTests
{
    [Fact]
    public void Tokens_are_random_url_safe_and_stored_only_as_a_hash()
    {
        var a = AccountTokens.New();
        var b = AccountTokens.New();
        Assert.NotEqual(a, b);
        Assert.True(AccountTokens.LooksValid(a));
        Assert.Equal(64, AccountTokens.Hash(a).Length);
        Assert.Equal(AccountTokens.Hash(a), AccountTokens.Hash(a));
        Assert.DoesNotContain(a, AccountTokens.Hash(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Malformed_tokens_are_rejected_before_any_lookup(string? token) => Assert.False(AccountTokens.LooksValid(token));

    [Fact]
    public void A_reset_link_is_short_lived_and_a_verification_link_lasts_days()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), AccountTokens.Lifetime(AccountTokens.PasswordReset));
        Assert.Equal(TimeSpan.FromDays(3), AccountTokens.Lifetime(AccountTokens.EmailVerification));
    }
}
