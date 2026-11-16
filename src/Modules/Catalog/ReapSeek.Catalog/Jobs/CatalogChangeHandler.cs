using ReapSeek.Shared.Outbox;

namespace ReapSeek.Catalog.Jobs;

internal sealed class CatalogChangeHandler(ProductCacheInvalidator productCache, SearchIndexer searchIndexer) : ICatalogChangeHandler
{
    public async Task HandleAsync(IReadOnlyList<OutboxMessageRecord> batch, CancellationToken ct)
    {
        await productCache.HandleAsync(batch, ct);
        await searchIndexer.HandleAsync(batch, ct);
    }
}
