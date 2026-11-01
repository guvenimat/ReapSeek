using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Caching;
using ReapSeek.Worker.CatalogSync;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Orders;
using ReapSeek.Worker.Search;

namespace ReapSeek.Worker.Outbox;

public sealed class OutboxDispatcher(ProductCacheInvalidator productCache, SearchIndexer searchIndexer, CatalogSyncHandler catalogSync,
    OrderPaidHandler orderPaid, ReturnHandler returns, QuestionHandler questions, CampaignHandler campaigns, EngagementHandler engagement, AccountEmailHandler accounts,
    ReapSeek.Worker.Wishlist.PriceDropHandler priceDrops, OrderRefundHandler orderRefunds)
{
    const string CatalogGroup = "catalog";

    public static string GroupOf(OutboxRepository.MessageRecord message) =>
        CatalogSyncHandler.Handles(message.Type) ? CatalogGroup : $"message:{message.Id}";

    public async Task HandleAsync(IReadOnlyList<OutboxRepository.MessageRecord> batch, CancellationToken ct)
    {
        if (CatalogSyncHandler.Handles(batch[0].Type))
        {
            await productCache.HandleAsync(batch, ct);
            await searchIndexer.HandleAsync(batch, ct);
            await catalogSync.HandleAsync(batch, ct);
            return;
        }
        var message = batch.Single();
        if (message.Type is OutboxTypes.CartReminder or OutboxTypes.AlertReceived or OutboxTypes.OutboxFailed)
        {
            await engagement.HandleAsync(message, ct);
            return;
        }
        if (message.Type is OutboxTypes.CampaignSend or OutboxTypes.CouponIssued)
        {
            await campaigns.HandleAsync(message, ct);
            return;
        }
        if (message.Type is OutboxTypes.QuestionAsked or OutboxTypes.QuestionAnswered)
        {
            await questions.HandleAsync(message, ct);
            return;
        }
        if (ReturnHandler.Handles(message.Type))
        {
            await returns.HandleAsync(message, ct);
            return;
        }
        await (message.Type switch
        {
            OutboxTypes.UserRegistered or OutboxTypes.AccountEmail => accounts.HandleAsync(message, ct),
            OutboxTypes.OrderPaid => orderPaid.HandleAsync(message, ct),
            OutboxTypes.PackageShipped => orderPaid.HandleShippedAsync(message, ct),
            OutboxTypes.OrderRefundDue => orderRefunds.HandleAsync(message, ct),
            OutboxTypes.PriceDropped => priceDrops.HandleAsync(message, ct),
            OutboxTypes.PackageDelivered => Task.CompletedTask,
            OutboxTypes.OrderPlaced or OutboxTypes.OrderPaymentFailed or OutboxTypes.OrderCancelled => Task.CompletedTask,
            _ => throw new NotSupportedException($"Unknown outbox message type: {message.Type}"),
        });
    }
}
