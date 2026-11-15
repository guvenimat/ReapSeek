using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Api;
using ReapSeek.Identity.Application;
using ReapSeek.Identity.Contracts;
using ReapSeek.Identity.Infrastructure;

namespace ReapSeek.Identity;

public static class IdentityModule
{
    public static IJsonTypeInfoResolver JsonTypeInfoResolver => IdentityJsonContext.Default;

    public static IServiceCollection AddIdentity(this IServiceCollection services)
    {
        services.AddSingleton<AccountRepository>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<ISessionReader>(sp => sp.GetRequiredService<SessionService>());
        services.AddSingleton<ISellerAccounts, SellerAccounts>();
        services.AddSingleton<CustomerRepository>();
        services.AddSingleton<AccountRecoveryService>();
        services.AddHostedService<BootstrapAdmin>();
        services.AddSingleton<ReapDeepProfile>();
        return services;
    }

    public static IApplicationBuilder UseIdentitySessions(this IApplicationBuilder app) => app.UseMiddleware<UserAccessMiddleware>();

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        var storefrontAuth = api.MapGroup("/auth");
        var backofficeAuth = api.MapGroup("/backoffice/auth");
        storefrontAuth.MapStorefrontAuth();
        backofficeAuth.MapBackofficeAuth();
        storefrontAuth.MapAccountRecovery(backofficeAuth, api);
        api.MapCustomers();
        return api;
    }
}
