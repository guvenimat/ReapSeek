using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Ordering;

namespace ReapSeek.Api.Orders;

public enum CancelOutcome { Cancelled, NotFound }

public sealed class OrderCancellation(NpgsqlDataSource db, TimeProvider clock, ILogger<OrderCancellation> log)
{
    public sealed record OwnedOrderRecord(string Number, long CustomerId);

    public async Task<CancelOutcome> CancelAsync(string number, long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.ExecuteScalarAsync<Guid?>("SELECT id FROM orders WHERE number = @Number AND customer_id = @CustomerId",
            new OwnedOrderRecord(number, customerId), tx);
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
