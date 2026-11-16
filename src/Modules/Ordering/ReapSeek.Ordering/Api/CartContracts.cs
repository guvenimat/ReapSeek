namespace ReapSeek.Ordering.Api;

internal sealed record AddToCartRequest(long? OfferId, int? Quantity);
internal sealed record UpdateCartLineRequest(int? Quantity);

internal sealed record CartLineView(
    long OfferId, long ProductId, string ProductSlug, string ProductTitle, string Brand, string VariantLabel, string? ImageUrl,
    int Quantity, decimal UnitPrice, decimal? ListPrice, decimal AddedPrice, decimal LineTotal, int Available, int HandlingDays,
    string Status, string? Message);
internal sealed record CartPackageView(long SellerId, string SellerName, decimal SellerRating, List<CartLineView> Lines, decimal Subtotal, decimal ShippingFee, decimal FreeShippingRemaining);
internal sealed record CartView(
    List<CartPackageView> Packages, int ItemCount, decimal Subtotal, decimal ShippingTotal, decimal Total, string Currency, bool CanCheckout, List<string> Warnings);
internal sealed record CartSummary(int ItemCount);

internal static class CartLineStatus
{
    public const string Ok = "ok";
    public const string PriceChanged = "price_changed";
    public const string LowStock = "low_stock";
    public const string Unavailable = "unavailable";
}
