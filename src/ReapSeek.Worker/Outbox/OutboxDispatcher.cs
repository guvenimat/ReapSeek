using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Catalog.Caching;
using ReapSeek.Worker.Catalog.CatalogSync;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Catalog.Search;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Outbox;

public sealed class OutboxDispatcher(ProductCacheInvalidator productCache, SearchIndexer searchIndexer, CatalogSyncHandler catalogSync,
    OrderPaidHandler orderPaid, ReturnHandler returns, CartReminderHandler cartReminders, FailedJobNotifier failedJobs,
    OrderRefundHandler orderRefunds, PackageReminderHandler packageReminders, ReapDeepEventsHandler reapDeepEvents)
{
    const string CatalogGroup = "catalog";
    const string ReapDeepCatalogGroup = "reapdeep_catalog";

    static bool IsCatalogChange(string type) => type is OutboxTypes.ProductsChanged or OutboxTypes.CatalogReindex;

    public static string GroupOf(OutboxRepository.MessageRecord message) =>
        IsCatalogChange(message.Type) ? CatalogGroup
        : message.Type == OutboxTypes.ReapDeepCatalog ? ReapDeepCatalogGroup
        : $"message:{message.Id}";

    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> batch, CancellationToken ct)
    {
        if (IsCatalogChange(batch[0].Type))
        {
            await productCache.HandleAsync(batch, ct);
            await searchIndexer.HandleAsync(batch, ct);
            await catalogSync.EnqueueAsync(batch, ct);
            return;
        }
        if (batch[0].Type == OutboxTypes.ReapDeepCatalog)
        {
            await catalogSync.HandleAsync(batch, ct);
            return;
        }
        var message = batch.Single();
        if (ReturnHandler.Handles(message.Type))
        {
            await returns.HandleAsync(message, ct);
            return;
        }
        await (message.Type switch
        {
            OutboxTypes.OrderPaid => orderPaid.HandleAsync(message, ct),
            OutboxTypes.PackageShipped => orderPaid.HandleShippedAsync(message, ct),
            OutboxTypes.PackageShipReminder => packageReminders.HandleAsync(message, ct),
            OutboxTypes.OrderRefundDue => orderRefunds.HandleAsync(message, ct),
            OutboxTypes.ReapDeepEvents => reapDeepEvents.HandleAsync(message, ct),
            OutboxTypes.CartReminder => cartReminders.HandleAsync(message, ct),
            OutboxTypes.OutboxFailed => failedJobs.HandleAsync(message, ct),
            OutboxTypes.PackageDelivered => Task.CompletedTask,
            OutboxTypes.OrderPlaced or OutboxTypes.OrderPaymentFailed or OutboxTypes.OrderCancelled => Task.CompletedTask,
            _ => throw new NotSupportedException($"Unknown outbox message type: {message.Type}"),
        });
    }
}
