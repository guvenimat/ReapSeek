using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ReapSeek.Api.Alerts;
using ReapSeek.Api.Carts;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Checkout;
using ReapSeek.Api.Customers;
using ReapSeek.Api.Health;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Marketing;
using ReapSeek.Api.Media;
using ReapSeek.Api.Operations;
using ReapSeek.Api.Orders;
using ReapSeek.Api.Payments;
using ReapSeek.Api.Products;
using ReapSeek.Api.Recommendations;
using ReapSeek.Api.Returns;
using ReapSeek.Api.Reviews;
using ReapSeek.Api.Search;
using ReapSeek.Api.SellerPanel;
using ReapSeek.Api.Sellers;
using ReapSeek.Shared;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Caching;
using ReapSeek.Shared.Observability;
using ReapSeek.Shared.Payouts;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Infrastructure;

public static class HostingExtensions
{
    public const string AuthRateLimit = "auth";
    const int DefaultAuthAttemptsPerMinute = 10;
    static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);

    public static WebApplicationBuilder AddReapSeekApi(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var services = builder.Services;

        builder.AddReapSeekTelemetry("reapseek-api")?
            .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz")))
            .WithMetrics(m => m.AddAspNetCoreInstrumentation());
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
            o.OnRejected = (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                return ValueTask.CompletedTask;
            };
            o.AddPolicy(AuthRateLimit, ctx => RateLimitPartition.Get(ClientIp(ctx), ip =>
                new RedisFixedWindowLimiter(ctx.RequestServices.GetRequiredService<RedisCache>(), CacheKeys.RateLimit(AuthRateLimit, ip), attempts, AuthWindow,
                    new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = AuthWindow, QueueLimit = 0 }),
                    ctx.RequestServices.GetRequiredService<ILogger<RedisFixedWindowLimiter>>())));
        });

        services.AddSingleton(Database.Create(cfg.GetConnectionString("Postgres")));
        services.AddSingleton<RedisCache>();
        services.AddSingleton<UploadStore>();
        services.AddSingleton<ProductImageService>();

        services.AddSingleton<AccountRepository>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<AccountRecoveryService>();
        services.AddHostedService<BootstrapAdmin>();
        services.AddSingleton<CatalogRepository>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<SellerRepository>();
        services.AddSingleton<OpenSearchClient>();
        services.AddSingleton<CategoryCache>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<ProductRepository>();
        services.AddSingleton<ProductService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CartRepository>();
        services.AddSingleton<CartService>();
        services.AddSingleton<SandboxClient>();
        services.AddSingleton<CheckoutService>();
        services.AddSingleton<PaymentService>();
        services.AddSingleton<OrderQueries>();
        services.AddSingleton<RecommenderClient>();
        services.AddSingleton<RecommendationService>();
        services.AddSingleton<SellerPanelRepository>();
        services.AddSingleton<SellerPanelService>();
        services.AddSingleton<PayoutRunner>();
        services.AddSingleton<ReturnService>();
        services.AddSingleton<ReviewRepository>();
        services.AddSingleton<ReapDeepAudience>();
        services.AddSingleton<ReapDeepProfile>();
        return builder;
    }

    public static WebApplication UseReapSeekApi(this WebApplication app)
    {
        if (List(app.Configuration["ForwardedHeaders:TrustedNetworks"]).Length > 0) app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseMiddleware<UserAccessMiddleware>();

        var api = app.MapGroup("/api/v1");
        var storefrontAuth = api.MapGroup("/auth");
        var backofficeAuth = api.MapGroup("/backoffice/auth");
        storefrontAuth.MapStorefrontAuth();
        backofficeAuth.MapBackofficeAuth();
        storefrontAuth.MapAccountRecovery(backofficeAuth, api);
        api.MapStorefrontCatalog();
        api.MapStorefrontProducts();
        api.MapGroup("/cart").MapCart();
        api.MapOrdering();
        api.MapRecommendations();
        api.MapGroup("/backoffice/catalog").MapCatalogAdmin();
        api.MapGroup("/backoffice/sellers").MapSellersAdmin();
        api.MapSellerPanel();
        api.MapReturns();
        api.MapReviews();
        api.MapCampaigns();
        api.MapAlerts();
        api.MapOutboxAdmin();
        api.MapProductImages();
        api.MapCustomers();
        app.MapMedia();
        app.MapHealth();
        return app;
    }

    static string[] List(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress is { } ip
        ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
        : "unknown";
}
