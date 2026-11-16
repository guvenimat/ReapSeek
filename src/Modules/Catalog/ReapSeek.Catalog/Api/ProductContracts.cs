namespace ReapSeek.Catalog.Api;

internal sealed record CategoryCrumb(string Name, string Slug, string Path);
internal sealed record StoreImage(string Url, string Alt, string? Color);
internal sealed record VariantAxis(string Name, List<string> Values);
internal sealed record StoreOffer(
    long Id, long SellerId, string SellerName, string SellerSlug, decimal SellerRating, string SellerCity,
    decimal Price, decimal? ListPrice, int Discount, int? LowStock, int HandlingDays, bool IsBuyBox);
internal sealed record StoreVariant(long Id, string Sku, Dictionary<string, string> Options, List<StoreOffer> Offers);
internal sealed record StoreProduct(
    long Id, string Slug, string Title, string Brand, string BrandSlug, string Description, Dictionary<string, string> Attributes,
    List<CategoryCrumb> Categories, List<StoreImage> Images, List<VariantAxis> Axes, List<StoreVariant> Variants,
    decimal? MinPrice, decimal? MaxPrice, int SellerCount, DateTime UpdatedAt);

internal sealed record SitemapProduct(string Slug, DateTime UpdatedAt);
internal sealed record SitemapResponse(List<SitemapProduct> Products, List<string> Categories);
