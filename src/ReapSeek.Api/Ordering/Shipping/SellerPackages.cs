using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Modules.Ordering;
using ReapSeek.Shared;
using ReapSeek.Shared.Providers;

namespace ReapSeek.Api.Ordering.Shipping;

public sealed record PackageAddress(string FullName, string Phone, string City, string District, string Line, string PostalCode);
public sealed record PackageLineRow(string ProductTitle, string VariantLabel, string Sku, decimal UnitPrice, int Quantity);
public sealed record SellerPackageRow(
    Guid Id, string OrderNumber, string Status, DateTime PlacedAt, DateTime? PaidAt, decimal Subtotal, decimal ShippingFee, decimal CommissionRate,
    string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt, PackageAddress Address, List<PackageLineRow> Lines);
public sealed record SellerPackagesResponse(List<SellerPackageRow> Packages, long Total, int Page, int PageSize);

public static class PackageFilter
{
    public static readonly string[] Statuses = ["preparing", "shipped", "delivered"];

    public static string? Status(string? value) =>
        value?.Trim() is { Length: > 0 } s ? Ensure.OneOf(s, "status", Statuses) : null;
}

public sealed class SellerPackagesRepository(NpgsqlDataSource db)
{
    public sealed record PackageRecord(
        Guid Id, string OrderNumber, string Status, DateTime PlacedAt, DateTime? PaidAt, decimal Subtotal, decimal ShippingFee, decimal CommissionRate,
        string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt,
        string FullName, string Phone, string City, string District, string Line, string PostalCode);
    public sealed record PackageLineRecord(Guid PackageId, string ProductTitle, string VariantLabel, string Sku, decimal UnitPrice, int Quantity);
    const string PackageSelect = """
        SELECT p.id, o.number AS order_number, p.status, o.placed_at, o.paid_at,
            (SELECT coalesce(sum(l.unit_price * l.quantity), 0) FROM order_lines l WHERE l.package_id = p.id) AS subtotal,
            p.shipping_fee, p.commission_rate, p.tracking_number, p.shipped_at, p.delivered_at,
            o.shipping_address->>'full_name' AS full_name, o.shipping_address->>'phone' AS phone, o.shipping_address->>'city' AS city,
            o.shipping_address->>'district' AS district, o.shipping_address->>'line' AS line, o.shipping_address->>'postal_code' AS postal_code
        FROM ordering.order_packages p JOIN ordering.orders o ON o.id = p.order_id
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

    public async Task<decimal> InTransitAsync(long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new { SellerId = sellerId };
        return await conn.ExecuteScalarAsync<decimal>("""
            SELECT coalesce(sum(t.subtotal + p.shipping_fee - round(t.subtotal * p.commission_rate, 2)), 0)
            FROM ordering.order_packages p
            CROSS JOIN LATERAL (SELECT coalesce(sum(l.unit_price * l.quantity), 0) AS subtotal FROM ordering.order_lines l WHERE l.package_id = p.id) t
            WHERE p.seller_id = @SellerId AND p.status IN ('preparing', 'shipped')
            """, args);
    }

    public async Task<int> DeliveredCountAsync(long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM ordering.order_packages WHERE seller_id = @SellerId AND status = 'delivered'",
            new { SellerId = sellerId });
    }
}

public sealed class SellerPackages(NpgsqlDataSource db, SellerPackagesRepository packages, ICarrier carrier, TimeProvider clock)
{
    public Task<(List<SellerPackageRow> Rows, long Total)> ListAsync(long sellerId, string? status, int page, int pageSize, CancellationToken ct) =>
        packages.PackagesAsync(sellerId, status, page, pageSize, ct);

    public Task<SellerPackageRow?> GetAsync(long? sellerId, Guid packageId, CancellationToken ct) => packages.PackageAsync(sellerId, packageId, ct);

    public Task<decimal> InTransitAsync(long sellerId, CancellationToken ct) => packages.InTransitAsync(sellerId, ct);

    public Task<int> DeliveredCountAsync(long sellerId, CancellationToken ct) => packages.DeliveredCountAsync(sellerId, ct);

    public async Task<SellerPackageRow> ShipAsync(long sellerId, Guid packageId, CancellationToken ct)
    {
        var orderId = await packages.OrderOfPackageAsync(packageId, sellerId, ct) ?? throw new NotFoundException("Package not found");
        var shipment = await carrier.CreateShipmentAsync(packageId.ToString(), ShipmentKinds.Outbound, ct);
        await using (var conn = await db.OpenConnectionAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var order = await OrderStore.LoadAsync(conn, tx, orderId) ?? throw new NotFoundException("Package not found");
            order.ShipPackage(packageId, sellerId, shipment.TrackingNumber, clock.GetUtcNow().UtcDateTime);
            await OrderStore.SaveAsync(conn, tx, order);
            await tx.CommitAsync(ct);
        }
        return (await packages.PackageAsync(sellerId, packageId, ct))!;
    }
}
