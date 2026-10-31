namespace ReapSeek.Api.Catalog;

public sealed record CategoryRow(long Id, long? ParentId, string Slug, string Name, string Path, int Depth, int Position, string ImageKind, long ProductCount);
public sealed record CategoriesResponse(List<CategoryRow> Categories);
public sealed record CreateCategoryRequest(long? ParentId, string? Name, string? Slug, string? ImageKind);

public sealed record BrandRow(long Id, string Name, string Slug, long ProductCount);
public sealed record BrandsResponse(List<BrandRow> Brands);
public sealed record CreateBrandRequest(string? Name);

public sealed record ProductListItem(
    long Id, string Slug, string Title, string Brand, string CategoryName, string CategoryPath, string Status,
    int VariantCount, int SellerCount, decimal? Price, decimal? ListPrice, string? ImageUrl, DateTime UpdatedAt);
public sealed record ProductsResponse(List<ProductListItem> Products, long Total, int Page, int PageSize);

public sealed record ProductImage(int Position, string Url, string Alt, string? Color);
public sealed record OfferRow(
    long Id, long SellerId, string SellerName, string SellerStatus, decimal SellerRating, long VariantId,
    decimal Price, decimal? ListPrice, int Stock, int HandlingDays, string Status, bool IsBuyBox, DateTime UpdatedAt);
public sealed record VariantRow(long Id, string Sku, Dictionary<string, string> Options, int Position, List<OfferRow> Offers);
public sealed record ProductDetail(
    long Id, string Slug, string Title, long BrandId, string Brand, long CategoryId, string CategoryName, string CategoryPath,
    string Description, Dictionary<string, string> Attributes, string Status, DateTime CreatedAt, DateTime UpdatedAt,
    List<ProductImage> Images, List<VariantRow> Variants);

public sealed record CreateProductRequest(
    string? Title, long? BrandId, long? CategoryId, string? Description,
    Dictionary<string, string>? Attributes, Dictionary<string, List<string>>? VariantAxes);
public sealed record UpdateProductRequest(
    string? Title, long? BrandId, long? CategoryId, string? Description, Dictionary<string, string>? Attributes, string? Status);

public sealed record VariantProductRecord(long Id, string Status, long? SubmittedBySellerId);
public sealed record OfferRequest(long? SellerId, long? VariantId, decimal? Price, decimal? ListPrice, int? Stock, int? HandlingDays, string? Status);

public sealed record CatalogStats(
    long ActiveProducts, long DraftProducts, long ArchivedProducts, long ActiveSellers, long SuspendedSellers,
    long Offers, long OutOfStockProducts, long Brands, long Categories);

public static class ProductStatus
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Archived = "archived";
    public const string PendingReview = "pending_review";
    public const string Rejected = "rejected";
    public static readonly string[] All = [Draft, PendingReview, Active, Rejected, Archived];
}

public static class OfferStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
    public static readonly string[] All = [Active, Paused];
}
