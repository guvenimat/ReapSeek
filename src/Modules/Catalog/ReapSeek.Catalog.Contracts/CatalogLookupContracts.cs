namespace ReapSeek.Catalog.Contracts;

public sealed record ProductInfo(long Id, string Title, string Slug);

public sealed record OfferView(
    long OfferId, decimal Price, decimal? ListPrice, int Stock, string OfferStatus, int HandlingDays,
    long SellerId, string SellerName, string SellerStatus, decimal SellerRating,
    long ProductId, string ProductSlug, string ProductTitle, string ProductStatus, string Brand,
    long VariantId, string Sku, string Options, string? ImageUrl);

public sealed record SellerSnapshotRecord(long Id, string Name, string Slug, string City, string Status, decimal Rating);
public sealed record SellerOfferCountRecord(long SellerId, long ProductCount, long OfferCount);
