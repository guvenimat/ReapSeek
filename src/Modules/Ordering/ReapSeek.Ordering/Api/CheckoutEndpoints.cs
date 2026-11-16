using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Ordering.Application;

namespace ReapSeek.Ordering.Api;

internal static class CheckoutEndpoints
{
    public static void MapCheckout(this IEndpointRouteBuilder api)
    {
        api.MapPost("/checkout", async (PlaceOrderRequest req, UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Created(await checkout.PlaceOrderAsync(user, req, ct), OrderingJsonContext.Default.PlaceOrderResponse)).RequireCustomer();

        api.MapGet("/checkout/coupon", async (string? code, UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Ok(await checkout.PreviewCouponAsync(user, code, ct), OrderingJsonContext.Default.CouponPreview)).RequireCustomer();

        api.MapGet("/account/addresses", async (UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Ok(new AddressesResponse(await checkout.AddressesAsync(user.Id, ct)), OrderingJsonContext.Default.AddressesResponse)).RequireCustomer();
    }
}
