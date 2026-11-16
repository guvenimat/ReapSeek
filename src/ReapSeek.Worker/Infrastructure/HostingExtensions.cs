using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Engagement.Contracts;
using ReapSeek.Identity.Contracts;
using ReapSeek.Marketing.Contracts;
using ReapSeek.Worker.Engagement.Wishlist;
using ReapSeek.Shared;
using ReapSeek.Shared.Caching;
using ReapSeek.Worker.Catalog.Caching;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Search;
using ReapSeek.Worker.Catalog.Search;
using ReapSeek.Worker.Catalog.CatalogSync;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Finance.Payouts;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Observability;
using ReapSeek.Modules.Finance;
using ReapSeek.Shared.Providers;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Infrastructure;

public static class HostingExtensions
{
    public static HostApplicationBuilder AddReapSeekWorker(this HostApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var services = builder.Services;

        builder.AddReapSeekTelemetry("reapseek-worker");

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
        services.AddSingleton<ReapDeepEventsHandler>();
        services.AddSingleton<OrderPaidHandler>();
        services.AddSingleton<ReturnHandler>();
        services.AddSingleton<QuestionHandler>();
        services.AddSingleton<CampaignHandler>();
        services.AddSingleton<CartReminderHandler>();
        services.AddSingleton<AlertMailHandler>();
        services.AddSingleton<FailedJobNotifier>();
        services.AddSingleton<OrderRefundHandler>();
        services.AddSingleton<PackageReminderHandler>();
        services.AddHostedService<ShippingDeadlineJob>();
        services.AddSingleton<AccountEmailHandler>();
        services.AddHostedService<CartReminderJob>();
        services.AddSingleton<PriceDropHandler>();
        services.AddHostedService<PriceDropJob>();
        services.AddHostedService<ReapSeek.Worker.Sellers.SellerRatingJob>();
        services.AddSingleton<ReapDeepAudience>();
        services.AddHostedService<PaymentExpiryJob>();
        services.AddSandboxProviders();
        services.AddSingleton<PayoutRunner>();
        services.AddHostedService<PayoutJob>();
        services.AddHostedService<ReapSeek.Worker.Platform.Maintenance.CleanupJob>();
        services.AddSingleton<OutboxRepository>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxProcessor>();
        services.AddMessagingWorker(new MessagingOptions(TimeSpan.FromMilliseconds(cfg.GetValue("Outbox:PollMilliseconds", 1000))), subscriptions => subscriptions
            .Add<QuestionAskedV1, QuestionHandler>(MessagingModules.Engagement, QuestionAskedV1.EventName, EngagementEventsJsonContext.Default.QuestionAskedV1,
                MessagingModules.Engagement, QuestionHandler.AskedHandler)
            .Add<QuestionAnsweredV1, QuestionHandler>(MessagingModules.Engagement, QuestionAnsweredV1.EventName, EngagementEventsJsonContext.Default.QuestionAnsweredV1,
                MessagingModules.Engagement, QuestionHandler.AnsweredHandler)
            .Add<PriceDroppedV1, PriceDropHandler>(MessagingModules.Engagement, PriceDroppedV1.EventName, EngagementEventsJsonContext.Default.PriceDroppedV1,
                MessagingModules.Engagement, PriceDropHandler.NotifyHandler)
            .Add<CampaignSendV1, CampaignHandler>(MessagingModules.Marketing, CampaignSendV1.EventName, MarketingEventsJsonContext.Default.CampaignSendV1,
                MessagingModules.Marketing, CampaignHandler.SendHandler)
            .Add<CouponIssuedV1, CampaignHandler>(MessagingModules.Marketing, CouponIssuedV1.EventName, MarketingEventsJsonContext.Default.CouponIssuedV1,
                MessagingModules.Marketing, CampaignHandler.CouponHandler)
            .Add<AlertReceivedV1, AlertMailHandler>(MessagingModules.Marketing, AlertReceivedV1.EventName, MarketingEventsJsonContext.Default.AlertReceivedV1,
                MessagingModules.Marketing, AlertMailHandler.NotifyHandler)
            .Add<UserRegisteredV1, AccountEmailHandler>(MessagingModules.Identity, UserRegisteredV1.EventName, IdentityEventsJsonContext.Default.UserRegisteredV1,
                MessagingModules.Identity, AccountEmailHandler.WelcomeHandler)
            .Add<AccountEmailV1, AccountEmailHandler>(MessagingModules.Identity, AccountEmailV1.EventName, IdentityEventsJsonContext.Default.AccountEmailV1,
                MessagingModules.Identity, AccountEmailHandler.AccountHandler));
        return builder;
    }
}
