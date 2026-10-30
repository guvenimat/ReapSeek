using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReapSeek.Sandbox.Payments;

public sealed class PaymentStore
{
    readonly ConcurrentDictionary<string, Payment> _payments = new(StringComparer.Ordinal);

    public Payment Add(Payment payment)
    {
        _payments[payment.Id] = payment;
        return payment;
    }

    public Payment? Find(string id) => _payments.GetValueOrDefault(id);

    public static string NewId(string prefix) => $"{prefix}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
}

public sealed class WebhookDispatcher(IConfiguration cfg, IHttpClientFactory http, ILogger<WebhookDispatcher> log)
{
    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    readonly string _secret = cfg["Sandbox:WebhookSecret"] ?? throw new InvalidOperationException("Sandbox:WebhookSecret is not set");

    public void Send(Payment payment) => _ = Task.Run(() => DeliverAsync(payment, PaymentStore.NewId("evt")));

    public async Task DeliverAsync(Payment payment, string eventId)
    {
        var body = JsonSerializer.Serialize(new WebhookEvent(eventId, $"payment.{payment.Status}", DateTime.UtcNow,
            new WebhookData(payment.Id, payment.Reference, payment.Amount, payment.Currency, payment.Status, payment.FailureReason, payment.CardLast4)),
            SandboxJsonContext.Default.WebhookEvent);
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, payment.WebhookUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add(WebhookSignature.Header, WebhookSignature.Create(_secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body));
                using var res = await http.CreateClient("webhooks").SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    log.LogInformation("Delivered {Event} for {Payment} ({Status})", eventId, payment.Id, (int)res.StatusCode);
                    return;
                }
                log.LogWarning("Webhook {Event} for {Payment} got {Status}", eventId, payment.Id, (int)res.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.LogWarning("Webhook {Event} for {Payment} failed: {Message}", eventId, payment.Id, ex.Message);
            }
            if (attempt < RetryDelays.Length) await Task.Delay(RetryDelays[attempt]);
        }
        log.LogError("Gave up delivering {Event} for {Payment}", eventId, payment.Id);
    }
}
