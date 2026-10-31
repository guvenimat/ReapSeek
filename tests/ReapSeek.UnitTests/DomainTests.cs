using ReapSeek.Domain.Carts;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;

namespace ReapSeek.UnitTests;

public class MoneyTests
{
    [Fact]
    public void Money_rounds_to_cents_and_adds_up()
    {
        Assert.Equal(10.01m, Money.Of(10.005m).Amount);
        Assert.Equal(Money.Of(29.97m), Money.Of(9.99m) * 3);
        Assert.Equal(Money.Of(15m), Money.Sum([Money.Of(5m), Money.Of(10m)]));
        Assert.True(Money.Of(500m) >= Money.Of(499.99m));
        Assert.Equal("1399.90 TRY", Money.Of(1399.9m).ToString());
    }

    [Fact]
    public void Money_rejects_negative_amounts_and_mixed_currencies()
    {
        Assert.Equal("money.negative", Assert.Throws<DomainException>(() => Money.Of(-1m)).Code);
        Assert.Equal("money.currency_mismatch", Assert.Throws<DomainException>(() => Money.Of(1m) + Money.Of(1m, "EUR")).Code);
        Assert.Equal("money.currency", Assert.Throws<DomainException>(() => Money.Of(1m, "LIRA")).Code);
    }
}

public class CartTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    static Cart NewCart() => Cart.ForCustomer(Guid.NewGuid(), 7);

    [Fact]
    public void Adding_the_same_offer_increases_the_quantity()
    {
        var cart = NewCart();
        cart.Add(1, 2, Money.Of(100m), Now);
        cart.Add(1, 3, Money.Of(90m), Now);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(Money.Of(100m), line.AddedPrice);
        Assert.Equal(5, cart.ItemCount);
    }

    [Fact]
    public void Quantity_limits_are_enforced()
    {
        var cart = NewCart();
        cart.Add(1, 9, Money.Of(100m), Now);
        Assert.Equal("cart.max_quantity", Assert.Throws<DomainException>(() => cart.Add(1, 2, Money.Of(100m), Now)).Code);
        Assert.Equal("cart.quantity", Assert.Throws<DomainException>(() => cart.Add(2, 0, Money.Of(100m), Now)).Code);
        Assert.Equal("cart.quantity", Assert.Throws<DomainException>(() => cart.SetQuantity(1, 11)).Code);
        Assert.Equal("cart.line_not_found", Assert.Throws<DomainException>(() => cart.SetQuantity(99, 1)).Code);
    }

    [Fact]
    public void Setting_quantity_to_zero_removes_the_line()
    {
        var cart = NewCart();
        cart.Add(1, 2, Money.Of(100m), Now);
        cart.SetQuantity(1, 0);
        Assert.Empty(cart.Lines);
        Assert.False(cart.Remove(1));
    }

    [Fact]
    public void A_cart_holds_at_most_fifty_lines()
    {
        var cart = NewCart();
        for (var i = 0; i < Cart.MaxLines; i++) cart.Add(i + 1, 1, Money.Of(10m), Now);
        Assert.Equal("cart.max_lines", Assert.Throws<DomainException>(() => cart.Add(999, 1, Money.Of(10m), Now)).Code);
    }

    [Fact]
    public void Guest_cart_merges_into_the_customer_cart_with_capped_quantities()
    {
        var customer = NewCart();
        customer.Add(1, 8, Money.Of(100m), Now);
        var guest = Cart.ForGuest(Guid.NewGuid(), "token");
        guest.Add(1, 5, Money.Of(100m), Now);
        guest.Add(2, 1, Money.Of(50m), Now);
        customer.MergeFrom(guest);
        Assert.Equal(10, customer.Find(1)!.Quantity);
        Assert.Equal(1, customer.Find(2)!.Quantity);
    }

    [Fact]
    public void Purchased_quantities_leave_the_cart()
    {
        var cart = NewCart();
        cart.Add(1, 3, Money.Of(100m), Now);
        cart.Add(2, 1, Money.Of(50m), Now);
        cart.RemovePurchased([(1, 2), (2, 1), (3, 1)]);
        Assert.Equal(1, Assert.Single(cart.Lines).Quantity);
    }
}

