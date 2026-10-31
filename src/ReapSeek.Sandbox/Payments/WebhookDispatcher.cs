using System.Text;
using System.Text.Json;

namespace ReapSeek.Sandbox.Payments;

public sealed class WebhookDispatcher(IConfiguration cfg, IHttpClientFactory http, ILogger<WebhookDispatcher> log)
{
    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    readonly string _secret = cfg["Sandbox:WebhookSecret"] ?? throw new InvalidOperationException("Sandbox:WebhookSecret is not set");

    public void Send(PaymentRecord payment) => _ = Task.Run(() => DeliverAsync(payment, PaymentStore.NewId("evt")));

    public Task DeliverAsync(PaymentRecord payment, string eventId) =>
        PostAsync(payment.WebhookUrl, eventId, payment.Id, JsonSerializer.Serialize(new WebhookEvent(eventId, $"payment.{payment.Status}", DateTime.UtcNow,
            new WebhookData(payment.Id, payment.Reference, payment.Amount, payment.Currency, payment.Status, payment.FailureReason, payment.CardLast4)),
            SandboxJsonContext.Default.WebhookEvent));

    public void SendRaw(string url, string eventId, string subject, string body) => _ = Task.Run(() => PostAsync(url, eventId, subject, body));

    async Task PostAsync(string url, string eventId, string subject, string body)
    {
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add(WebhookSignature.Header, WebhookSignature.Create(_secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body));
                using var res = await http.CreateClient("webhooks").SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    log.LogInformation("Delivered {Event} for {Subject} ({Status})", eventId, subject, (int)res.StatusCode);
                    return;
                }
                log.LogWarning("Webhook {Event} for {Subject} got {Status}", eventId, subject, (int)res.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.LogWarning("Webhook {Event} for {Subject} failed: {Message}", eventId, subject, ex.Message);
            }
            if (attempt < RetryDelays.Length) await Task.Delay(RetryDelays[attempt]);
        }
        log.LogError("Gave up delivering {Event} for {Subject}", eventId, subject);
    }
}
