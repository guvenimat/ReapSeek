using System.Text.Json;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
using ReapSeek.Worker.Notifications;
namespace ReapSeek.Worker.Catalog.CatalogSync;

public sealed class CatalogSyncHandler(CatalogSyncRepository products, ReapDeepClient reapDeep, IConfiguration cfg, ILogger<CatalogSyncHandler> log)
{
    public const int ChunkSize = 1000;
    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    bool _warned;

    public async Task EnqueueAsync(IReadOnlyList<OutboxRepository.MessageRecord> messages, CancellationToken ct)
    {
        if (!reapDeep.IsConfigured)
        {
            if (!_warned) log.LogWarning("ReapDeep is not configured (ReapDeep:ApiUrl, ReapDeep:CatalogKey); catalog changes are not synced");
            _warned = true;
            return;
        }
        var all = messages.Any(m => m.Type == OutboxTypes.CatalogReindex);
        var ids = all ? [] : messages.SelectMany(m => JsonSerializer.Deserialize(m.Payload, OutboxJsonContext.Default.ProductsChanged)?.ProductIds ?? []).Distinct().ToList();
        if (all || ids.Count > 0) await products.EnqueueSyncAsync(new ReapDeepCatalogMessage(ids, all), ct);
    }

    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> messages, CancellationToken ct)
    {
        if (!reapDeep.IsConfigured) return;

        var payloads = messages.Select(m => JsonSerializer.Deserialize(m.Payload, OutboxJsonContext.Default.ReapDeepCatalogMessage)
            ?? throw new InvalidOperationException($"Outbox message {m.Id} has an empty payload")).ToList();
        var changed = payloads.SelectMany(p => p.ProductIds).ToHashSet();
        var ids = payloads.Any(p => p.All) ? await products.AllProductIdsAsync(ct) : changed.ToList();

        var upserted = 0;
        var removed = 0;
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            var rows = await products.LoadAsync(chunk, ct);
            var items = rows.Where(r => r.Status == "active").Select(r => ToItem(r, _storefrontUrl)).ToList();
            if (items.Count > 0) await reapDeep.UpsertAsync(items, ct);
            upserted += items.Count;

            var gone = chunk.Where(changed.Contains).Except(rows.Where(r => r.Status == "active").Select(r => r.Id)).ToList();
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
