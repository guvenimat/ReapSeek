using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Ordering;

public sealed record PurchaseKeyRecord(long ProductId, long CustomerId);

public static class OrderLookups
{
    public static Task<string?> DeliveredVariantAsync(NpgsqlConnection conn, long productId, long customerId) =>
        conn.ExecuteScalarAsync<string?>("""
            SELECT l.variant_label FROM ordering.order_lines l JOIN ordering.order_packages p ON p.id = l.package_id JOIN ordering.orders o ON o.id = l.order_id
            WHERE l.product_id = @ProductId AND o.customer_id = @CustomerId AND p.status = 'delivered'
            ORDER BY p.delivered_at DESC LIMIT 1
            """, new PurchaseKeyRecord(productId, customerId));
}
