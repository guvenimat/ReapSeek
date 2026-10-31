using ReapSeek.Api.Checkout;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Payments;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared;

namespace ReapSeek.Api.Orders;

public static class OrderEndpoints
{
    const int PageSize = 20;
    static readonly string[] Statuses = Enum.GetValues<OrderStatus>().Select(OrderStatusRules.Name).ToArray();

    public static void MapOrdering(this RouteGroupBuilder api)
    {
        api.MapPost("/checkout", async (PlaceOrderRequest req, UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Created(await checkout.PlaceOrderAsync(user, req, ct), AppJsonContext.Default.PlaceOrderResponse)).RequireCustomer();

        api.MapGet("/checkout/coupon", async (string? code, UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Ok(await checkout.PreviewCouponAsync(user, code, ct), AppJsonContext.Default.CouponPreview)).RequireCustomer();

        api.MapGet("/account/addresses", async (UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Ok(new AddressesResponse(await checkout.AddressesAsync(user.Id, ct)), AppJsonContext.Default.AddressesResponse)).RequireCustomer();

        api.MapGet("/orders", async (UserInfo user, OrderQueries orders, int? page, CancellationToken ct) =>
            ApiResults.Ok(await orders.ForCustomerAsync(user.Id, Ensure.InRange(page ?? 1, 1, 1000, "page"), PageSize, ct), AppJsonContext.Default.OrdersPage))
            .RequireCustomer();

        api.MapGet("/orders/{number}", async (string number, UserInfo user, OrderQueries orders, CancellationToken ct) =>
            await orders.DetailAsync(number, user.Id, ct) is { } order
                ? ApiResults.Ok(order, AppJsonContext.Default.OrderDetail)
                : ApiResults.NotFound("Order not found")).RequireCustomer();

        api.MapPost("/payments/sandbox/webhook", async (HttpRequest request, PaymentService payments, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            return await payments.HandleSandboxWebhookAsync(body, request.Headers["Sandbox-Signature"], ct) switch
            {
                WebhookOutcome.Rejected => ApiResults.Unauthorized("Invalid signature"),
                WebhookOutcome.Malformed => ApiResults.BadRequest("Malformed event"),
                _ => Results.Ok(),
            };
        });

        var admin = api.MapGroup("/backoffice/orders").RequireAdmin();
        admin.MapGet("/", async (OrderQueries orders, string? status, string? q, int? page, CancellationToken ct) =>
            ApiResults.Ok(await orders.AllAsync(
                status is { Length: > 0 } ? Ensure.OneOf(status, "status", Statuses) : null,
                q?.Trim() is { Length: > 0 } s ? Ensure.NotBlank(s, "q", 100) : null,
                Ensure.InRange(page ?? 1, 1, 1000, "page"), PageSize, ct), AppJsonContext.Default.OrdersPage));
        admin.MapGet("/{number}", async (string number, OrderQueries orders, CancellationToken ct) =>
            await orders.DetailAsync(number, null, ct) is { } order
                ? ApiResults.Ok(order, AppJsonContext.Default.OrderDetail)
                : ApiResults.NotFound("Order not found"));
    }
}
