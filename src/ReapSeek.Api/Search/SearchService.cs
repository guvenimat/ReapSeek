using Dapper;
using Npgsql;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Search;

public sealed class CategoryCache(NpgsqlDataSource db)
{
    public sealed record CategoryRecord(long Id, long? ParentId, string Slug, string Name, string Path, int Depth, int Position, string ImageKind);

    static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    readonly SemaphoreSlim _lock = new(1, 1);
    (List<CategoryRecord> Rows, Dictionary<string, string> Names, DateTime LoadedAt)? _cache;

    public void Invalidate() => _cache = null;

    public async Task<(List<CategoryRecord> Rows, Dictionary<string, string> Names)> GetAsync(CancellationToken ct)
    {
        if (_cache is { } hit && DateTime.UtcNow - hit.LoadedAt < Ttl) return (hit.Rows, hit.Names);
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } again && DateTime.UtcNow - again.LoadedAt < Ttl) return (again.Rows, again.Names);
            await using var conn = await db.OpenConnectionAsync(ct);
            var rows = (await conn.QueryAsync<CategoryRecord>(
                "SELECT id, parent_id, slug, name, path, depth, position, image_kind FROM categories ORDER BY depth, position, name")).AsList();
            var names = rows.ToDictionary(r => r.Path, r => r.Name, StringComparer.Ordinal);
            _cache = (rows, names, DateTime.UtcNow);
            return (rows, names);
        }
        finally
        {
            _lock.Release();
        }
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
