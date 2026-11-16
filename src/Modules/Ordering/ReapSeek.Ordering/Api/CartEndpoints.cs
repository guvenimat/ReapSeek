using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Ordering.Application;

namespace ReapSeek.Ordering.Api;

internal static class CartEndpoints
{
    public static void MapCart(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.ViewAsync(ctx, ct), OrderingJsonContext.Default.CartView));
        group.MapGet("/summary", async (CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.SummaryAsync(ctx, ct), OrderingJsonContext.Default.CartSummary));
        group.MapPost("/items", async (AddToCartRequest req, CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.AddAsync(ctx, req, ct), OrderingJsonContext.Default.CartView));
        group.MapPatch("/items/{offerId:long}", async (long offerId, UpdateCartLineRequest req, CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.UpdateAsync(ctx, offerId, req, ct), OrderingJsonContext.Default.CartView));
        group.MapDelete("/items/{offerId:long}", async (long offerId, CartService carts, HttpContext ctx, CancellationToken ct) =>
            ApiResults.Ok(await carts.RemoveAsync(ctx, offerId, ct), OrderingJsonContext.Default.CartView));
    }
}
