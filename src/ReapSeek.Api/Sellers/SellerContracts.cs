using ReapSeek.Shared;

namespace ReapSeek.Api.Sellers;

public sealed record SellerRow(
    long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating,
    long ProductCount, long OfferCount, DateTime CreatedAt);
public sealed record SellersResponse(List<SellerRow> Sellers);
public sealed record CreateSellerRequest(string? Name, string? City, decimal? CommissionRate);
public sealed record UpdateSellerRequest(string? Name, string? City, string? Status, decimal? CommissionRate);

public static class SellerStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public static readonly string[] All = [Active, Suspended];
}

public static class SellerRules
{
    public const decimal DefaultCommission = 0.15m;

    public static decimal Commission(decimal? rate)
    {
        var r = rate ?? DefaultCommission;
        Ensure.That(r is >= 0m and <= 0.5m, "commission_rate must be between 0 and 0.5");
        return Math.Round(r, 4);
    }
}
