using System.Globalization;

namespace ReapSeek.Domain.Common;

public readonly record struct Money : IComparable<Money>
{
    public const string DefaultCurrency = "TRY";

    public decimal Amount { get; }
    public string Currency { get; }

    Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0) throw new DomainException("money.negative", "An amount cannot be negative");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3) throw new DomainException("money.currency", "Currency must be a three-letter code");
        return new Money(Math.Round(amount, 2, MidpointRounding.AwayFromZero), currency.ToUpperInvariant());
    }

    public static Money Zero(string currency = DefaultCurrency) => Of(0, currency);

    public bool IsZero => Amount == 0;

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator *(Money a, int quantity)
    {
        if (quantity < 0) throw new DomainException("money.quantity", "Quantity cannot be negative");
        return new Money(a.Amount * quantity, a.Currency);
    }

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public static Money Sum(IEnumerable<Money> values, string currency = DefaultCurrency) =>
        values.Aggregate(Zero(currency), (total, m) => total + m);

    static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency) throw new DomainException("money.currency_mismatch", $"Cannot combine {a.Currency} and {b.Currency}");
    }

    public override string ToString() => $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}";
}
