using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ReapSeek.Api.Health;
using ReapSeek.Api.Operations;
using ReapSeek.Api.Sellers.Stores;
using ReapSeek.Api.Sellers.SellerPanel;
using ReapSeek.Api.Sellers;
using ReapSeek.Shared.Providers;
using ReapSeek.Shared;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Caching;
using ReapSeek.Shared.Observability;
using ReapSeek.Modules.Finance;
using ReapSeek.Shared.Search;
using ReapSeek.Engagement;
using ReapSeek.Marketing;
using ReapSeek.Finance;
using ReapSeek.Identity;
using ReapSeek.Catalog;
using ReapSeek.Ordering;

namespace ReapSeek.Api.Infrastructure;

public static class HostingExtensions
{
    public const string AuthRateLimit = RateLimits.Auth;
    public const string ContentRateLimit = RateLimits.Content;
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
            o.SerializerOptions.TypeInfoResolverChain.Insert(1, EngagementModule.JsonTypeInfoResolver);
            o.SerializerOptions.TypeInfoResolverChain.Insert(2, MarketingModule.JsonTypeInfoResolver);
            o.SerializerOptions.TypeInfoResolverChain.Insert(3, IdentityModule.JsonTypeInfoResolver);
            o.SerializerOptions.TypeInfoResolverChain.Insert(4, CatalogModule.JsonTypeInfoResolver);
            o.SerializerOptions.TypeInfoResolverChain.Insert(5, OrderingModule.JsonTypeInfoResolver);
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

        services.AddIdentity();
        services.AddSingleton<OutboxAdminRepository>();
        services.AddSingleton<DatabaseProbe>();
        services.AddMarketing();
        services.AddSingleton<StoreRepository>();
        services.AddSingleton<SellerRepository>();
        services.AddSingleton<OpenSearchClient>();
        services.AddCatalog();
        services.AddSingleton(TimeProvider.System);
        services.AddSandboxProviders();
        services.AddOrdering();
        services.AddSingleton<SellerPanelRepository>();
        services.AddFinance();
        services.AddSingleton<PayoutRunner>();
        services.AddEngagement();
        services.AddSingleton<ReapDeepAudience>();
        return builder;
    }

    public static WebApplication UseReapSeekApi(this WebApplication app)
    {
        if (List(app.Configuration["ForwardedHeaders:TrustedNetworks"]).Length > 0) app.UseForwardedHeaders();
        app.Use(SecurityHeaders);
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseIdentitySessions();

        var api = app.MapGroup("/api/v1");
        api.MapIdentityEndpoints();
        api.MapCatalogEndpoints();
        api.MapOrderingEndpoints();
        api.MapGroup("/backoffice/sellers").MapSellersAdmin();
        api.MapSellerPanel();
        api.MapEngagementEndpoints();
        api.MapStores();
        api.MapMarketingEndpoints();
        api.MapOutboxAdmin();
        app.MapCatalogMedia();
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
