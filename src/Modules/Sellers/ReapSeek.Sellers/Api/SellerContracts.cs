namespace ReapSeek.Sellers.Api;

internal sealed record SellerRow(
    long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating,
    long ProductCount, long OfferCount, DateTime CreatedAt);
internal sealed record SellersResponse(List<SellerRow> Sellers);
internal sealed record CreateSellerRequest(string? Name, string? City, decimal? CommissionRate);
internal sealed record UpdateSellerRequest(string? Name, string? City, string? Status, decimal? CommissionRate);
