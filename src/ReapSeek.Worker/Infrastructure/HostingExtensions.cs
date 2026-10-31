using ReapSeek.Shared;
using ReapSeek.Shared.Caching;
using ReapSeek.Worker.Caching;
using ReapSeek.Shared.Search;
using ReapSeek.Worker.Search;
using ReapSeek.Worker.CatalogSync;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Orders;
using ReapSeek.Worker.Payouts;
using ReapSeek.Shared.Payouts;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Infrastructure;

public static class HostingExtensions
{
    public static HostApplicationBuilder AddReapSeekWorker(this HostApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var services = builder.Services;

        services.AddSingleton(Database.Create(cfg.GetConnectionString("Postgres")));
        services.AddSingleton<RedisCache>();
        services.AddSingleton<ProductCacheInvalidator>();
        services.AddSingleton<EmailSender>();
        services.AddSingleton<ReapDeepClient>();
        services.AddSingleton<CatalogSyncRepository>();
        services.AddSingleton<CatalogSyncHandler>();
        services.AddSingleton<OpenSearchClient>();
        services.AddSingleton<SearchRepository>();
        services.AddSingleton<SearchIndexer>();
        services.AddHostedService<SearchBootstrap>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ReapDeepIngestClient>();
        services.AddSingleton<OrderPaidHandler>();
        services.AddSingleton<ReturnHandler>();
        services.AddHostedService<PaymentExpiryJob>();
        services.AddSingleton<PayoutRunner>();
        services.AddHostedService<PayoutJob>();
        services.AddSingleton<OutboxRepository>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxProcessor>();
        return builder;
    }
}
