using Dapper;
using Npgsql;
using ReapSeek.Domain.Ledger;

namespace ReapSeek.Api.Sellers.SellerPanel;

public sealed class SellerPanelRepository(NpgsqlDataSource db)
{
    public sealed record PackageRecord(
        Guid Id, string OrderNumber, string Status, DateTime PlacedAt, DateTime? PaidAt, decimal Subtotal, decimal ShippingFee, decimal CommissionRate,
        string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt,
        string FullName, string Phone, string City, string District, string Line, string PostalCode);
    public sealed record PackageLineRecord(Guid PackageId, string ProductTitle, string VariantLabel, string Sku, decimal UnitPrice, int Quantity);
    public sealed record BalanceRecord(decimal Pending, decimal Available, decimal PaidOut);

    public async Task<SellerAccount?> AccountAsync(long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerAccount>(
            "SELECT id, name, slug, city, status, commission_rate, rating FROM sellers.sellers WHERE id = @Id", new { Id = sellerId });
    }

    const string PackageSelect = """
        SELECT p.id, o.number AS order_number, p.status, o.placed_at, o.paid_at,
            (SELECT coalesce(sum(l.unit_price * l.quantity), 0) FROM order_lines l WHERE l.package_id = p.id) AS subtotal,
            p.shipping_fee, p.commission_rate, p.tracking_number, p.shipped_at, p.delivered_at,
            o.shipping_address->>'full_name' AS full_name, o.shipping_address->>'phone' AS phone, o.shipping_address->>'city' AS city,
            o.shipping_address->>'district' AS district, o.shipping_address->>'line' AS line, o.shipping_address->>'postal_code' AS postal_code
        FROM order_packages p JOIN orders o ON o.id = p.order_id
        """;

    public async Task<(List<SellerPackageRow> Rows, long Total)> PackagesAsync(long sellerId, string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new { SellerId = sellerId, Status = status, Statuses = PackageFilter.Statuses, Limit = pageSize, Offset = (page - 1) * pageSize };
        const string Where = " WHERE p.seller_id = @SellerId AND p.status = ANY(@Statuses) AND (@Status::text IS NULL OR p.status = @Status)";
        var total = await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM order_packages p" + Where, args);
        var packages = (await conn.QueryAsync<PackageRecord>(PackageSelect + Where +
            " ORDER BY (p.status = 'preparing') DESC, o.paid_at DESC NULLS LAST, p.id LIMIT @Limit OFFSET @Offset", args)).AsList();
        return (await WithLinesAsync(conn, packages), total);
    }

    public async Task<SellerPackageRow?> PackageAsync(long? sellerId, Guid packageId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var packages = (await conn.QueryAsync<PackageRecord>(PackageSelect + " WHERE p.id = @Id AND (@SellerId::bigint IS NULL OR p.seller_id = @SellerId)",
            new { Id = packageId, SellerId = sellerId })).AsList();
        return (await WithLinesAsync(conn, packages)).FirstOrDefault();
    }

    static async Task<List<SellerPackageRow>> WithLinesAsync(NpgsqlConnection conn, List<PackageRecord> packages)
    {
        if (packages.Count == 0) return [];
        var lines = (await conn.QueryAsync<PackageLineRecord>("""
            SELECT package_id, product_title, variant_label, sku, unit_price, quantity FROM order_lines
            WHERE package_id = ANY(@Ids) ORDER BY product_title, id
            """, new { Ids = packages.Select(p => p.Id).ToArray() })).ToLookup(l => l.PackageId);
        return packages.Select(p => new SellerPackageRow(p.Id, p.OrderNumber, p.Status, p.PlacedAt, p.PaidAt, p.Subtotal, p.ShippingFee, p.CommissionRate,
            p.TrackingNumber, p.ShippedAt, p.DeliveredAt, new PackageAddress(p.FullName, p.Phone, p.City, p.District, p.Line, p.PostalCode),
            lines[p.Id].Select(l => new PackageLineRow(l.ProductTitle, l.VariantLabel, l.Sku, l.UnitPrice, l.Quantity)).ToList())).ToList();
    }

    public async Task<Guid?> OrderOfPackageAsync(Guid packageId, long? sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>("SELECT order_id FROM order_packages WHERE id = @Id AND (@SellerId::bigint IS NULL OR seller_id = @SellerId)",
            new { Id = packageId, SellerId = sellerId });
    }

    public async Task<SellerBalanceResponse> BalanceAsync(long sellerId, DateTime nowUtc, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new { SellerId = sellerId, Now = nowUtc };
        var balance = await conn.QuerySingleAsync<BalanceRecord>("""
            SELECT coalesce(sum(amount) FILTER (WHERE payout_id IS NULL AND available_at > @Now), 0) AS pending,
                coalesce(sum(amount) FILTER (WHERE payout_id IS NULL AND available_at <= @Now), 0) AS available,
                coalesce(sum(amount) FILTER (WHERE payout_id IS NOT NULL), 0) AS paid_out
            FROM seller_ledger WHERE seller_id = @SellerId
            """, args);
        var inTransit = await conn.ExecuteScalarAsync<decimal>("""
            SELECT coalesce(sum(t.subtotal + p.shipping_fee - round(t.subtotal * p.commission_rate, 2)), 0)
            FROM order_packages p
            CROSS JOIN LATERAL (SELECT coalesce(sum(l.unit_price * l.quantity), 0) AS subtotal FROM order_lines l WHERE l.package_id = p.id) t
            WHERE p.seller_id = @SellerId AND p.status IN ('preparing', 'shipped')
            """, args);
        var entries = (await conn.QueryAsync<LedgerRow>("""
            SELECT id, order_number, kind, amount, available_at, payout_id, created_at FROM seller_ledger
            WHERE seller_id = @SellerId ORDER BY created_at DESC, id DESC LIMIT 100
            """, args)).AsList();
        var payouts = await PayoutsAsync(conn, sellerId);
        var next = PayoutPeriod.CurrentPeriodEnd(nowUtc, PayoutPeriod.Turkey).AddDays(7);
        return new SellerBalanceResponse(new BalanceSummary(inTransit, balance.Pending, balance.Available, balance.PaidOut, next), entries, payouts);
    }

    public async Task<List<PayoutRow>> PayoutsAsync(long? sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await PayoutsAsync(conn, sellerId);
    }

    static async Task<List<PayoutRow>> PayoutsAsync(NpgsqlConnection conn, long? sellerId) =>
        (await conn.QueryAsync<PayoutRow>("""
            SELECT p.id, p.seller_id, s.name AS seller_name, p.period_end, p.amount, p.entry_count, p.transfer_reference, p.created_at
            FROM payouts p JOIN sellers s ON s.id = p.seller_id
            WHERE @SellerId::bigint IS NULL OR p.seller_id = @SellerId
            ORDER BY p.period_end DESC, p.id DESC LIMIT 100
            """, new { SellerId = sellerId })).AsList();
}
