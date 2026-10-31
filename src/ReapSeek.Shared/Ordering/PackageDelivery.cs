using Dapper;
using Npgsql;
using ReapSeek.Domain.Ledger;
using ReapSeek.Shared.Ledger;

namespace ReapSeek.Shared.Ordering;

public static class PackageDelivery
{
    public sealed record PackageIdRecord(Guid Id);

    public static async Task DeliverAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid orderId, Guid packageId, DateTime deliveredAt, TimeSpan hold)
    {
        var order = await OrderStore.LoadAsync(conn, tx, orderId) ?? throw new InvalidOperationException($"Order {orderId} of package {packageId} was not found");
        var package = order.DeliverPackage(packageId, deliveredAt);
        var rate = await conn.ExecuteScalarAsync<decimal>("SELECT commission_rate FROM order_packages WHERE id = @Id", new PackageIdRecord(packageId), tx);
        var entries = Settlement.ForPackage(package, rate);
        await OrderStore.SaveAsync(conn, tx, order);
        await LedgerStore.InsertAsync(conn, tx, new LedgerInsertRecord(package.SellerId, package.Id, null, order.Number,
            entries.Select(e => e.Kind).ToArray(), entries.Select(e => e.Amount).ToArray(), Settlement.AvailableAt(deliveredAt, hold)));
    }
}
