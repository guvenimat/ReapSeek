using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Caching;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Search;

public sealed class CategoryCache(NpgsqlDataSource db, RedisCache cache)
{
    public sealed record CategoryRecord(long Id, long? ParentId, string Slug, string Name, string Path, int Depth, int Position, string ImageKind);

    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public Task InvalidateAsync() => cache.RemoveAsync(CacheKeys.Categories);

    public async Task<(List<CategoryRecord> Rows, Dictionary<string, string> Names)> GetAsync(CancellationToken ct)
    {
        var rows = await cache.GetOrLoadAsync(CacheKeys.Categories, CacheJsonContext.Default.ListCategoryRecord, Ttl, async () =>
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

public sealed class SearchService(OpenSearchClient search, CategoryCache categories)
{
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var (_, names) = await categories.GetAsync(ct);
        var result = await search.PostAsync($"{ProductIndex.Alias}/_search", SearchQuery.Build(request), ct)
            ?? throw new OpenSearchException(502, "Empty search response");
        return SearchQuery.ParseResults(result, request, names);
    }

    public async Task<SuggestResponse> SuggestAsync(string q, CancellationToken ct)
    {
        var (_, names) = await categories.GetAsync(ct);
        var result = await search.PostAsync($"{ProductIndex.Alias}/_search", SearchQuery.BuildSuggest(q), ct)
            ?? throw new OpenSearchException(502, "Empty search response");
        return SearchQuery.ParseSuggest(result, q, names);
    }
}
