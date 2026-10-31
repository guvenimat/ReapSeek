using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;

namespace ReapSeek.Domain.Ledger;

public static class LedgerKind
{
    public const string Sale = "sale";
    public const string Shipping = "shipping";
    public const string Commission = "commission";
    public const string Refund = "refund";
    public const string CommissionRefund = "commission_refund";
}

public sealed record LedgerEntry(string Kind, decimal Amount);

public static class Settlement
{
    public static readonly TimeSpan DefaultHold = TimeSpan.FromDays(7);
    public const decimal MaxCommissionRate = 0.5m;

    public static IReadOnlyList<LedgerEntry> ForPackage(SellerPackage package, decimal commissionRate)
    {
        if (package.Status != PackageStatus.Delivered)
            throw new DomainException("ledger.not_delivered", "Only a delivered package is settled");
        if (commissionRate is < 0 or > MaxCommissionRate)
            throw new DomainException("ledger.commission", $"Commission rate must be between 0 and {MaxCommissionRate}");

        var subtotal = package.Subtotal.Amount;
        var commission = Math.Round(subtotal * commissionRate, 2, MidpointRounding.AwayFromZero);
        var entries = new List<LedgerEntry> { new(LedgerKind.Sale, subtotal) };
        if (!package.ShippingFee.IsZero) entries.Add(new(LedgerKind.Shipping, package.ShippingFee.Amount));
        if (commission > 0) entries.Add(new(LedgerKind.Commission, -commission));
        return entries;
    }

    public static IReadOnlyList<LedgerEntry> ForRefund(decimal refunded, decimal commissionRate)
    {
        if (refunded <= 0) throw new DomainException("ledger.refund", "A refund must be positive");
        if (commissionRate is < 0 or > MaxCommissionRate)
            throw new DomainException("ledger.commission", $"Commission rate must be between 0 and {MaxCommissionRate}");
        var commission = Math.Round(refunded * commissionRate, 2, MidpointRounding.AwayFromZero);
        var entries = new List<LedgerEntry> { new(LedgerKind.Refund, -refunded) };
        if (commission > 0) entries.Add(new(LedgerKind.CommissionRefund, commission));
        return entries;
    }

    public static DateTime AvailableAt(DateTime deliveredAt, TimeSpan hold) => deliveredAt + hold;
}

public static class PayoutPeriod
{
    public static readonly TimeZoneInfo Turkey = TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "Turkey Time", "Turkey Time");

    public static DateTime CurrentPeriodEnd(DateTime nowUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var daysSinceMonday = ((int)local.DayOfWeek + 6) % 7;
        var monday = DateTime.SpecifyKind(local.Date.AddDays(-daysSinceMonday), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(monday, zone);
    }
}
