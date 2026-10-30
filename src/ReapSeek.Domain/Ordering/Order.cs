using ReapSeek.Domain.Common;

namespace ReapSeek.Domain.Ordering;

public sealed record OrderLine(
    Guid Id, long OfferId, long ProductId, long VariantId, string ProductTitle, string VariantLabel, string Sku, string? ImageUrl,
    Money UnitPrice, int Quantity)
{
    public Money Total => UnitPrice * Quantity;
}

public sealed class SellerPackage
{
    readonly List<OrderLine> _lines;

    internal SellerPackage(Guid id, long sellerId, string sellerName, IEnumerable<OrderLine> lines, Money shippingFee, PackageStatus status,
        string? trackingNumber, DateTime? shippedAt, DateTime? deliveredAt)
    {
        Id = id;
        SellerId = sellerId;
        SellerName = sellerName;
        _lines = lines.ToList();
        ShippingFee = shippingFee;
        Status = status;
        TrackingNumber = trackingNumber;
        ShippedAt = shippedAt;
        DeliveredAt = deliveredAt;
    }

    public Guid Id { get; }
    public long SellerId { get; }
    public string SellerName { get; }
    public IReadOnlyList<OrderLine> Lines => _lines;
    public Money ShippingFee { get; }
    public PackageStatus Status { get; private set; }
    public string? TrackingNumber { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public Money Subtotal => Money.Sum(_lines.Select(l => l.Total));
    public Money Total => Subtotal + ShippingFee;

    internal void MoveTo(PackageStatus next)
    {
        if (!OrderStatusRules.CanMove(Status, next))
            throw new DomainException("package.transition", $"A package cannot move from {OrderStatusRules.Name(Status)} to {OrderStatusRules.Name(next)}");
        Status = next;
    }

    public void Ship(string trackingNumber, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber)) throw new DomainException("package.tracking", "A tracking number is required");
        MoveTo(PackageStatus.Shipped);
        TrackingNumber = trackingNumber.Trim();
        ShippedAt = now;
    }

    public void Deliver(DateTime now)
    {
        MoveTo(PackageStatus.Delivered);
        DeliveredAt = now;
    }

    public static SellerPackage Restore(Guid id, long sellerId, string sellerName, IEnumerable<OrderLine> lines, Money shippingFee, PackageStatus status,
        string? trackingNumber, DateTime? shippedAt, DateTime? deliveredAt) =>
        new(id, sellerId, sellerName, lines, shippingFee, status, trackingNumber, shippedAt, deliveredAt);
}

