namespace ReapSeek.Catalog.Contracts;

public sealed record SellerOfferRow(
    long Id, long ProductId, string ProductTitle, string ProductSlug, string ProductStatus, long VariantId, string Sku, string VariantLabel,
    decimal Price, decimal? ListPrice, int Stock, int HandlingDays, string Status, decimal? BuyBoxPrice, bool IsBuyBox, DateTime UpdatedAt);
public sealed record SellerOffersResponse(List<SellerOfferRow> Offers, long Total, int Page, int PageSize);

public sealed record VariantBuyBox(long VariantId, decimal? Price, bool Mine);
public sealed record SellerProductView(ProductDetail Product, List<VariantBuyBox> BuyBox, bool Editable);

public sealed record ProposalRow(long Id, string Slug, string Title, string Brand, string CategoryName, string Status, string? ReviewNote, DateTime CreatedAt);

public sealed record SellerStorePage(List<ProductCard> Products, int Total, decimal? AverageHandlingDays);

public interface ISellerCatalog
{
    Task<SellerOffersResponse> OffersAsync(long sellerId, string? q, int page, int pageSize, CancellationToken ct);
    Task<SellerProductView> UpsertOfferAsync(long sellerId, OfferRequest req, CancellationToken ct);
    Task<SellerProductView> ProductAsync(long sellerId, long productId, CancellationToken ct);
    Task<SellerProductView> ProposeAsync(long sellerId, CreateProductRequest req, CancellationToken ct);
    Task<List<ProposalRow>> ProposalsAsync(long sellerId, CancellationToken ct);
    Task<ProductDetail> ReviewProposalAsync(long productId, string? decision, string? note, CancellationToken ct);
    Task<SellerStorePage> StoreProductsAsync(long sellerId, int page, int pageSize, CancellationToken ct);
}
