using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Engagement.Api;
using ReapSeek.Engagement.Infrastructure;

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

    public static IEndpointRouteBuilder MapEngagementEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapReviews();
        api.MapWishlist();
        return api;
    }
}
