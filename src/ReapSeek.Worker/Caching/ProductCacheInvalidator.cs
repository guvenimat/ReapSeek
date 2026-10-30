using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Caching;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Caching;

public sealed class ProductCacheInvalidator(NpgsqlDataSource db, RedisCache cache, ILogger<ProductCacheInvalidator> log)
{
    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> messages, CancellationToken ct)
    {
        if (messages.Any(m => m.Type == OutboxTypes.CatalogReindex))
        {
            await cache.RemoveByPrefixAsync(CacheKeys.ProductPrefix);
            await cache.RemoveAsync(CacheKeys.Categories);
            return;
        }
        var ids = messages
            .SelectMany(m => JsonSerializer.Deserialize(m.Payload, OutboxJsonContext.Default.ProductsChanged)?.ProductIds ?? [])
            .Distinct().ToArray();
        if (ids.Length == 0) return;
        await using var conn = await db.OpenConnectionAsync(ct);
        var slugs = (await conn.QueryAsync<string>("SELECT slug FROM products WHERE id = ANY(@Ids)", new { Ids = ids })).ToArray();
        if (await cache.RemoveAsync(slugs.Select(CacheKeys.Product).ToArray()))
            log.LogDebug("Invalidated {Count} cached product page(s)", slugs.Length);
    }
}