public sealed class Order : AggregateRoot
{
    public const int MaxLines = 50;
    public const int MaxQuantityPerLine = 10;
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);

    readonly List<SellerPackage> _packages;

    Order(Guid id, string number, long customerId, ShippingAddress address, IEnumerable<SellerPackage> packages, OrderStatus status,
        DateTime placedAt, DateTime? paidAt, string? paymentReference, string? closeReason)
    {
        Id = id;
        Number = number;
        CustomerId = customerId;
        Address = address;
        _packages = packages.ToList();
        Status = status;
        PlacedAt = placedAt;
        PaidAt = paidAt;
        PaymentReference = paymentReference;
        CloseReason = closeReason;
    }

    public Guid Id { get; }
    public string Number { get; }
    public long CustomerId { get; }
    public ShippingAddress Address { get; }
    public IReadOnlyList<SellerPackage> Packages => _packages;
    public OrderStatus Status { get; private set; }
    public DateTime PlacedAt { get; }
    public DateTime? PaidAt { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? CloseReason { get; private set; }

    public IEnumerable<OrderLine> Lines => _packages.SelectMany(p => p.Lines);
    public Money Subtotal => Money.Sum(_packages.Select(p => p.Subtotal));
    public Money ShippingTotal => Money.Sum(_packages.Select(p => p.ShippingFee));
    public Money Total => Subtotal + ShippingTotal;
    public DateTime PaymentDeadline => PlacedAt + PaymentWindow;

    public static Order Place(Guid id, string number, long customerId, ShippingAddress address, IReadOnlyList<OrderItem> items,
        ShippingPolicy shipping, Func<Guid> newId, DateTime now)
    {
        if (items.Count == 0) throw new DomainException("order.empty", "An order needs at least one item");
        if (items.Count > MaxLines) throw new DomainException("order.max_lines", $"An order can have at most {MaxLines} items");
        if (items.Select(i => i.OfferId).Distinct().Count() != items.Count) throw new DomainException("order.duplicate_offer", "An offer can appear only once");
        foreach (var item in items)
        {
            if (item.Quantity is < 1 or > MaxQuantityPerLine)
                throw new DomainException("order.quantity", $"Quantity must be between 1 and {MaxQuantityPerLine}");
            if (item.UnitPrice.IsZero) throw new DomainException("order.price", "Items must have a price");
        }
        if (items.Select(i => i.UnitPrice.Currency).Distinct().Count() > 1) throw new DomainException("order.currency", "All items must use one currency");

        var packages = items
            .GroupBy(i => (i.SellerId, i.SellerName))
            .OrderBy(g => g.Key.SellerName, StringComparer.Ordinal)
            .Select(g =>
            {
                var lines = g.Select(i => new OrderLine(newId(), i.OfferId, i.ProductId, i.VariantId, i.ProductTitle, i.VariantLabel, i.Sku, i.ImageUrl,
                    i.UnitPrice, i.Quantity)).ToList();
                var subtotal = Money.Sum(lines.Select(l => l.Total), lines[0].UnitPrice.Currency);
                return new SellerPackage(newId(), g.Key.SellerId, g.Key.SellerName, lines, shipping.FeeFor(subtotal), PackageStatus.AwaitingPayment, null, null, null);
            })
            .ToList();

        var order = new Order(id, number, customerId, address, packages, OrderStatus.PendingPayment, now, null, null, null);
        order.Raise(new OrderPlaced(id, number, customerId, now));
        return order;
    }

    public static Order Restore(Guid id, string number, long customerId, ShippingAddress address, IEnumerable<SellerPackage> packages,
        OrderStatus status, DateTime placedAt, DateTime? paidAt, string? paymentReference, string? closeReason) =>
        new(id, number, customerId, address, packages, status, placedAt, paidAt, paymentReference, closeReason);

    public void AttachPayment(string paymentReference)
    {
        EnsureStatus(OrderStatus.PendingPayment, "attach a payment");
        if (string.IsNullOrWhiteSpace(paymentReference)) throw new DomainException("order.payment_reference", "A payment reference is required");
        PaymentReference = paymentReference;
    }

    public bool MarkPaid(string paymentReference, Money amount, DateTime now)
    {
        if (Status == OrderStatus.Paid && paymentReference == PaymentReference) return false;
        EnsureStatus(OrderStatus.PendingPayment, "be paid");
        if (PaymentReference is not null && paymentReference != PaymentReference)
            throw new DomainException("order.payment_mismatch", "The payment does not belong to this order");
        if (amount != Total) throw new DomainException("order.amount_mismatch", $"Paid {amount} but the order total is {Total}");
        Move(OrderStatus.Paid);
        PaymentReference = paymentReference;
        PaidAt = now;
        foreach (var p in _packages) p.MoveTo(PackageStatus.Preparing);
        Raise(new OrderPaid(Id, Number, CustomerId, now));
        return true;
    }

    public bool MarkPaymentFailed(string reason, DateTime now)
    {
        if (Status == OrderStatus.PaymentFailed) return false;
        EnsureStatus(OrderStatus.PendingPayment, "fail its payment");
        Close(OrderStatus.PaymentFailed, reason);
        Raise(new OrderPaymentFailed(Id, Number, CustomerId, CloseReason!, now));
        return true;
    }

    public bool Cancel(string reason, DateTime now)
    {
        if (Status == OrderStatus.Cancelled) return false;
        if (Status == OrderStatus.Paid) throw new DomainException("order.cancel_paid", "Paid orders are cancelled through a refund");
        EnsureStatus(OrderStatus.PendingPayment, "be cancelled");
        Close(OrderStatus.Cancelled, reason);
        Raise(new OrderCancelled(Id, Number, CustomerId, CloseReason!, now));
        return true;
    }

    public bool IsPaymentOverdue(DateTime now) => Status == OrderStatus.PendingPayment && now > PaymentDeadline;

    void Close(OrderStatus status, string reason)
    {
        Move(status);
        CloseReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        foreach (var p in _packages) p.MoveTo(PackageStatus.Cancelled);
    }

    void Move(OrderStatus next)
    {
        if (!OrderStatusRules.CanMove(Status, next))
            throw new DomainException("order.transition", $"An order cannot move from {OrderStatusRules.Name(Status)} to {OrderStatusRules.Name(next)}");
        Status = next;
    }

    void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException("order.status", $"An order that is {OrderStatusRules.Name(Status)} cannot {action}");
    }
}
