using ReapSeek.Catalog.Api;
using ReapSeek.Catalog.Infrastructure;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Catalog.Application;

internal sealed class ProductService(ProductRepository products, RedisCache cache)
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public Task<StoreProduct?> FindAsync(string slug, CancellationToken ct) =>
        cache.GetOrLoadAsync(CacheKeys.Product(slug), CatalogCacheJsonContext.Default.StoreProduct, CacheTtl, () => products.FindAsync(slug, ct));
}
