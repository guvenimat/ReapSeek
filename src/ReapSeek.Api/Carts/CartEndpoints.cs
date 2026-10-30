using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Carts;

public static class CartEndpoints
{
    public static void MapCart(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.ViewAsync(ctx, ct), AppJsonContext.Default.CartView));
        group.MapGet("/summary", async (CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.SummaryAsync(ctx, ct), AppJsonContext.Default.CartSummary));
        group.MapPost("/items", async (AddToCartRequest req, CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.AddAsync(ctx, req, ct), AppJsonContext.Default.CartView));
        group.MapPatch("/items/{offerId:long}", async (long offerId, UpdateCartLineRequest req, CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.UpdateAsync(ctx, offerId, req, ct), AppJsonContext.Default.CartView));
        group.MapDelete("/items/{offerId:long}", async (long offerId, CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.RemoveAsync(ctx, offerId, ct), AppJsonContext.Default.CartView));
    }
}
