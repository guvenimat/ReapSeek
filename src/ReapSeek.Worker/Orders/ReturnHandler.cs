using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared.Ledger;
using ReapSeek.Shared.Outbox;
using ReapSeek.Shared.Providers;
using ReapSeek.Shared.Returns;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class ReturnHandler
{
    public sealed record PersonRecord(string Email, string Name);

    readonly NpgsqlDataSource _db;
    readonly EmailSender _email;
    readonly ReapDeepIngestClient _reapDeep;
    readonly TimeProvider _clock;
    readonly ILogger<ReturnHandler> _log;
    readonly IPaymentGateway _payments;
    readonly string _storefrontUrl;
    readonly string _backofficeUrl;

    public ReturnHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IPaymentGateway payments, IConfiguration cfg, TimeProvider clock, ILogger<ReturnHandler> log)
    {
        _db = db;
        _email = email;
        _reapDeep = reapDeep;
        _clock = clock;
        _log = log;
        _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
        _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');
        _payments = payments;
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
        var refundId = await _payments.RefundAsync(paymentId, request.RefundAmount.Amount, $"return-{request.Id}", ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        var locked = await ReturnStore.LoadAsync(conn, tx, request.Id);
        if (locked?.Status != ReturnStatus.Received) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        var entries = locked.MarkRefunded(refundId, now);
        await ReturnStore.SaveAsync(conn, tx, locked);
        await LedgerStore.InsertAsync(conn, tx, new LedgerInsertRecord(locked.SellerId, locked.PackageId, locked.Id, locked.OrderNumber,
            entries.Select(e => e.Kind).ToArray(), entries.Select(e => e.Amount).ToArray(), now));
        await tx.CommitAsync(ct);
        _log.LogInformation("Refunded {Amount} for {Return} ({Refund})", locked.RefundAmount, locked.Number, refundId);
    }

    static async Task<PersonRecord> CustomerAsync(NpgsqlConnection conn, ReturnRequest request) =>
        await conn.QuerySingleAsync<PersonRecord>("SELECT email, name FROM users WHERE id = @Id", new { Id = request.CustomerId });

}
