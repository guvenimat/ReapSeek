using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Ordering;

public sealed record PurchaseKeyRecord(long ProductId, long CustomerId);
public sealed record OrderIdsRecord(Guid[] OrderIds);
public sealed record PaidTotalRecord(int Count, decimal Total);
public sealed record PurchasePairsRecord(long[] CustomerIds, long[] ProductIds);
public sealed record DeliveredSellerRecord(long CustomerId, long ProductId, long SellerId);

public static class OrderLookups
{
    public static Task<string?> DeliveredVariantAsync(NpgsqlConnection conn, long productId, long customerId) =>
        conn.ExecuteScalarAsync<string?>("""
            SELECT l.variant_label FROM ordering.order_lines l JOIN ordering.order_packages p ON p.id = l.package_id JOIN ordering.orders o ON o.id = l.order_id
            WHERE l.product_id = @ProductId AND o.customer_id = @CustomerId AND p.status = 'delivered'
            ORDER BY p.delivered_at DESC LIMIT 1
            """, new PurchaseKeyRecord(productId, customerId));

    public static async Task<List<DeliveredSellerRecord>> DeliveredSellersAsync(NpgsqlConnection conn, NpgsqlTransaction? tx,
        IReadOnlyCollection<(long CustomerId, long ProductId)> purchases)
    {
        if (purchases.Count == 0) return [];
        return (await conn.QueryAsync<DeliveredSellerRecord>("""
            SELECT DISTINCT ON (q.customer_id, q.product_id) q.customer_id, q.product_id, pk.seller_id
            FROM unnest(@CustomerIds, @ProductIds) AS q(customer_id, product_id)
            JOIN ordering.orders o ON o.customer_id = q.customer_id
            JOIN ordering.order_lines l ON l.order_id = o.id AND l.product_id = q.product_id
            JOIN ordering.order_packages pk ON pk.id = l.package_id AND pk.status = 'delivered'
            ORDER BY q.customer_id, q.product_id, pk.delivered_at DESC NULLS LAST, pk.id
            """, new PurchasePairsRecord(purchases.Select(p => p.CustomerId).ToArray(), purchases.Select(p => p.ProductId).ToArray()), tx)).AsList();
    }

    public static Task<PaidTotalRecord> PaidTotalAsync(NpgsqlConnection conn, Guid[] orderIds) =>
        conn.QuerySingleAsync<PaidTotalRecord>("""
            SELECT count(*)::int AS count, coalesce(sum(total), 0) AS total FROM ordering.orders WHERE id = ANY(@OrderIds) AND status = 'paid'
            """, new OrderIdsRecord(orderIds));
}
