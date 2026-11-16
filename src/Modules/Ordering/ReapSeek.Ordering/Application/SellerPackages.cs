using Npgsql;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Modules.Ordering;
using ReapSeek.Ordering.Contracts;
using ReapSeek.Ordering.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Providers;

namespace ReapSeek.Ordering.Application;

internal static class PackageFilter
{
    public static readonly string[] Statuses = ["preparing", "shipped", "delivered"];

    public static string? Status(string? value) =>
        value?.Trim() is { Length: > 0 } s ? Ensure.OneOf(s, "status", Statuses) : null;
}

internal sealed class SellerPackages(NpgsqlDataSource db, SellerPackagesRepository packages, ICarrier carrier, TimeProvider clock) : ISellerPackages
{
    public Task<(List<SellerPackageRow> Rows, long Total)> ListAsync(long sellerId, string? status, int page, int pageSize, CancellationToken ct) =>
        packages.PackagesAsync(sellerId, PackageFilter.Status(status), page, pageSize, ct);

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
