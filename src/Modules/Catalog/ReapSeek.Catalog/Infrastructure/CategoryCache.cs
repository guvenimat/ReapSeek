using Dapper;
using Npgsql;
using ReapSeek.Catalog.Api;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Catalog.Infrastructure;

internal sealed class CategoryCache(NpgsqlDataSource db, RedisCache cache)
{
    internal sealed record CategoryRecord(long Id, long? ParentId, string Slug, string Name, string Path, int Depth, int Position, string ImageKind);

    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public Task InvalidateAsync() => cache.RemoveAsync(CacheKeys.Categories);

    public async Task<(List<CategoryRecord> Rows, Dictionary<string, string> Names)> GetAsync(CancellationToken ct)
    {
        var rows = await cache.GetOrLoadAsync(CacheKeys.Categories, CatalogCacheJsonContext.Default.ListCategoryRecord, Ttl, async () =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return (await conn.QueryAsync<CategoryRecord>(
                "SELECT id, parent_id, slug, name, path, depth, position, image_kind FROM categories ORDER BY depth, position, name")).AsList();
        }) ?? [];
        return (rows, rows.ToDictionary(r => r.Path, r => r.Name, StringComparer.Ordinal));
    }

    public async Task<CategoryTreeResponse> TreeAsync(CancellationToken ct)
    {
        var (rows, _) = await GetAsync(ct);
        List<CategoryNode> Children(long? parent) => rows.Where(r => r.ParentId == parent)
            .Select(r => new CategoryNode(r.Id, r.Slug, r.Name, r.Path, r.Depth, r.ImageKind, Children(r.Id))).ToList();
        return new CategoryTreeResponse(Children(null));
    }
}
