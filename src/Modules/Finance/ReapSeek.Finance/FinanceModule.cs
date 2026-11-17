using Microsoft.Extensions.DependencyInjection;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Finance.Application;
using ReapSeek.Finance.Contracts;
using ReapSeek.Finance.Infrastructure;
using ReapSeek.Finance.Jobs;
using ReapSeek.Ordering.Contracts;

namespace ReapSeek.Finance;

public static class FinanceModule
{
    public static IServiceCollection AddFinance(this IServiceCollection services)
    {
        services.AddSingleton<SellerFinanceRepository>();
        services.AddSingleton<ISellerFinance, SellerFinance>();
        return services;
    }

    public static IServiceCollection AddFinanceJobs(this IServiceCollection services)
    {
        services.AddHostedService<PayoutJob>();
        services.AddSingleton<LedgerRepository>();
        services.AddSingleton<LedgerHandler>();
        return services;
    }

    public static Subscriptions AddFinanceSubscriptions(this Subscriptions subscriptions) => subscriptions
        .Add<PackageDeliveredV1, LedgerHandler>(MessagingModules.Ordering, PackageDeliveredV1.EventName, OrderingEventsJsonContext.Default.PackageDeliveredV1,
            MessagingModules.Finance, LedgerHandler.DeliveredHandler)
        .Add<ReturnRefundedV1, LedgerHandler>(MessagingModules.Ordering, ReturnRefundedV1.EventName, OrderingEventsJsonContext.Default.ReturnRefundedV1,
            MessagingModules.Finance, LedgerHandler.RefundedHandler);
}
