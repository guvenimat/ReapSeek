using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Sellers.Api;
using ReapSeek.Sellers.Infrastructure;

namespace ReapSeek.Sellers;

public static class SellersModule
{
    public static IJsonTypeInfoResolver JsonTypeInfoResolver => SellersJsonContext.Default;

    public static IServiceCollection AddSellers(this IServiceCollection services)
    {
        services.AddSingleton<SellerRepository>();
        services.AddSingleton<SellerPanelRepository>();
        services.AddSingleton<StoreRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapSellersEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGroup("/backoffice/sellers").MapSellersAdmin();
        api.MapSellerPanel();
        api.MapStores();
        return api;
    }
}
