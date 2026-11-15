using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReapSeek.BuildingBlocks.Messaging;

public static class MessagingServices
{
    public static IServiceCollection AddMessagingWorker(this IServiceCollection services, MessagingOptions options, Action<Subscriptions> subscribe)
    {
        var subscriptions = new Subscriptions();
        subscribe(subscriptions);
        services.AddSingleton(subscriptions);
        services.AddSingleton(options);
        services.AddSingleton<IEventDelivery, InboxDelivery>();
        services.AddSingleton<RelayRepository>();
        services.AddSingleton<InboxRepository>();
        services.AddHostedService<MessageRelay>();
        foreach (var module in subscriptions.Subscribers)
            services.AddSingleton<IHostedService>(sp => new InboxProcessor(module, sp.GetRequiredService<InboxRepository>(), subscriptions, sp, options,
                sp.GetRequiredService<ILogger<InboxProcessor>>()));
        return services;
    }
}
