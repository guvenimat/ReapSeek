using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Engagement.Api;
using ReapSeek.Engagement.Infrastructure;
using ReapSeek.Engagement.Jobs;

namespace ReapSeek.Engagement;

public static class EngagementModule
{
    public static IJsonTypeInfoResolver JsonTypeInfoResolver => EngagementJsonContext.Default;

    public static IServiceCollection AddEngagement(this IServiceCollection services)
    {
        services.AddSingleton<ReviewRepository>();
        services.AddSingleton<WishlistRepository>();
        return services;
    }

    public static IServiceCollection AddEngagementJobs(this IServiceCollection services)
    {
        services.AddHostedService<PriceDropJob>();
        return services;
    }

    public static IEndpointRouteBuilder MapEngagementEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapReviews();
        api.MapWishlist();
        return api;
    }
}