public class OrderTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    static readonly ShippingAddress Address = ShippingAddress.Create("Ayse Yilmaz", "+90 100 000 00 02", "Izmir", "Konak", "Ataturk Cd. 12", "35210");

    static OrderItem Item(long offerId, long sellerId, decimal price, int quantity) =>
        new(offerId, offerId * 10, offerId * 100, sellerId, $"Seller {sellerId}", $"Product {offerId}", "Size: M", $"SKU{offerId}", null, Money.Of(price), quantity);

    static Order Place(params OrderItem[] items) =>
        Order.Place(Guid.NewGuid(), "RS261030000001", 7, Address, items, ShippingPolicy.Standard, Guid.NewGuid, Now);

    [Fact]
    public void Items_are_split_into_seller_packages_with_their_own_shipping()
    {
        var order = Place(Item(1, 1, 300m, 1), Item(2, 1, 250m, 1), Item(3, 2, 120m, 2));
        Assert.Equal(2, order.Packages.Count);
        var big = order.Packages.Single(p => p.SellerId == 1);
        var small = order.Packages.Single(p => p.SellerId == 2);
        Assert.Equal(Money.Of(550m), big.Subtotal);
        Assert.True(big.ShippingFee.IsZero);
        Assert.Equal(Money.Of(240m), small.Subtotal);
        Assert.Equal(Money.Of(39.90m), small.ShippingFee);
        Assert.Equal(Money.Of(829.90m), order.Total);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.All(order.Packages, p => Assert.Equal(PackageStatus.AwaitingPayment, p.Status));
        Assert.IsType<OrderPlaced>(Assert.Single(order.DomainEvents));
    }

    [Fact]
    public void Invalid_orders_are_rejected()
    {
        Assert.Equal("order.empty", Assert.Throws<DomainException>(() => Place()).Code);
        Assert.Equal("order.quantity", Assert.Throws<DomainException>(() => Place(Item(1, 1, 10m, 11))).Code);
        Assert.Equal("order.duplicate_offer", Assert.Throws<DomainException>(() => Place(Item(1, 1, 10m, 1), Item(1, 1, 10m, 2))).Code);
        Assert.Equal("order.price", Assert.Throws<DomainException>(() => Place(Item(1, 1, 0m, 1))).Code);
    }

    [Fact]
    public void Paying_the_exact_total_moves_packages_to_preparing()
    {
        var order = Place(Item(1, 1, 600m, 1));
        order.AttachPayment("pay_1");
        order.ClearDomainEvents();
        Assert.True(order.MarkPaid("pay_1", Money.Of(600m), Now.AddMinutes(2)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Now.AddMinutes(2), order.PaidAt);
        Assert.Equal(PackageStatus.Preparing, order.Packages[0].Status);
        Assert.IsType<OrderPaid>(Assert.Single(order.DomainEvents));
    }

    [Fact]
    public void Repeated_payment_notifications_are_ignored()
    {
        var order = Place(Item(1, 1, 600m, 1));
        order.AttachPayment("pay_1");
        order.MarkPaid("pay_1", Money.Of(600m), Now);
        order.ClearDomainEvents();
        Assert.False(order.MarkPaid("pay_1", Money.Of(600m), Now));
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void A_wrong_amount_or_payment_is_rejected()
    {
        var order = Place(Item(1, 1, 600m, 1));
        order.AttachPayment("pay_1");
        Assert.Equal("order.amount_mismatch", Assert.Throws<DomainException>(() => order.MarkPaid("pay_1", Money.Of(599.99m), Now)).Code);
        Assert.Equal("order.payment_mismatch", Assert.Throws<DomainException>(() => order.MarkPaid("pay_2", Money.Of(600m), Now)).Code);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void Failed_payment_closes_the_order_and_releases_stock()
    {
        var order = Place(Item(1, 1, 100m, 1));
        order.ClearDomainEvents();
        Assert.True(order.MarkPaymentFailed("card_declined", Now));
        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal("card_declined", order.CloseReason);
        Assert.All(order.Packages, p => Assert.Equal(PackageStatus.Cancelled, p.Status));
        Assert.IsAssignableFrom<IReleasesStock>(Assert.Single(order.DomainEvents));
        Assert.False(order.MarkPaymentFailed("card_declined", Now));
        Assert.Equal("order.status", Assert.Throws<DomainException>(() => order.MarkPaid("pay", Money.Of(100m), Now)).Code);
    }

    [Fact]
    public void Paid_orders_cannot_be_cancelled_without_a_refund()
    {
        var order = Place(Item(1, 1, 100m, 1));
        order.MarkPaid("pay_1", Money.Of(139.90m), Now);
        Assert.Equal("order.cancel_paid", Assert.Throws<DomainException>(() => order.Cancel("changed_mind", Now)).Code);
    }

    [Fact]
    public void Unpaid_orders_become_overdue_after_the_payment_window()
    {
        var order = Place(Item(1, 1, 100m, 1));
        Assert.False(order.IsPaymentOverdue(Now.AddMinutes(14)));
        Assert.True(order.IsPaymentOverdue(Now.AddMinutes(16)));
        Assert.True(order.Cancel("payment_timeout", Now.AddMinutes(16)));
        Assert.False(order.IsPaymentOverdue(Now.AddMinutes(30)));
    }

    [Fact]
    public void Packages_ship_and_deliver_only_after_payment()
    {
        var order = Place(Item(1, 1, 600m, 1));
        var package = order.Packages[0];
        Assert.Equal("package.transition", Assert.Throws<DomainException>(() => package.Ship("TRK1", Now)).Code);
        order.MarkPaid("pay_1", Money.Of(600m), Now);
        package.Ship("TRK1", Now.AddDays(1));
        package.Deliver(Now.AddDays(3));
        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.Equal("TRK1", package.TrackingNumber);
        Assert.Equal("package.transition", Assert.Throws<DomainException>(() => package.Ship("TRK2", Now)).Code);
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Paid, true)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.PendingPayment, false)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.Paid, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid, false)]
    public void Order_state_machine(OrderStatus from, OrderStatus to, bool allowed) =>
        Assert.Equal(allowed, OrderStatusRules.CanMove(from, to));

    [Fact]
    public void Status_names_round_trip()
    {
        Assert.All(Enum.GetValues<OrderStatus>(), s => Assert.Equal(s, OrderStatusRules.ParseOrder(OrderStatusRules.Name(s))));
        Assert.All(Enum.GetValues<PackageStatus>(), s => Assert.Equal(s, OrderStatusRules.ParsePackage(OrderStatusRules.Name(s))));
    }

    [Fact]
    public void Order_numbers_are_dated_and_padded() =>
        Assert.Equal("RS261030000042", OrderNumber.Format(Now, 42));

    [Theory]
    [InlineData("", "1000000003", "Izmir", "Konak", "Street 1", "35210", "address.invalid")]
    [InlineData("Ayse", "123", "Izmir", "Konak", "Street 1", "35210", "address.phone")]
    [InlineData("Ayse", "1000000003", "Izmir", "Konak", "Street 1", "3521", "address.postal_code")]
    public void Addresses_are_validated(string name, string phone, string city, string district, string line, string postal, string code) =>
        Assert.Equal(code, Assert.Throws<DomainException>(() => ShippingAddress.Create(name, phone, city, district, line, postal)).Code);

    [Fact]
    public void Phone_numbers_keep_only_digits() =>
        Assert.Equal("901000000002", Address.Phone);
}
