using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.Domain.Returns;
using ReapSeek.Modules.Ordering;
using ReapSeek.Ordering.Contracts;
using ReapSeek.Ordering.Infrastructure;
using ReapSeek.Shared.Providers;

namespace ReapSeek.Ordering.Jobs;

internal sealed class ReturnRefundHandler(NpgsqlDataSource db, IPaymentGateway payments, TimeProvider clock, ILogger<ReturnRefundHandler> log) : IReturnRefunds
{
    public async Task RefundAsync(Guid returnId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var request = await ReturnStore.LoadAsync(conn, null, returnId) ?? throw new InvalidOperationException($"Return {returnId} was not found");
        if (request.Status != ReturnStatus.Received) return;
        var order = await OrderStore.LoadByNumberAsync(conn, null, request.OrderNumber);
        var paymentId = order?.PaymentReference ?? throw new InvalidOperationException($"Order {request.OrderNumber} has no payment");
        var refundId = await payments.RefundAsync(paymentId, request.RefundAmount.Amount, $"return-{request.Id}", ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        var locked = await ReturnStore.LoadAsync(conn, tx, request.Id);
        if (locked?.Status != ReturnStatus.Received) return;
        locked.MarkRefunded(refundId, clock.GetUtcNow().UtcDateTime);
        await ReturnStore.SaveAsync(conn, tx, locked);
        await OrderingEvents.ReturnRefundedAsync(conn, tx, locked);
        await tx.CommitAsync(ct);
        log.LogInformation("Refunded {Amount} for {Return} ({Refund})", locked.RefundAmount, locked.Number, refundId);
    }
}
