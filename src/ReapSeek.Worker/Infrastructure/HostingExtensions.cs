using ReapSeek.Shared;
using ReapSeek.Shared.Search;
using ReapSeek.Worker.Search;
using ReapSeek.Worker.CatalogSync;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Infrastructure;

public static class HostingExtensions
{
    public static HostApplicationBuilder AddReapSeekWorker(this HostApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var services = builder.Services;

        services.AddSingleton(Database.Create(cfg.GetConnectionString("Postgres")));
        services.AddSingleton<EmailSender>();
        services.AddSingleton<ReapDeepClient>();
        services.AddSingleton<CatalogSyncRepository>();
        services.AddSingleton<CatalogSyncHandler>();
        services.AddSingleton<OpenSearchClient>();
        services.AddSingleton<SearchRepository>();
        services.AddSingleton<SearchIndexer>();
        services.AddHostedService<SearchBootstrap>();
        services.AddSingleton<OutboxRepository>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxProcessor>();
        return builder;
    }
}
