using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Modules.Ordering;

namespace ReapSeek.Ordering.Application;

internal enum CancelOutcome { Cancelled, NotFound }

internal sealed class OrderCancellation(NpgsqlDataSource db, TimeProvider clock, ILogger<OrderCancellation> log)
{
    public async Task<CancelOutcome> CancelAsync(string number, long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await OrderStore.FindOwnedIdAsync(conn, tx, number, customerId);
        if (id is null || await OrderStore.LoadAsync(conn, tx, id.Value) is not { } order) return CancelOutcome.NotFound;
        var now = clock.GetUtcNow().UtcDateTime;
        var paid = order.Status == OrderStatus.Paid;
        var changed = paid ? order.CancelPaid(CancelReasons.Customer, now) : order.Cancel(CancelReasons.Customer, now);
        if (changed)
        {
            await OrderStore.SaveAsync(conn, tx, order);
            log.LogInformation("Customer {Customer} cancelled order {Order} ({State})", customerId, order.Number, paid ? "paid, refund queued" : "unpaid");
        }
        await tx.CommitAsync(ct);
        return CancelOutcome.Cancelled;
    }
}
