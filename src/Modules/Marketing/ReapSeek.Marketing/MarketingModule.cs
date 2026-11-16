using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Marketing.Api;
using ReapSeek.Marketing.Infrastructure;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Marketing.Contracts;
using ReapSeek.Marketing.Jobs;

namespace ReapSeek.Marketing;

public static class MarketingModule
{
    public static IJsonTypeInfoResolver JsonTypeInfoResolver => MarketingJsonContext.Default;

    public static IServiceCollection AddMarketing(this IServiceCollection services)
    {
        services.AddSingleton<AlertRepository>();
        services.AddSingleton<CampaignRepository>();
        return services;
    }

    public static IServiceCollection AddMarketingJobs(this IServiceCollection services)
    {
        services.AddSingleton<CampaignHandler>();
        return services;
    }

    public static Subscriptions AddMarketingSubscriptions(this Subscriptions subscriptions) =>
        subscriptions.Add<CampaignSendV1, CampaignHandler>(MessagingModules.Marketing, CampaignSendV1.EventName, MarketingEventsJsonContext.Default.CampaignSendV1,
            MessagingModules.Marketing, CampaignHandler.SendHandler);

    public static IEndpointRouteBuilder MapMarketingEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapCampaigns();
        api.MapAlerts();
        return api;
    }
}
