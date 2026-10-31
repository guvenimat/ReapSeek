using Npgsql;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Payments;
using ReapSeek.Domain.Ledger;
using ReapSeek.Shared;
using ReapSeek.Shared.Ordering;

namespace ReapSeek.Api.SellerPanel;

public sealed class SellerPanelService(
    NpgsqlDataSource db, SellerPanelRepository panel, CatalogService catalog, SandboxClient sandbox, IConfiguration cfg, TimeProvider clock)
{
    readonly TimeSpan _hold = TimeSpan.FromDays(cfg.GetValue("Ledger:HoldDays", Settlement.DefaultHold.TotalDays));

    public async Task<SellerProductView> ProductAsync(long sellerId, long productId, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(productId, ct);
        var submitter = await panel.SubmitterAsync(productId, ct);
        var own = submitter == sellerId;
        if (product.Status != ProductStatus.Active && !own) throw new NotFoundException("Product not found");
        var buyBox = product.Status == ProductStatus.Active ? await panel.BuyBoxAsync(productId, ct) : [];
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
        var detail = await catalog.UpsertOfferAsync(req with { SellerId = sellerId }, ct, product =>
            product.Status == ProductStatus.Active || product.SubmittedBySellerId == sellerId);
        return await ProductAsync(sellerId, detail.Id, ct);
    }

    public async Task<SellerProductView> ProposeAsync(long sellerId, CreateProductRequest req, CancellationToken ct)
    {
        var product = await catalog.CreateProductAsync(req, ct);
        await panel.MarkSubmittedAsync(product.Id, sellerId, ct);
        return await ProductAsync(sellerId, product.Id, ct);
    }

    public async Task<ProductDetail> ReviewAsync(long productId, ReviewRequest req, CancellationToken ct)
    {
        var decision = Ensure.OneOf(req.Decision ?? "", "decision", ReviewDecision.All);
        var note = req.Note?.Trim() is { Length: > 0 } n ? n : null;
        Ensure.That(note is null || note.Length <= 500, "note can be at most 500 characters");
        Ensure.That(decision == ReviewDecision.Approve || note is not null, "A note is required when rejecting a product");
        var product = await catalog.GetProductAsync(productId, ct);
        Ensure.That(product.Status == ProductStatus.PendingReview, "Only a product that is pending review can be reviewed");
        var status = decision == ReviewDecision.Approve ? ProductStatus.Active : ProductStatus.Rejected;
        await catalog.UpdateProductAsync(productId, new UpdateProductRequest(null, null, null, null, null, status), ct);
        await panel.SetReviewNoteAsync(productId, note, ct);
        return await catalog.GetProductAsync(productId, ct);
    }

    public async Task<SellerPackageRow> ShipAsync(long sellerId, Guid packageId, CancellationToken ct)
    {
        var orderId = await panel.OrderOfPackageAsync(packageId, sellerId, ct) ?? throw new NotFoundException("Package not found");
        var shipment = await sandbox.CreateShipmentAsync(packageId.ToString(), ct);
        await using (var conn = await db.OpenConnectionAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var order = await OrderStore.LoadAsync(conn, tx, orderId) ?? throw new NotFoundException("Package not found");
            order.ShipPackage(packageId, sellerId, shipment.TrackingNumber, clock.GetUtcNow().UtcDateTime);
            await OrderStore.SaveAsync(conn, tx, order);
            await tx.CommitAsync(ct);
        }
        return (await panel.PackageAsync(sellerId, packageId, ct))!;
    }

    public async Task<SellerPackageRow> DeliverAsync(Guid packageId, CancellationToken ct)
    {
        var orderId = await panel.OrderOfPackageAsync(packageId, null, ct) ?? throw new NotFoundException("Package not found");
        await using (var conn = await db.OpenConnectionAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var order = await OrderStore.LoadAsync(conn, tx, orderId) ?? throw new NotFoundException("Package not found");
            var package = order.DeliverPackage(packageId, clock.GetUtcNow().UtcDateTime);
            var entries = Settlement.ForPackage(package, await SellerPanelRepository.CommissionRateAsync(conn, tx, packageId));
            await OrderStore.SaveAsync(conn, tx, order);
            await SellerPanelRepository.InsertLedgerAsync(conn, tx, new SellerPanelRepository.LedgerInsertRecord(package.SellerId, package.Id, order.Number,
                entries.Select(e => e.Kind).ToArray(), entries.Select(e => e.Amount).ToArray(), Settlement.AvailableAt(package.DeliveredAt!.Value, _hold)));
            await tx.CommitAsync(ct);
        }
        return (await panel.PackageAsync(null, packageId, ct))!;
    }
}
