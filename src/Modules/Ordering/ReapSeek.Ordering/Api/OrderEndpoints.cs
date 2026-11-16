using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Ordering.Application;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared;

namespace ReapSeek.Ordering.Api;

internal static class OrderEndpoints
{
    const int PageSize = 20;
    static readonly string[] Statuses = Enum.GetValues<OrderStatus>().Select(OrderStatusRules.Name).ToArray();

    public static void MapOrders(this IEndpointRouteBuilder api)
    {
        api.MapGet("/orders", async (UserInfo user, OrderQueries orders, int? page, CancellationToken ct) =>
            ApiResults.Ok(await orders.ForCustomerAsync(user.Id, Ensure.InRange(page ?? 1, 1, 1000, "page"), PageSize, ct), OrderingJsonContext.Default.OrdersPage))
            .RequireCustomer();

        api.MapGet("/orders/{number}", async (string number, UserInfo user, OrderQueries orders, CancellationToken ct) =>
            await orders.DetailAsync(number, user.Id, ct) is { } order
                ? ApiResults.Ok(order, OrderingJsonContext.Default.OrderDetail)
                : ApiResults.NotFound("Order not found")).RequireCustomer();

        api.MapPost("/orders/{number}/cancel", async (string number, UserInfo user, OrderCancellation cancellation, OrderQueries orders, CancellationToken ct) =>
            await cancellation.CancelAsync(number, user.Id, ct) == CancelOutcome.NotFound || await orders.DetailAsync(number, user.Id, ct) is not { } order
                ? ApiResults.NotFound("Order not found")
                : ApiResults.Ok(order, OrderingJsonContext.Default.OrderDetail)).RequireCustomer();

        var admin = api.MapGroup("/backoffice/orders").RequireAdmin();
        admin.MapGet("/", async (OrderQueries orders, string? status, string? q, int? page, CancellationToken ct) =>
            ApiResults.Ok(await orders.AllAsync(
                status is { Length: > 0 } ? Ensure.OneOf(status, "status", Statuses) : null,
                q?.Trim() is { Length: > 0 } s ? Ensure.NotBlank(s, "q", 100) : null,
                Ensure.InRange(page ?? 1, 1, 1000, "page"), PageSize, ct), OrderingJsonContext.Default.OrdersPage));
        admin.MapGet("/{number}", async (string number, OrderQueries orders, CancellationToken ct) =>
            await orders.DetailAsync(number, null, ct) is { } order
                ? ApiResults.Ok(order, OrderingJsonContext.Default.OrderDetail)
                : ApiResults.NotFound("Order not found"));
    }
}
