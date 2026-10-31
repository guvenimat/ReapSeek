using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ledger;
using ReapSeek.Domain.Ordering;

namespace ReapSeek.UnitTests;

public class LedgerTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    static readonly ShippingAddress Address = ShippingAddress.Create("Ayse Yilmaz", "+90 532 123 45 67", "Izmir", "Konak", "Ataturk Cd. 12", "35210");

    static OrderItem Item(long offerId, long sellerId, decimal price, int quantity) =>
        new(offerId, offerId * 10, offerId * 100, sellerId, $"Seller {sellerId}", $"Product {offerId}", "Size: M", $"SKU{offerId}", null, Money.Of(price), quantity);

    static Order Paid(params OrderItem[] items)
    {
        var order = Order.Place(Guid.NewGuid(), "RS261030000001", 7, Address, items, ShippingPolicy.Standard, Guid.NewGuid, Now);
        order.AttachPayment("pay_1");
        order.MarkPaid("pay_1", order.Total, Now);
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public void A_seller_ships_only_their_own_package_of_a_paid_order()
    {
        var order = Paid(Item(1, 1, 300m, 1), Item(2, 2, 120m, 1));
        var mine = order.Packages.Single(p => p.SellerId == 1);
        var theirs = order.Packages.Single(p => p.SellerId == 2);

        Assert.Equal("package.not_found", Assert.Throws<DomainException>(() => order.ShipPackage(theirs.Id, 1, "SC1", Now)).Code);
        order.ShipPackage(mine.Id, 1, " SC123 ", Now);

        Assert.Equal((PackageStatus.Shipped, "SC123", (DateTime?)Now), (mine.Status, mine.TrackingNumber, mine.ShippedAt));
        Assert.Equal(PackageStatus.Preparing, theirs.Status);
        var shipped = Assert.IsType<PackageShipped>(Assert.Single(order.DomainEvents));
        Assert.Equal((mine.Id, "SC123"), (shipped.PackageId, shipped.TrackingNumber));
        Assert.Equal("package.transition", Assert.Throws<DomainException>(() => order.ShipPackage(mine.Id, 1, "SC9", Now)).Code);
    }

    [Fact]
    public void Unpaid_orders_cannot_be_shipped_and_a_package_is_delivered_only_after_shipping()
    {
        var unpaid = Order.Place(Guid.NewGuid(), "RS261030000002", 7, Address, [Item(1, 1, 300m, 1)], ShippingPolicy.Standard, Guid.NewGuid, Now);
        Assert.Equal("order.status", Assert.Throws<DomainException>(() => unpaid.ShipPackage(unpaid.Packages[0].Id, 1, "SC1", Now)).Code);

        var order = Paid(Item(1, 1, 300m, 1));
        var package = order.Packages[0];
        Assert.Equal("package.transition", Assert.Throws<DomainException>(() => order.DeliverPackage(package.Id, Now)).Code);
        order.ShipPackage(package.Id, 1, "SC1", Now);
        order.DeliverPackage(package.Id, Now.AddDays(2));
        Assert.Equal((PackageStatus.Delivered, (DateTime?)Now.AddDays(2)), (package.Status, package.DeliveredAt));
        Assert.IsType<PackageDelivered>(order.DomainEvents[^1]);
    }

    [Fact]
    public void Settlement_pays_the_sale_and_shipping_minus_commission()
    {
        var order = Paid(Item(1, 1, 99.99m, 3));
        var package = order.Packages[0];
        order.ShipPackage(package.Id, 1, "SC1", Now);
        order.DeliverPackage(package.Id, Now);

        var entries = Settlement.ForPackage(package, 0.15m);

        Assert.Equal([new LedgerEntry("sale", 299.97m), new LedgerEntry("shipping", 39.90m), new LedgerEntry("commission", -45.00m)], entries);
        Assert.Equal(294.87m, entries.Sum(e => e.Amount));
    }

    [Fact]
    public void Free_shipping_and_zero_commission_leave_out_empty_entries()
    {
        var order = Paid(Item(1, 1, 600m, 1));
        var package = order.Packages[0];
        order.ShipPackage(package.Id, 1, "SC1", Now);
        order.DeliverPackage(package.Id, Now);

        Assert.Equal([new LedgerEntry("sale", 600m)], Settlement.ForPackage(package, 0m));
    }

    [Fact]
    public void Only_delivered_packages_with_a_valid_rate_are_settled()
    {
        var order = Paid(Item(1, 1, 600m, 1));
        var package = order.Packages[0];
        Assert.Equal("ledger.not_delivered", Assert.Throws<DomainException>(() => Settlement.ForPackage(package, 0.1m)).Code);
        order.ShipPackage(package.Id, 1, "SC1", Now);
        order.DeliverPackage(package.Id, Now);
        Assert.Equal("ledger.commission", Assert.Throws<DomainException>(() => Settlement.ForPackage(package, 0.6m)).Code);
        Assert.Equal(Now.AddDays(7), Settlement.AvailableAt(Now, Settlement.DefaultHold));
    }

    [Theory]
    [InlineData("2026-11-02T10:00:00Z", "2026-11-01T21:00:00Z")]
    [InlineData("2026-11-01T20:59:59Z", "2026-10-25T21:00:00Z")]
    [InlineData("2026-11-01T21:00:00Z", "2026-11-01T21:00:00Z")]
    [InlineData("2026-11-05T12:00:00Z", "2026-11-01T21:00:00Z")]
    public void A_payout_period_ends_on_Monday_at_midnight_in_Turkey(string now, string expected) =>
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(),
            PayoutPeriod.CurrentPeriodEnd(DateTime.Parse(now).ToUniversalTime(), PayoutPeriod.Turkey));
}
