using System.Globalization;
using System.Text.Json;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Domain.Carts;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;

namespace ReapSeek.Api.Carts;

public static class CartPricing
{
    static readonly string[] OptionOrder = ["Color", "Size", "Storage", "Memory"];

    public static bool IsPurchasable(CartRepository.OfferRecord o) =>
        o.OfferStatus == "active" && o.SellerStatus == "active" && o.ProductStatus == "active" && o.Stock > 0;

    public static string VariantLabel(string optionsJson)
    {
        var options = JsonSerializer.Deserialize(optionsJson, AppJsonContext.Default.DictionaryStringString) ?? [];
        return string.Join(" · ", options
            .OrderBy(kv => Array.IndexOf(OptionOrder, kv.Key) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    public static CartView Build(Cart cart, IReadOnlyCollection<CartRepository.OfferRecord> offers, ShippingPolicy shipping)
    {
        var byId = offers.ToDictionary(o => o.OfferId);
        var warnings = new List<string>();
        var lines = new List<(CartRepository.OfferRecord? Offer, CartLineView View)>();
        foreach (var line in cart.Lines)
        {
            byId.TryGetValue(line.OfferId, out var o);
            if (o is null)
            {
                warnings.Add("An item in your cart is no longer sold.");
                continue;
            }
            var (status, message) = !IsPurchasable(o)
                ? (CartLineStatus.Unavailable, "This item is currently unavailable.")
                : o.Stock < line.Quantity
                    ? (CartLineStatus.LowStock, $"Only {o.Stock} left. Reduce the quantity to continue.")
                    : o.Price != line.AddedPrice.Amount
                        ? (CartLineStatus.PriceChanged, $"The price changed from {line.AddedPrice.Amount.ToString("N2", CultureInfo.InvariantCulture)}.")
                        : (CartLineStatus.Ok, (string?)null);
            if (status == CartLineStatus.Unavailable) warnings.Add($"{o.ProductTitle} is currently unavailable.");
            if (status == CartLineStatus.LowStock) warnings.Add($"Only {o.Stock} of {o.ProductTitle} left.");
            lines.Add((o, new CartLineView(o.OfferId, o.ProductId, o.ProductSlug, o.ProductTitle, o.Brand, VariantLabel(o.Options), o.ImageUrl,
                line.Quantity, o.Price, o.ListPrice is { } lp && lp > o.Price ? lp : null, line.AddedPrice.Amount, o.Price * line.Quantity,
                IsPurchasable(o) ? o.Stock : 0, o.HandlingDays, status, message)));
        }

        var packages = lines
            .GroupBy(l => (l.Offer!.SellerId, l.Offer.SellerName, l.Offer.SellerRating))
            .OrderBy(g => g.Key.SellerName, StringComparer.Ordinal)
            .Select(g =>
            {
                var payable = g.Where(l => l.View.Status != CartLineStatus.Unavailable).ToList();
                var subtotal = Money.Sum(payable.Select(l => Money.Of(l.View.LineTotal)));
                var fee = payable.Count == 0 ? Money.Zero() : shipping.FeeFor(subtotal);
                var remaining = subtotal >= shipping.FreeShippingThreshold ? 0m : shipping.FreeShippingThreshold.Amount - subtotal.Amount;
                return new CartPackageView(g.Key.SellerId, g.Key.SellerName, g.Key.SellerRating, g.Select(l => l.View).ToList(),
                    subtotal.Amount, fee.Amount, payable.Count == 0 ? 0m : remaining);
            })
            .ToList();

        var subtotalAll = packages.Sum(p => p.Subtotal);
        var shippingAll = packages.Sum(p => p.ShippingFee);
        var canCheckout = lines.Count > 0 && lines.All(l => l.View.Status is CartLineStatus.Ok or CartLineStatus.PriceChanged);
        return new CartView(packages, cart.ItemCount, subtotalAll, shippingAll, subtotalAll + shippingAll, Money.DefaultCurrency, canCheckout, warnings);
    }
}
