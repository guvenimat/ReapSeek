using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ReapSeek.Shared.Providers;

public sealed record SandboxWebhookData(string PaymentId, string Reference, decimal Amount, string Currency, string Status, string? FailureReason, string? CardLast4);
public sealed record SandboxWebhookEvent(string Id, string Type, DateTime Created, SandboxWebhookData Data);
public sealed record SandboxShipmentData(string TrackingNumber, string Reference, string Kind, string Status, DateTime OccurredAt);
public sealed record SandboxShipmentEvent(string Id, string Type, DateTime Created, SandboxShipmentData Data);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SandboxWebhookEvent))]
[JsonSerializable(typeof(SandboxShipmentEvent))]
public sealed partial class SandboxJsonContext : JsonSerializerContext;

public static class WebhookVerifier
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(string? header, string body, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret)) return false;
        string? t = null, v1 = null;
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") t = kv[1];
            else if (kv[0] == "v1") v1 = kv[1];
        }
        if (t is null || v1 is null || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(timestamp)).Duration() > Tolerance) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{body}"));
        byte[] actual;
        try
        {
            actual = Convert.FromHexString(v1);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
