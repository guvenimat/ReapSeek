using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using ReapSeek.Api.Carts;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Checkout;
using ReapSeek.Api.Health;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Media;
using ReapSeek.Api.Orders;
using ReapSeek.Api.Payments;
using ReapSeek.Api.Products;
using ReapSeek.Api.Search;
using ReapSeek.Api.Sellers;
using ReapSeek.Shared;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Infrastructure;

public static class HostingExtensions
{
    public const string AuthRateLimit = "auth";
    const int DefaultAuthAttemptsPerMinute = 10;

    public static WebApplicationBuilder AddReapSeekApi(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var services = builder.Services;

        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        });
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var cidr in List(cfg["ForwardedHeaders:TrustedNetworks"])) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        });

        var attempts = cfg.GetValue("Auth:AttemptsPerMinute", DefaultAuthAttemptsPerMinute);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        services.AddSingleton(Database.Create(cfg.GetConnectionString("Postgres")));

        services.AddSingleton<AccountRepository>();
        services.AddSingleton<SessionService>();
        services.AddHostedService<BootstrapAdmin>();
        services.AddSingleton<CatalogRepository>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<SellerRepository>();
        services.AddSingleton<OpenSearchClient>();
        services.AddSingleton<CategoryCache>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<ProductRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CartRepository>();
        services.AddSingleton<CartService>();
        services.AddSingleton<SandboxClient>();
        services.AddSingleton<CheckoutService>();
        services.AddSingleton<PaymentService>();
        services.AddSingleton<OrderQueries>();
        return builder;
    }

    public static WebApplication UseReapSeekApi(this WebApplication app)
    {
        if (List(app.Configuration["ForwardedHeaders:TrustedNetworks"]).Length > 0) app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseMiddleware<UserAccessMiddleware>();

        var api = app.MapGroup("/api/v1");
        api.MapGroup("/auth").MapStorefrontAuth();
        api.MapGroup("/backoffice/auth").MapBackofficeAuth();
        api.MapStorefrontCatalog();
        api.MapStorefrontProducts();
        api.MapGroup("/cart").MapCart();
        api.MapOrdering();
        api.MapGroup("/backoffice/catalog").MapCatalogAdmin();
        api.MapGroup("/backoffice/sellers").MapSellersAdmin();
        app.MapMedia();
        app.MapHealth();
        return app;
    }

    static string[] List(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
