using ReapSeek.Shared;

namespace ReapSeek.Sellers.Application;

internal static class SellerStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public static readonly string[] All = [Active, Suspended];
}

internal static class SellerRules
{
    public const decimal DefaultCommission = 0.15m;

    public static decimal Commission(decimal? rate)
    {
        var r = rate ?? DefaultCommission;
        Ensure.That(r is >= 0m and <= 0.5m, "commission_rate must be between 0 and 0.5");
        return Math.Round(r, 4);
    }
}
