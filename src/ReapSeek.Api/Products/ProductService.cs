using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Api.Products;

public sealed class ProductService(ProductRepository products, RedisCache cache)
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public Task<StoreProduct?> FindAsync(string slug, CancellationToken ct) =>
        cache.GetOrLoadAsync(CacheKeys.Product(slug), CacheJsonContext.Default.StoreProduct, CacheTtl, () => products.FindAsync(slug, ct));
}
