using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Engagement.Infrastructure;
using ReapSeek.Modules.Catalog;

namespace ReapSeek.Engagement.Api;

internal static class WishlistEndpoints
{
    public static void MapWishlist(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/wishlist").RequireCustomer();

        group.MapGet("/", async (UserInfo user, WishlistRepository wishlist, IProductCards search, CancellationToken ct) =>
        {
            var rows = await wishlist.ListAsync(user.Id, ct);
            var cards = (await search.CardsAsync(rows.Select(r => r.ProductId).ToList(), false, ct)).ToDictionary(c => c.Id);
            var items = rows.Where(r => cards.ContainsKey(r.ProductId)).Select(r =>
            {
                var card = cards[r.ProductId];
                var drop = r.AddedPrice is { } added && card.InStock && card.Price is { } now && now < added ? added - now : (decimal?)null;
                return new WishlistItem(card, r.AddedPrice, drop, r.CreatedAt);
            }).ToList();
            return ApiResults.Ok(new WishlistResponse(items), EngagementJsonContext.Default.WishlistResponse);
        });

        group.MapGet("/ids", async (UserInfo user, WishlistRepository wishlist, CancellationToken ct) =>
            ApiResults.Ok(new WishlistIdsResponse(await wishlist.IdsAsync(user.Id, ct)), EngagementJsonContext.Default.WishlistIdsResponse));

        group.MapPost("/", async (WishlistRequest req, UserInfo user, WishlistRepository wishlist, CancellationToken ct) =>
        {
            if (req.ProductId is not > 0) return ApiResults.BadRequest("product_id is required");
            return await wishlist.AddAsync(user.Id, req.ProductId.Value, ct) switch
            {
                null => ApiResults.NotFound("Product not found"),
                false => ApiResults.Conflict($"Your wishlist can hold at most {WishlistRepository.MaxItems} products"),
                _ => ApiResults.Ok(new WishlistIdsResponse(await wishlist.IdsAsync(user.Id, ct)), EngagementJsonContext.Default.WishlistIdsResponse),
            };
        });

        group.MapDelete("/{productId:long}", async (long productId, UserInfo user, WishlistRepository wishlist, CancellationToken ct) =>
        {
            await wishlist.RemoveAsync(user.Id, productId, ct);
            return ApiResults.Ok(new WishlistIdsResponse(await wishlist.IdsAsync(user.Id, ct)), EngagementJsonContext.Default.WishlistIdsResponse);
        });
    }
}
