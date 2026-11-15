using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Marketing.Api;
using ReapSeek.Marketing.Infrastructure;

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

    public static IEndpointRouteBuilder MapMarketingEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapCampaigns();
        api.MapAlerts();
        return api;
    }
}
