using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.CatalogSync;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Search;

namespace ReapSeek.Worker.Outbox;

public sealed class OutboxDispatcher(EmailSender email, SearchIndexer searchIndexer, CatalogSyncHandler catalogSync, IConfiguration cfg)
{
    const string CatalogGroup = "catalog";
    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public static string GroupOf(OutboxRepository.MessageRecord message) =>
        CatalogSyncHandler.Handles(message.Type) ? CatalogGroup : $"message:{message.Id}";

    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> batch, CancellationToken ct)
    {
        if (CatalogSyncHandler.Handles(batch[0].Type))
        {
            await searchIndexer.HandleAsync(batch, ct);
            await catalogSync.HandleAsync(batch, ct);
            return;
        }
        var message = batch.Single();
        await (message.Type switch
        {
            OutboxTypes.UserRegistered => email.SendAsync(EmailTemplates.Welcome(
                Deserialize(message, OutboxJsonContext.Default.UserRegistered), _storefrontUrl), ct),
            _ => throw new NotSupportedException($"Unknown outbox message type: {message.Type}"),
        });
    }

    static T Deserialize<T>(OutboxRepository.MessageRecord message, JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(message.Payload, type) ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
}
