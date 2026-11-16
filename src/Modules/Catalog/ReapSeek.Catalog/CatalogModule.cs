using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Catalog.Api;
using ReapSeek.Catalog.Application;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Catalog.Infrastructure;
using ReapSeek.Catalog.Jobs;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Catalog;

public static class CatalogModule
{
    public static IJsonTypeInfoResolver JsonTypeInfoResolver => CatalogJsonContext.Default;

    public static IServiceCollection AddCatalog(this IServiceCollection services)
    {
        services.AddSingleton<UploadStore>();
        services.AddSingleton<ProductImageService>();
        services.AddSingleton<CatalogRepository>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<ICatalogBrowser>(sp => sp.GetRequiredService<CatalogService>());
        services.AddSingleton<CategoryCache>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<IProductCards>(sp => sp.GetRequiredService<SearchService>());
        services.AddSingleton<ProductRepository>();
        services.AddSingleton<ProductService>();
        services.AddSingleton<RecommenderClient>();
        services.AddSingleton<RecommendationService>();
        services.AddSingleton<SellerCatalogRepository>();
        services.AddSingleton<ISellerCatalog, SellerCatalog>();
        return services;
    }

    public static IServiceCollection AddCatalogJobs(this IServiceCollection services)
    {
        services.AddSingleton<ProductCacheInvalidator>();
        services.AddSingleton<SearchRepository>();
        services.AddSingleton<SearchIndexer>();
        services.AddSingleton<ICatalogChangeHandler, CatalogChangeHandler>();
        services.AddHostedService<SearchBootstrap>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapStorefrontCatalog();
        api.MapStorefrontProducts();
        api.MapRecommendations();
        api.MapGroup("/backoffice/catalog").MapCatalogAdmin();
        api.MapProductImages();
        return api;
    }

    public static IEndpointRouteBuilder MapCatalogMedia(this IEndpointRouteBuilder app)
    {
        app.MapMedia();
        return app;
    }
}
