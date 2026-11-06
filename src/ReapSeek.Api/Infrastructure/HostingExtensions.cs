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
using ReapSeek.Api.Stores;
using ReapSeek.Api.Wishlist;
using ReapSeek.Api.SellerPanel;
using ReapSeek.Api.Sellers;
using ReapSeek.Api.Shipping;
using ReapSeek.Shared.Providers;
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
    public const string ContentRateLimit = "content";
    const int DefaultAuthAttemptsPerMinute = 10;
    const int DefaultContentPostsPerMinute = 10;
    static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);

    static RateLimitPartition<string> Limit(HttpContext ctx, string policy, int permits) => RateLimitPartition.Get(ClientIp(ctx), ip =>
        new RedisFixedWindowLimiter(ctx.RequestServices.GetRequiredService<RedisCache>(), CacheKeys.RateLimit(policy, ip), permits, AuthWindow,
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = AuthWindow, QueueLimit = 0 }),
            ctx.RequestServices.GetRequiredService<ILogger<RedisFixedWindowLimiter>>()));

    public static WebApplicationBuilder AddReapSeekApi(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var services = builder.Services;

        builder.AddReapSeekTelemetry("reapseek-api")?
            .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz") && !ctx.Request.Path.StartsWithSegments("/livez")))
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
        var posts = cfg.GetValue("Content:PostsPerMinute", DefaultContentPostsPerMinute);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                return ValueTask.CompletedTask;
            };
            o.AddPolicy(AuthRateLimit, ctx => Limit(ctx, AuthRateLimit, attempts));
            o.AddPolicy(ContentRateLimit, ctx => Limit(ctx, ContentRateLimit, posts));
        });

        services.AddSingleton(Database.Create(cfg.GetConnectionString("Postgres")));
        services.AddSingleton<RedisCache>();
        services.AddSingleton<UploadStore>();
        services.AddSingleton<ProductImageService>();

        services.AddSingleton<AccountRepository>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<SellerAccounts>();
        services.AddSingleton<OutboxAdminRepository>();
        services.AddSingleton<DatabaseProbe>();
        services.AddSingleton<AlertRepository>();
        services.AddSingleton<CampaignRepository>();
        services.AddSingleton<CustomerRepository>();
        services.AddSingleton<WishlistRepository>();
        services.AddSingleton<StoreRepository>();
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
        services.AddSandboxProviders();
        services.AddSingleton<ShippingService>();
        services.AddSingleton<CheckoutService>();
        services.AddSingleton<OrderCancellation>();
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
        app.Use(SecurityHeaders);
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
        api.MapCheckout();
        api.MapPayments();
        api.MapOrdering();
        api.MapRecommendations();
        api.MapGroup("/backoffice/catalog").MapCatalogAdmin();
        api.MapGroup("/backoffice/sellers").MapSellersAdmin();
        api.MapSellerPanel();
        api.MapReturns();
        api.MapReviews();
        api.MapWishlist();
        api.MapStores();
        api.MapCampaigns();
        api.MapAlerts();
        api.MapOutboxAdmin();
        api.MapProductImages();
        api.MapCustomers();
        api.MapShipping();
        app.MapMedia();
        app.MapHealth();
        return app;
    }

    static Task SecurityHeaders(HttpContext ctx, RequestDelegate next)
    {
        var headers = ctx.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "frame-ancestors 'none'";
        headers["Referrer-Policy"] = "no-referrer";
        return next(ctx);
    }

    static string[] List(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress is { } ip
        ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
        : "unknown";
}
