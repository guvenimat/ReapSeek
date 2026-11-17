using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Ordering.Api;
using ReapSeek.Ordering.Application;
using ReapSeek.Ordering.Contracts;
using ReapSeek.Ordering.Infrastructure;
using ReapSeek.Ordering.Jobs;

namespace ReapSeek.Ordering;

public static class OrderingModule
{
    public static IJsonTypeInfoResolver JsonTypeInfoResolver => OrderingJsonContext.Default;

    public static IServiceCollection AddOrdering(this IServiceCollection services)
    {
        services.AddSingleton<CartRepository>();
        services.AddSingleton<CartService>();
        services.AddSingleton<IStorefrontSignIn>(sp => sp.GetRequiredService<CartService>());
        services.AddSingleton<IStorefrontCart>(sp => sp.GetRequiredService<CartService>());
        services.AddSingleton<ShippingService>();
        services.AddSingleton<CheckoutService>();
        services.AddSingleton<OrderCancellation>();
        services.AddSingleton<PaymentService>();
        services.AddSingleton<OrderQueries>();
        services.AddSingleton<SellerPackagesRepository>();
        services.AddSingleton<ISellerPackages, SellerPackages>();
        services.AddSingleton<ReturnService>();
        return services;
    }

    public static IServiceCollection AddOrderingJobs(this IServiceCollection services)
    {
        services.AddHostedService<ShippingDeadlineJob>();
        services.AddHostedService<CartReminderJob>();
        services.AddHostedService<PaymentExpiryJob>();
        services.AddSingleton<IReturnRefunds, ReturnRefundHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGroup("/cart").MapCart();
        api.MapCheckout();
        api.MapPayments();
        api.MapOrders();
        api.MapReturns();
        api.MapShipping();
        return api;
    }
}
