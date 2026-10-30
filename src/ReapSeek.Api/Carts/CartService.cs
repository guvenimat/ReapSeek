using System.Security.Cryptography;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Domain.Carts;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared;

namespace ReapSeek.Api.Carts;

public sealed class CartService(CartRepository carts, SessionService sessions, IConfiguration cfg, TimeProvider clock)
{
    public const string CookieName = "rs_cart";
    const int MaxTokenLength = 64;
    static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);
    readonly bool _secureCookie = cfg["Auth:SecureCookie"] != "false";

    public async Task<CartOwner> OwnerAsync(HttpContext ctx)
    {
        var user = await sessions.GetCurrentAsync(ctx, SessionAudience.Storefront);
        return user is not null ? new CartOwner(user.Id, null) : new CartOwner(null, GuestToken(ctx));
    }

    public async Task<CartView> ViewAsync(HttpContext ctx, CancellationToken ct)
    {
        var cart = await carts.FindAsync(await OwnerAsync(ctx), ct);
        return await PriceAsync(cart, ct);
    }

    public async Task<CartSummary> SummaryAsync(HttpContext ctx, CancellationToken ct) =>
        new((await carts.FindAsync(await OwnerAsync(ctx), ct))?.ItemCount ?? 0);

    public async Task<CartView> AddAsync(HttpContext ctx, AddToCartRequest req, CancellationToken ct)
    {
        Ensure.That(req.OfferId is > 0, "offer_id is required");
        var quantity = Ensure.InRange(req.Quantity ?? 1, 1, Cart.MaxQuantityPerLine, "quantity");
        var offer = (await carts.OffersAsync([req.OfferId!.Value], ct)).SingleOrDefault() ?? throw new NotFoundException("Offer not found");
        if (!CartPricing.IsPurchasable(offer)) throw new ConflictException("This item is currently unavailable");

        var owner = await OwnerAsync(ctx);
        var cart = await carts.FindAsync(owner, ct) ?? NewCart(ctx, owner);
        var wanted = (cart.Find(offer.OfferId)?.Quantity ?? 0) + quantity;
        if (wanted > offer.Stock) throw new ConflictException($"Only {offer.Stock} left from this seller");
        cart.Add(offer.OfferId, quantity, Money.Of(offer.Price), clock.GetUtcNow().UtcDateTime);
        await carts.SaveAsync(cart, ct);
        return await PriceAsync(cart, ct);
    }

    public async Task<CartView> UpdateAsync(HttpContext ctx, long offerId, UpdateCartLineRequest req, CancellationToken ct)
    {
        var quantity = Ensure.InRange(req.Quantity ?? -1, 0, Cart.MaxQuantityPerLine, "quantity");
        var cart = await carts.FindAsync(await OwnerAsync(ctx), ct) ?? throw new NotFoundException("The cart is empty");
        if (quantity > 0)
        {
            var offer = (await carts.OffersAsync([offerId], ct)).SingleOrDefault();
            if (offer is not null && CartPricing.IsPurchasable(offer) && quantity > offer.Stock)
                throw new ConflictException($"Only {offer.Stock} left from this seller");
        }
        cart.SetQuantity(offerId, quantity);
        await carts.SaveAsync(cart, ct);
        return await PriceAsync(cart, ct);
    }

    public async Task<CartView> RemoveAsync(HttpContext ctx, long offerId, CancellationToken ct)
    {
        var cart = await carts.FindAsync(await OwnerAsync(ctx), ct) ?? throw new NotFoundException("The cart is empty");
        if (!cart.Remove(offerId)) throw new NotFoundException("This item is not in the cart");
        await carts.SaveAsync(cart, ct);
        return await PriceAsync(cart, ct);
    }

    public async Task MergeGuestCartAsync(HttpContext ctx, long customerId, CancellationToken ct)
    {
        if (GuestToken(ctx) is not { } token) return;
        var guest = await carts.FindAsync(new CartOwner(null, token), ct);
        ctx.Response.Cookies.Delete(CookieName, CookieOptions(null));
        if (guest is null) return;
        var customer = await carts.FindAsync(new CartOwner(customerId, null), ct) ?? Cart.ForCustomer(Guid.CreateVersion7(), customerId);
        customer.MergeFrom(guest);
        await carts.SaveAsync(customer, ct);
        await carts.DeleteAsync(guest.Id, ct);
    }

    async Task<CartView> PriceAsync(Cart? cart, CancellationToken ct)
    {
        if (cart is null || cart.Lines.Count == 0)
            return new CartView([], 0, 0, 0, 0, Money.DefaultCurrency, false, []);
        var offers = await carts.OffersAsync(cart.Lines.Select(l => l.OfferId).ToArray(), ct);
        return CartPricing.Build(cart, offers, ShippingPolicy.Standard);
    }

    Cart NewCart(HttpContext ctx, CartOwner owner)
    {
        if (owner.CustomerId is { } id) return Cart.ForCustomer(Guid.CreateVersion7(), id);
        var token = owner.GuestToken ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        ctx.Response.Cookies.Append(CookieName, token, CookieOptions(DateTime.UtcNow + CookieLifetime));
        return Cart.ForGuest(Guid.CreateVersion7(), token);
    }

    static string? GuestToken(HttpContext ctx) =>
        ctx.Request.Cookies.TryGetValue(CookieName, out var token) && token is { Length: > 0 and <= MaxTokenLength } ? token : null;

    CookieOptions CookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = _secureCookie,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
    };
}
