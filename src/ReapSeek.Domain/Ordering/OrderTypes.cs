using System.Globalization;
using ReapSeek.Domain.Common;

namespace ReapSeek.Domain.Ordering;

public enum OrderStatus { PendingPayment, Paid, PaymentFailed, Cancelled }

public enum PackageStatus { AwaitingPayment, Preparing, Shipped, Delivered, Cancelled }

public static class OrderStatusRules
{
    static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Paid, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.Paid] = [],
        [OrderStatus.PaymentFailed] = [],
        [OrderStatus.Cancelled] = [],
    };

    static readonly Dictionary<PackageStatus, PackageStatus[]> AllowedPackage = new()
    {
        [PackageStatus.AwaitingPayment] = [PackageStatus.Preparing, PackageStatus.Cancelled],
        [PackageStatus.Preparing] = [PackageStatus.Shipped, PackageStatus.Cancelled],
        [PackageStatus.Shipped] = [PackageStatus.Delivered],
        [PackageStatus.Delivered] = [],
        [PackageStatus.Cancelled] = [],
    };

    public static bool CanMove(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    public static bool CanMove(PackageStatus from, PackageStatus to) => AllowedPackage[from].Contains(to);

    public static string Name(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "pending_payment",
        OrderStatus.Paid => "paid",
        OrderStatus.PaymentFailed => "payment_failed",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string Name(PackageStatus status) => status switch
    {
        PackageStatus.AwaitingPayment => "awaiting_payment",
        PackageStatus.Preparing => "preparing",
        PackageStatus.Shipped => "shipped",
        PackageStatus.Delivered => "delivered",
        PackageStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static OrderStatus ParseOrder(string name) => Enum.GetValues<OrderStatus>().First(s => Name(s) == name);

    public static PackageStatus ParsePackage(string name) => Enum.GetValues<PackageStatus>().First(s => Name(s) == name);
}

public sealed record ShippingAddress(string FullName, string Phone, string City, string District, string Line, string PostalCode)
{
    public static ShippingAddress Create(string? fullName, string? phone, string? city, string? district, string? line, string? postalCode)
    {
        static string Required(string? value, string field, int max)
        {
            var v = value?.Trim() ?? "";
            if (v.Length == 0 || v.Length > max) throw new DomainException("address.invalid", $"{field} is required (at most {max} characters)");
            return v;
        }

        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is < 10 or > 13) throw new DomainException("address.phone", "Phone must have 10 to 13 digits");
        var postal = (postalCode ?? "").Trim();
        if (postal.Length != 5 || !postal.All(char.IsDigit)) throw new DomainException("address.postal_code", "Postal code must have 5 digits");
        return new ShippingAddress(Required(fullName, "Full name", 100), digits, Required(city, "City", 60), Required(district, "District", 60),
            Required(line, "Address", 250), postal);
    }
}

public sealed record ShippingPolicy(Money FreeShippingThreshold, Money Fee)
{
    public static readonly ShippingPolicy Standard = new(Money.Of(500m), Money.Of(39.90m));

    public Money FeeFor(Money packageSubtotal) => packageSubtotal >= FreeShippingThreshold ? Money.Zero(packageSubtotal.Currency) : Fee;
}

public static class OrderNumber
{
    public static string Format(DateTime placedAt, long sequence) =>
        $"RS{placedAt.ToString("yyMMdd", CultureInfo.InvariantCulture)}{sequence % 1_000_000:000000}";
}

public sealed record OrderItem(
    long OfferId, long ProductId, long VariantId, long SellerId, string SellerName, string ProductTitle, string VariantLabel,
    string Sku, string? ImageUrl, Money UnitPrice, int Quantity);

public interface IReleasesStock
{
    Guid OrderId { get; }
}

public sealed record OrderPlaced(Guid OrderId, string Number, long CustomerId, DateTime OccurredAt) : IDomainEvent;
public sealed record OrderPaid(Guid OrderId, string Number, long CustomerId, DateTime OccurredAt) : IDomainEvent;
public sealed record OrderPaymentFailed(Guid OrderId, string Number, long CustomerId, string Reason, DateTime OccurredAt) : IDomainEvent, IReleasesStock;
public sealed record OrderCancelled(Guid OrderId, string Number, long CustomerId, string Reason, DateTime OccurredAt) : IDomainEvent, IReleasesStock;
public sealed record PackageShipped(Guid OrderId, string Number, long CustomerId, Guid PackageId, string SellerName, string TrackingNumber, DateTime OccurredAt) : IDomainEvent;
public sealed record PackageDelivered(Guid OrderId, string Number, long CustomerId, Guid PackageId, long SellerId, DateTime OccurredAt) : IDomainEvent;
