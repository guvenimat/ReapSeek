using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ReapSeek.Shared.Providers;

public sealed record SandboxCreatePayment(decimal Amount, string Currency, string Reference, string Description, string ReturnUrl, string WebhookUrl);
public sealed record SandboxPayment(string Id, string Status, decimal Amount, string Currency, string Reference, string RedirectUrl);
public sealed record SandboxCreateShipment(string Reference, string Kind, string? WebhookUrl);
public sealed record SandboxShipment(string TrackingNumber, string Carrier);
public sealed record SandboxRefundRequest(decimal Amount, string Reference);
public sealed record SandboxRefund(string Id, string Status);
public sealed record SandboxTransferRequest(decimal Amount, string Currency, string Reference, string Recipient);
public sealed record SandboxTransfer(string Id, string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SandboxCreatePayment))]
[JsonSerializable(typeof(SandboxPayment))]
[JsonSerializable(typeof(SandboxCreateShipment))]
[JsonSerializable(typeof(SandboxShipment))]
[JsonSerializable(typeof(SandboxRefundRequest))]
[JsonSerializable(typeof(SandboxRefund))]
[JsonSerializable(typeof(SandboxTransferRequest))]
[JsonSerializable(typeof(SandboxTransfer))]
public sealed partial class SandboxProviderJsonContext : JsonSerializerContext;

public sealed class SandboxProvider : IPaymentGateway, ICarrier, IPayoutBank, IDisposable
{
    public const string ProviderName = "sandbox";

    readonly HttpClient _http;
    readonly string? _shippingWebhookUrl;

    public SandboxProvider(IConfiguration cfg)
    {
        _shippingWebhookUrl = cfg["Shipping:WebhookUrl"];
        var url = cfg["Payments:Sandbox:ApiUrl"] ?? "http://localhost:8101";
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg["Payments:Sandbox:SecretKey"] ?? "");
    }

    public string Name => ProviderName;

    public async Task<PaymentSession> CreatePaymentAsync(NewPayment p, CancellationToken ct)
    {
        var payment = await PostAsync("v1/payments", new SandboxCreatePayment(p.Amount, p.Currency, p.Reference, p.Description, p.ReturnUrl, p.WebhookUrl),
            SandboxProviderJsonContext.Default.SandboxCreatePayment, SandboxProviderJsonContext.Default.SandboxPayment, "The payment provider", Payment, ct);
        return new PaymentSession(payment.Id, payment.RedirectUrl, payment.Amount, payment.Currency);
    }

    public async Task<string> RefundAsync(string paymentId, decimal amount, string reference, CancellationToken ct) =>
        (await PostAsync($"v1/payments/{Uri.EscapeDataString(paymentId)}/refunds", new SandboxRefundRequest(amount, reference),
            SandboxProviderJsonContext.Default.SandboxRefundRequest, SandboxProviderJsonContext.Default.SandboxRefund, "The payment provider", Payment, ct)).Id;

    public async Task<Shipment> CreateShipmentAsync(string reference, string kind, CancellationToken ct)
    {
        var shipment = await PostAsync("v1/shipments", new SandboxCreateShipment(reference, kind, _shippingWebhookUrl),
            SandboxProviderJsonContext.Default.SandboxCreateShipment, SandboxProviderJsonContext.Default.SandboxShipment, "The carrier", Shipping, ct);
        return new Shipment(shipment.TrackingNumber, shipment.Carrier);
    }

    public async Task<string> TransferAsync(decimal amount, string currency, string reference, string recipient, CancellationToken ct) =>
        (await PostAsync("v1/transfers", new SandboxTransferRequest(amount, currency, reference, recipient),
            SandboxProviderJsonContext.Default.SandboxTransferRequest, SandboxProviderJsonContext.Default.SandboxTransfer, "The bank", Payment, ct)).Id;

    static Exception Payment(string message) => new PaymentProviderException(message);

    static Exception Shipping(string message) => new ShippingProviderException(message);

    async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResponse> responseType,
        string who, Func<string, Exception> fail, CancellationToken ct)
    {
        try
        {
            using var res = await _http.PostAsJsonAsync(path, body, requestType, ct);
            if (!res.IsSuccessStatusCode) throw fail($"{who} returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
            return await res.Content.ReadFromJsonAsync(responseType, ct) ?? throw fail($"{who} returned an empty response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw fail($"{who} is unreachable: {ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}

public static class ProviderRegistration
{
    public static IServiceCollection AddSandboxProviders(this IServiceCollection services) => services
        .AddSingleton<SandboxProvider>()
        .AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<SandboxProvider>())
        .AddSingleton<ICarrier>(sp => sp.GetRequiredService<SandboxProvider>())
        .AddSingleton<IPayoutBank>(sp => sp.GetRequiredService<SandboxProvider>());
}
