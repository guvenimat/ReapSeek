using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReapSeek.Shared.Outbox;
using ReapSeek.Shared.Search;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Search;

public sealed class SearchIndexer(OpenSearchClient search, SearchRepository products, ILogger<SearchIndexer> log)
{
    public const int ChunkSize = 1000;
    readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConfigured => search.IsConfigured;

    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> messages, CancellationToken ct)
    {
        if (!search.IsConfigured) return;
        if (messages.Any(m => m.Type == OutboxTypes.CatalogReindex))
        {
            await ReindexAsync(ct);
            return;
        }
        var ids = messages
            .SelectMany(m => JsonSerializer.Deserialize(m.Payload, OutboxJsonContext.Default.ProductsChanged)?.ProductIds ?? [])
            .Distinct().ToList();
        await _lock.WaitAsync(ct);
        try
        {
            if (!await search.ExistsAsync(ProductIndex.Alias, ct))
            {
                await ReindexLockedAsync(ct);
                return;
            }
            foreach (var chunk in ids.Chunk(ChunkSize)) await IndexChunkAsync(ProductIndex.Alias, chunk, deleteMissing: true, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnsureIndexAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!await search.ExistsAsync(ProductIndex.Alias, ct) || await CurrentVersionAsync(ct) != ProductIndex.Version) await ReindexLockedAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task<int?> CurrentVersionAsync(CancellationToken ct)
    {
        var mapping = await search.GetAsync($"{ProductIndex.Alias}/_mapping", ct);
        var first = mapping?.AsObject().Select(kv => kv.Value).FirstOrDefault();
        var version = first?["mappings"]?["_meta"]?["version"];
        return version is null ? null : version.GetValue<int>();
    }

    public async Task ReindexAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await ReindexLockedAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task ReindexLockedAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var index = ProductIndex.NewIndexName(started);
        await search.PutAsync(index, ProductIndex.Definition, ct);
        var ids = await products.ActiveProductIdsAsync(ct);
        foreach (var chunk in ids.Chunk(ChunkSize)) await IndexChunkAsync(index, chunk, deleteMissing: false, ct);
        await search.PostAsync($"{index}/_refresh", new JsonObject(), ct);

        var previous = await AliasedIndicesAsync(ct);
        var actions = new JsonArray();
        foreach (var old in previous) actions.Add((JsonNode)new JsonObject { ["remove"] = new JsonObject { ["index"] = old, ["alias"] = ProductIndex.Alias } });
        actions.Add((JsonNode)new JsonObject { ["add"] = new JsonObject { ["index"] = index, ["alias"] = ProductIndex.Alias } });
        await search.PostAsync("_aliases", new JsonObject { ["actions"] = actions }, ct);

        var stale = (await search.GetAsync($"_cat/indices/{ProductIndex.IndexPrefix}*?format=json&h=index", ct))?.AsArray()
            .Select(n => n?["index"]?.GetValue<string>()).Where(n => n is not null && n != index).ToList() ?? [];
        foreach (var old in stale) await search.DeleteAsync(old!, ct);
        log.LogInformation("Rebuilt search index {Index} with {Count} products in {Ms} ms", index, ids.Count, (int)(DateTime.UtcNow - started).TotalMilliseconds);
    }

    async Task<List<string>> AliasedIndicesAsync(CancellationToken ct)
    {
        if (!await search.ExistsAsync($"_alias/{ProductIndex.Alias}", ct)) return [];
        var node = await search.GetAsync($"_alias/{ProductIndex.Alias}", ct);
        return node?.AsObject().Select(kv => kv.Key).ToList() ?? [];
    }

    async Task IndexChunkAsync(string index, long[] ids, bool deleteMissing, CancellationToken ct)
    {
        var records = await products.LoadAsync(ids, ct);
        var active = records.Where(r => r.Status == "active").ToList();
        var body = new StringBuilder();
        foreach (var r in active)
        {
            body.Append("{\"index\":{\"_id\":\"").Append(r.Id).Append("\"}}\n");
            body.Append(JsonSerializer.Serialize(SearchRepository.ToDocument(r), SearchJsonContext.Default.ProductDocument)).Append('\n');
        }
        var removed = deleteMissing ? ids.Except(active.Select(r => r.Id)).ToList() : [];
        foreach (var id in removed) body.Append("{\"delete\":{\"_id\":\"").Append(id).Append("\"}}\n");
        if (body.Length == 0) return;

        var result = await search.BulkAsync($"{index}/_bulk", body.ToString(), ct);
        if (result?["errors"]?.GetValue<bool>() == true)
        {
            var failure = result["items"]!.AsArray()
                .Select(i => i!.AsObject().First().Value)
                .FirstOrDefault(i => i?["error"] is not null && i["status"]?.GetValue<int>() != 404);
            if (failure is not null) throw new OpenSearchException(failure["status"]?.GetValue<int>() ?? 500, $"Bulk indexing failed: {failure["error"]!.ToJsonString()}");
        }
        log.LogDebug("Indexed {Indexed} and removed {Removed} products in {Index}", active.Count, removed.Count, index);
    }
}

public sealed class SearchBootstrap(SearchIndexer indexer, ILogger<SearchBootstrap> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!indexer.IsConfigured) return;
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await indexer.EnsureIndexAsync(ct);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OpenSearchException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt % 10 == 1) log.LogWarning("Search index is not ready yet ({Message}), retrying", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}
