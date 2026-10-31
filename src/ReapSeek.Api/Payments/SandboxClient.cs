using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Payments;

public sealed record SandboxCreatePayment(decimal Amount, string Currency, string Reference, string Description, string ReturnUrl, string WebhookUrl);
public sealed record SandboxPayment(string Id, string Status, decimal Amount, string Currency, string Reference, string RedirectUrl);
public sealed record SandboxCreateShipment(string Reference);
public sealed record SandboxShipment(string TrackingNumber, string Carrier);
public sealed record SandboxWebhookData(string PaymentId, string Reference, decimal Amount, string Currency, string Status, string? FailureReason, string? CardLast4);
public sealed record SandboxWebhookEvent(string Id, string Type, DateTime Created, SandboxWebhookData Data);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SandboxCreatePayment))]
[JsonSerializable(typeof(SandboxPayment))]
[JsonSerializable(typeof(SandboxWebhookEvent))]
[JsonSerializable(typeof(SandboxCreateShipment))]
[JsonSerializable(typeof(SandboxShipment))]
public sealed partial class SandboxJsonContext : JsonSerializerContext;

public sealed class PaymentProviderException(string message) : Exception(message);

public sealed class SandboxClient : IDisposable
{
    public const string Provider = "sandbox";
    readonly HttpClient _http;

    public SandboxClient(IConfiguration cfg)
    {
        var url = cfg["Payments:Sandbox:ApiUrl"] ?? "http://localhost:8101";
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg["Payments:Sandbox:SecretKey"] ?? "");
    }

    public async Task<SandboxPayment> CreateAsync(SandboxCreatePayment request, CancellationToken ct)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync("v1/payments", request, SandboxJsonContext.Default.SandboxCreatePayment, ct);
            if (!res.IsSuccessStatusCode)
                throw new PaymentProviderException($"The payment provider returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
            return await res.Content.ReadFromJsonAsync(SandboxJsonContext.Default.SandboxPayment, ct)
                ?? throw new PaymentProviderException("The payment provider returned an empty response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PaymentProviderException($"The payment provider is unreachable: {ex.Message}");
        }
    }

    public async Task<SandboxShipment> CreateShipmentAsync(string reference, CancellationToken ct)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync("v1/shipments", new SandboxCreateShipment(reference), SandboxJsonContext.Default.SandboxCreateShipment, ct);
            if (!res.IsSuccessStatusCode)
                throw new ShippingProviderException($"The carrier returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
            return await res.Content.ReadFromJsonAsync(SandboxJsonContext.Default.SandboxShipment, ct)
                ?? throw new ShippingProviderException("The carrier returned an empty response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ShippingProviderException($"The carrier is unreachable: {ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}

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
