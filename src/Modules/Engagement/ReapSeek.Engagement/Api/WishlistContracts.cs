using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Engagement.Api;

internal sealed record WishlistItem(ProductCard Product, decimal? AddedPrice, decimal? PriceDrop, DateTime AddedAt);
internal sealed record WishlistResponse(List<WishlistItem> Items);
internal sealed record WishlistIdsResponse(List<long> ProductIds);
internal sealed record WishlistRequest(long? ProductId);
