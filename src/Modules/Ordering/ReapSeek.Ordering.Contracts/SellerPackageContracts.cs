namespace ReapSeek.Ordering.Contracts;

public sealed record PackageAddress(string FullName, string Phone, string City, string District, string Line, string PostalCode);
public sealed record PackageLineRow(string ProductTitle, string VariantLabel, string Sku, decimal UnitPrice, int Quantity);
public sealed record SellerPackageRow(
    Guid Id, string OrderNumber, string Status, DateTime PlacedAt, DateTime? PaidAt, decimal Subtotal, decimal ShippingFee, decimal CommissionRate,
    string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt, PackageAddress Address, List<PackageLineRow> Lines);
public sealed record SellerPackagesResponse(List<SellerPackageRow> Packages, long Total, int Page, int PageSize);

public interface ISellerPackages
{
    Task<(List<SellerPackageRow> Rows, long Total)> ListAsync(long sellerId, string? status, int page, int pageSize, CancellationToken ct);
    Task<SellerPackageRow?> GetAsync(long? sellerId, Guid packageId, CancellationToken ct);
    Task<SellerPackageRow> ShipAsync(long sellerId, Guid packageId, CancellationToken ct);
    Task<decimal> InTransitAsync(long sellerId, CancellationToken ct);
    Task<int> DeliveredCountAsync(long sellerId, CancellationToken ct);
}
