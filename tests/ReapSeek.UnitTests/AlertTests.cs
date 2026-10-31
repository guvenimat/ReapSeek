using System.Security.Cryptography;
using System.Text;
using ReapSeek.Api.Alerts;

namespace ReapSeek.UnitTests;

public class AlertTests
{
    static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"alert_id":7,"state":"firing"}""");

    static string Sign(string secret, byte[] body) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    [Fact]
    public void A_reapdeep_signature_is_checked_against_the_alert_secret()
    {
        Assert.True(AlertSignature.IsValid(Sign("whsec_1", Body), Body, "whsec_1"));
        Assert.False(AlertSignature.IsValid(Sign("whsec_2", Body), Body, "whsec_1"));
        Assert.False(AlertSignature.IsValid(Sign("whsec_1", Body), Encoding.UTF8.GetBytes("""{"alert_id":7,"state":"ok"}"""), "whsec_1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5=abcd")]
    [InlineData("sha256=not-hex")]
    public void Malformed_signatures_are_rejected(string? header) =>
        Assert.False(AlertSignature.IsValid(header, Body, "whsec_1"));
}
