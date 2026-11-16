using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Api;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Catalog.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Catalog.Application;

internal static class ReviewDecision
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public static readonly string[] All = [Approve, Reject];
}

internal sealed class SellerCatalog(SellerCatalogRepository repository, CatalogService catalog, SearchService search) : ISellerCatalog
{
    public async Task<SellerOffersResponse> OffersAsync(long sellerId, string? q, int page, int pageSize, CancellationToken ct)
    {
        var (rows, total) = await repository.OffersAsync(sellerId, q, page, pageSize, ct);
        return new SellerOffersResponse(rows, total, page, pageSize);
    }

    public async Task<SellerProductView> ProductAsync(long sellerId, long productId, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(productId, ct);
        var submitter = await repository.SubmitterAsync(productId, ct);
        var own = submitter == sellerId;
        if (product.Status != ProductStatus.Active && !own) throw new NotFoundException("Product not found");
        var buyBox = product.Status == ProductStatus.Active ? await repository.BuyBoxAsync(productId, ct) : [];
        var mine = product.Variants.SelectMany(v => v.Offers).Where(o => o.SellerId == sellerId).Select(o => o.Id).ToHashSet();
        var visible = product with
        {
            Variants = product.Variants.Select(v => v with { Offers = v.Offers.Where(o => o.SellerId == sellerId).ToList() }).ToList(),
        };
        return new SellerProductView(visible, buyBox.Select(b => new VariantBuyBox(b.VariantId, b.Price, b.OfferId is { } id && mine.Contains(id))).ToList(),
            own && product.Status is ProductStatus.PendingReview or ProductStatus.Rejected);
    }

    public async Task<SellerProductView> UpsertOfferAsync(long sellerId, OfferRequest req, CancellationToken ct)
    {
        Ensure.That(req.VariantId is > 0, "variant_id is required");
        var detail = await catalog.UpsertOfferAsync(req with { SellerId = sellerId }, ct, sellerId);
        return await ProductAsync(sellerId, detail.Id, ct);
    }

    public async Task<SellerProductView> ProposeAsync(long sellerId, CreateProductRequest req, CancellationToken ct)
    {
        var product = await catalog.CreateProductAsync(req, ct);
        await repository.MarkSubmittedAsync(product.Id, sellerId, ct);
        return await ProductAsync(sellerId, product.Id, ct);
    }

    public Task<List<ProposalRow>> ProposalsAsync(long sellerId, CancellationToken ct) => repository.ProposalsAsync(sellerId, ct);

    public async Task<ProductDetail> ReviewProposalAsync(long productId, string? decision, string? note, CancellationToken ct)
    {
        var verdict = Ensure.OneOf(decision ?? "", "decision", ReviewDecision.All);
        var text = note?.Trim() is { Length: > 0 } n ? n : null;
        Ensure.That(text is null || text.Length <= 500, "note can be at most 500 characters");
        Ensure.That(verdict == ReviewDecision.Approve || text is not null, "A note is required when rejecting a product");
        var product = await catalog.GetProductAsync(productId, ct);
        Ensure.That(product.Status == ProductStatus.PendingReview, "Only a product that is pending review can be reviewed");
        var status = verdict == ReviewDecision.Approve ? ProductStatus.Active : ProductStatus.Rejected;
        await catalog.UpdateProductAsync(productId, new UpdateProductRequest(null, null, null, null, null, status), ct);
        await repository.SetReviewNoteAsync(productId, text, ct);
        return await catalog.GetProductAsync(productId, ct);
    }

    public async Task<SellerStorePage> StoreProductsAsync(long sellerId, int page, int pageSize, CancellationToken ct)
    {
        var (ids, total, handling) = await repository.ProductsAsync(sellerId, page, pageSize, ct);
        return new SellerStorePage(await search.InStockCardsAsync(ids, ct), total, handling);
    }
}
