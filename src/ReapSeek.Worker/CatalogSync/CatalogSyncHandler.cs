using System.Text.Json;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.CatalogSync;

public sealed class CatalogSyncHandler(CatalogSyncRepository products, ReapDeepClient reapDeep, IConfiguration cfg, ILogger<CatalogSyncHandler> log)
{
    public const int ChunkSize = 1000;
    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    bool _warned;

    public static bool Handles(string type) => type is OutboxTypes.ProductsChanged or OutboxTypes.CatalogReindex;

    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> messages, CancellationToken ct)
    {
        if (!reapDeep.IsConfigured)
        {
            if (!_warned) log.LogWarning("ReapDeep is not configured (ReapDeep:ApiUrl, ReapDeep:CatalogKey); catalog changes are not synced");
            _warned = true;
            return;
        }

        var ids = messages.Any(m => m.Type == OutboxTypes.CatalogReindex)
            ? await products.AllProductIdsAsync(ct)
            : messages.SelectMany(m => (JsonSerializer.Deserialize(m.Payload, OutboxJsonContext.Default.ProductsChanged)?.ProductIds ?? [])).Distinct().ToList();

        var upserted = 0;
        var removed = 0;
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            var rows = await products.LoadAsync(chunk, ct);
            var items = rows.Where(r => r.Status == "active").Select(r => ToItem(r, _storefrontUrl)).ToList();
            if (items.Count > 0) await reapDeep.UpsertAsync(items, ct);
            upserted += items.Count;

            var gone = chunk.Except(rows.Where(r => r.Status == "active").Select(r => r.Id)).ToList();
            foreach (var id in gone) await reapDeep.DeleteAsync(id.ToString(), ct);
            removed += gone.Count;
        }
        log.LogInformation("Synced catalog to ReapDeep: {Upserted} upserted, {Removed} removed", upserted, removed);
    }

    public static ReapDeepItem ToItem(CatalogSyncRepository.ProductRecord p, string storefrontUrl) => new(
        p.Id.ToString(),
        p.Title,
        p.Category,
        p.Price,
        "TRY",
        p.Price is not null,
        $"{storefrontUrl}/p/{p.Slug}",
        p.ImageUrl is null ? "" : storefrontUrl + p.ImageUrl,
        new Dictionary<string, string> { ["brand"] = p.Brand, ["department"] = p.Department });
}
