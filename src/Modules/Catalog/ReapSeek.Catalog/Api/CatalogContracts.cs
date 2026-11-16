namespace ReapSeek.Catalog.Api;

internal sealed record CreateCategoryRequest(long? ParentId, string? Name, string? Slug, string? ImageKind);

internal sealed record CreateBrandRequest(string? Name);

internal sealed record UpdateProductRequest(
    string? Title, long? BrandId, long? CategoryId, string? Description, Dictionary<string, string>? Attributes, string? Status);

internal sealed record CatalogStats(
    long ActiveProducts, long DraftProducts, long ArchivedProducts, long ActiveSellers, long SuspendedSellers,
    long Offers, long OutOfStockProducts, long Brands, long Categories);
