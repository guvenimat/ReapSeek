using Dapper;
using Npgsql;

namespace ReapSeek.Shared.Ledger;

public sealed record LedgerInsertRecord(long SellerId, Guid PackageId, Guid? ReturnId, string OrderNumber, string[] Kinds, decimal[] Amounts, DateTime AvailableAt);

public static class LedgerStore
{
    public static Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, LedgerInsertRecord entries) =>
        entries.ReturnId is null
            ? conn.ExecuteAsync("""
                INSERT INTO seller_ledger (seller_id, package_id, return_id, order_number, kind, amount, available_at)
                SELECT @SellerId, @PackageId, NULL, @OrderNumber, k, a, @AvailableAt FROM unnest(@Kinds, @Amounts) AS e(k, a)
                ON CONFLICT (package_id, kind) WHERE return_id IS NULL DO NOTHING
                """, entries, tx)
            : conn.ExecuteAsync("""
                INSERT INTO seller_ledger (seller_id, package_id, return_id, order_number, kind, amount, available_at)
                SELECT @SellerId, @PackageId, @ReturnId, @OrderNumber, k, a, @AvailableAt FROM unnest(@Kinds, @Amounts) AS e(k, a)
                ON CONFLICT (return_id, kind) WHERE return_id IS NOT NULL DO NOTHING
                """, entries, tx);
}
