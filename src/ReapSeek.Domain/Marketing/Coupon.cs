using System.Security.Cryptography;
using ReapSeek.Domain.Common;

namespace ReapSeek.Domain.Marketing;

public enum DiscountKind { Percent, Fixed }

public sealed record Coupon(
    string Code, long? CustomerId, DiscountKind Kind, decimal Value, Money MinSubtotal, Money? MaxDiscount, DateTime ExpiresAt, Guid? UsedOrderId)
{
    public const decimal MaxPercent = 90m;
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewCode(string prefix) =>
        $"{prefix}-{string.Concat(Enumerable.Range(0, 8).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]))}";

    public static void ValidateTerms(DiscountKind kind, decimal value, decimal minSubtotal, decimal? maxDiscount)
    {
        if (kind == DiscountKind.Percent && value is <= 0 or > MaxPercent)
            throw new DomainException("coupon.value", $"A percentage discount must be between 1 and {MaxPercent}");
        if (kind == DiscountKind.Fixed && value <= 0) throw new DomainException("coupon.value", "A fixed discount must be positive");
        if (minSubtotal < 0) throw new DomainException("coupon.min_subtotal", "The minimum basket cannot be negative");
        if (maxDiscount is <= 0) throw new DomainException("coupon.max_discount", "The discount cap must be positive");
        if (kind == DiscountKind.Fixed && minSubtotal > 0 && value > minSubtotal)
            throw new DomainException("coupon.value", "A fixed discount cannot be larger than the minimum basket");
    }

    public Money DiscountFor(Money subtotal, long customerId, DateTime now)
    {
        if (CustomerId is { } owner && owner != customerId) throw new DomainException("coupon.not_found", "This code is not valid");
        if (UsedOrderId is not null) throw new DomainException("coupon.used", "This code has already been used");
        if (now >= ExpiresAt) throw new DomainException("coupon.expired", "This code has expired");
        if (subtotal < MinSubtotal)
            throw new DomainException("coupon.min_subtotal", $"This code needs a basket of at least {MinSubtotal.Amount:0.00} {MinSubtotal.Currency}");
        var amount = Kind == DiscountKind.Percent
            ? Math.Round(subtotal.Amount * Value / 100m, 2, MidpointRounding.AwayFromZero)
            : Value;
        if (MaxDiscount is { } cap && amount > cap.Amount) amount = cap.Amount;
        if (amount > subtotal.Amount) amount = subtotal.Amount;
        return Money.Of(amount, subtotal.Currency);
    }
}

public sealed record OrderDiscount(string CouponCode, Money Amount);
