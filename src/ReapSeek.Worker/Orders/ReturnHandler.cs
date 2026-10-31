using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared.Ledger;
using ReapSeek.Shared.Outbox;
using ReapSeek.Shared.Returns;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed record SandboxRefundRequest(decimal Amount, string Reference);
public sealed record SandboxRefund(string Id, string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SandboxRefundRequest))]
[JsonSerializable(typeof(SandboxRefund))]
public sealed partial class RefundJsonContext : JsonSerializerContext;

public sealed class ReturnHandler : IDisposable
{
    public sealed record PersonRecord(string Email, string Name);

    readonly NpgsqlDataSource _db;
    readonly EmailSender _email;
    readonly ReapDeepIngestClient _reapDeep;
    readonly TimeProvider _clock;
    readonly ILogger<ReturnHandler> _log;
    readonly HttpClient _sandbox;
    readonly string _storefrontUrl;
    readonly string _backofficeUrl;

    public ReturnHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IConfiguration cfg, TimeProvider clock, ILogger<ReturnHandler> log)
    {
        _db = db;
        _email = email;
        _reapDeep = reapDeep;
        _clock = clock;
        _log = log;
        _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
        _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');
        _sandbox = new HttpClient { BaseAddress = new Uri((cfg["Payments:Sandbox:ApiUrl"] ?? "http://localhost:8101").TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        _sandbox.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg["Payments:Sandbox:SecretKey"] ?? "");
    }

    public static bool Handles(string type) => type is OutboxTypes.ReturnRequested or OutboxTypes.ReturnApproved or OutboxTypes.ReturnRejected
        or OutboxTypes.ReturnReceived or OutboxTypes.ReturnRefunded;

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.ReturnEventMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await _db.OpenConnectionAsync(ct);
        var request = await ReturnStore.LoadAsync(conn, null, payload.ReturnId)
            ?? throw new InvalidOperationException($"Return {payload.Number} was not found");

        switch (message.Type)
        {
            case OutboxTypes.ReturnReceived:
                await RefundAsync(conn, request, ct);
                break;
            case OutboxTypes.ReturnRefunded:
                if (_reapDeep.IsConfigured)
                {
                    var events = RefundEvents.For(request);
                    await _reapDeep.CaptureAsync(events, ct);
                    _log.LogInformation("Sent {Count} refund events for {Return} to ReapDeep", events.Count, request.Number);
                }
                await _email.SendAsync(EmailTemplates.ReturnUpdate(request, await CustomerAsync(conn, request), _storefrontUrl), ct);
                break;
            case OutboxTypes.ReturnRequested:
                foreach (var seller in await conn.QueryAsync<PersonRecord>("SELECT email, name FROM users WHERE seller_id = @SellerId", new { request.SellerId }))
                    await _email.SendAsync(EmailTemplates.ReturnRequestedForSeller(request, seller.Email, seller.Name, _backofficeUrl), ct);
                break;
            default:
                await _email.SendAsync(EmailTemplates.ReturnUpdate(request, await CustomerAsync(conn, request), _storefrontUrl), ct);
                break;
        }
    }

    async Task RefundAsync(NpgsqlConnection conn, ReturnRequest request, CancellationToken ct)
    {
        if (request.Status != ReturnStatus.Received) return;
        var paymentId = await conn.ExecuteScalarAsync<string?>("SELECT payment_reference FROM orders WHERE id = @Id", new { Id = request.OrderId })
            ?? throw new InvalidOperationException($"Order {request.OrderNumber} has no payment");
        using var res = await _sandbox.PostAsJsonAsync($"v1/payments/{Uri.EscapeDataString(paymentId)}/refunds",
            new SandboxRefundRequest(request.Total.Amount, $"return-{request.Id}"), RefundJsonContext.Default.SandboxRefundRequest, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"The payment provider returned {(int)res.StatusCode} for the refund of {request.Number}: {await res.Content.ReadAsStringAsync(ct)}");
        var refund = await res.Content.ReadFromJsonAsync(RefundJsonContext.Default.SandboxRefund, ct) ?? throw new HttpRequestException("Empty refund response");

        await using var tx = await conn.BeginTransactionAsync(ct);
        var locked = await ReturnStore.LoadAsync(conn, tx, request.Id);
        if (locked?.Status != ReturnStatus.Received) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        var entries = locked.MarkRefunded(refund.Id, now);
        await ReturnStore.SaveAsync(conn, tx, locked);
        await LedgerStore.InsertAsync(conn, tx, new LedgerInsertRecord(locked.SellerId, locked.PackageId, locked.Id, locked.OrderNumber,
            entries.Select(e => e.Kind).ToArray(), entries.Select(e => e.Amount).ToArray(), now));
        await tx.CommitAsync(ct);
        _log.LogInformation("Refunded {Amount} for {Return} ({Refund})", locked.Total, locked.Number, refund.Id);
    }

    static async Task<PersonRecord> CustomerAsync(NpgsqlConnection conn, ReturnRequest request) =>
        await conn.QuerySingleAsync<PersonRecord>("SELECT email, name FROM users WHERE id = @Id", new { Id = request.CustomerId });

    public void Dispose() => _sandbox.Dispose();
}
