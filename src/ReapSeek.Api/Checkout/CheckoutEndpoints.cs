using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Checkout;

public static class CheckoutEndpoints
{
    public static void MapCheckout(this RouteGroupBuilder api)
    {
        api.MapPost("/checkout", async (PlaceOrderRequest req, UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Created(await checkout.PlaceOrderAsync(user, req, ct), AppJsonContext.Default.PlaceOrderResponse)).RequireCustomer();

        api.MapGet("/checkout/coupon", async (string? code, UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Ok(await checkout.PreviewCouponAsync(user, code, ct), AppJsonContext.Default.CouponPreview)).RequireCustomer();

        api.MapGet("/account/addresses", async (UserInfo user, CheckoutService checkout, CancellationToken ct) =>
            ApiResults.Ok(new AddressesResponse(await checkout.AddressesAsync(user.Id, ct)), AppJsonContext.Default.AddressesResponse)).RequireCustomer();
    }
}
