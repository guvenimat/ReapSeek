using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ledger;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;

namespace ReapSeek.UnitTests;

public class ReturnTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    static readonly ShippingAddress Address = ShippingAddress.Create("Ayse Yilmaz", "+90 532 123 45 67", "Izmir", "Konak", "Ataturk Cd. 12", "35210");
    static readonly Dictionary<Guid, int> None = [];

    static OrderItem Item(long offerId, long sellerId, decimal price, int quantity) =>
        new(offerId, offerId * 10, offerId * 100, sellerId, $"Seller {sellerId}", $"Product {offerId}", "Size: M", $"SKU{offerId}", null, Money.Of(price), quantity);

    static (Order Order, SellerPackage Package) Delivered(params OrderItem[] items)
    {
        var order = Order.Place(Guid.NewGuid(), "RS261030000001", 7, Address, items, ShippingPolicy.Standard, Guid.NewGuid, Now);
        order.AttachPayment("pay_1");
        order.MarkPaid("pay_1", order.Total, Now);
        var package = order.Packages[0];
        order.ShipPackage(package.Id, package.SellerId, "SC1", Now);
        order.DeliverPackage(package.Id, Now.AddDays(1));
        return (order, package);
    }

    static ReturnRequest Request(Order order, SellerPackage package, int quantity = 1, Dictionary<Guid, int>? returned = null, DateTime? at = null, string reason = "wrong_size") =>
        ReturnRequest.Request(Guid.NewGuid(), "RT261030000001", order, package.Id, 7, [new ReturnItem(package.Lines[0].Id, quantity)], reason, " too small ", 0.15m,
            returned ?? None, at ?? Now.AddDays(2));

    [Fact]
    public void A_customer_returns_part_of_a_delivered_package_within_seven_days()
    {
        var (order, package) = Delivered(Item(1, 1, 200m, 3));
        var request = Request(order, package, 2);

        Assert.Equal(ReturnStatus.Requested, request.Status);
        Assert.Equal(Money.Of(400m), request.Total);
        Assert.Equal("too small", request.Comment);
        Assert.Equal(1, request.SellerId);
        Assert.IsType<ReturnRequested>(Assert.Single(request.DomainEvents));
    }

    [Fact]
    public void Returns_are_refused_outside_the_rules()
    {
        var (order, package) = Delivered(Item(1, 1, 200m, 2));
        Assert.Equal("return.window_closed", Assert.Throws<DomainException>(() => Request(order, package, at: Now.AddDays(8).AddMinutes(1))).Code);
        Assert.Equal("return.quantity", Assert.Throws<DomainException>(() => Request(order, package, 3)).Code);
        Assert.Equal("return.quantity", Assert.Throws<DomainException>(() => Request(order, package, 1, new() { [package.Lines[0].Id] = 2 })).Code);
        Assert.Equal("return.reason", Assert.Throws<DomainException>(() => Request(order, package, reason: "bored")).Code);
        Assert.Equal("return.not_found", Assert.Throws<DomainException>(() =>
            ReturnRequest.Request(Guid.NewGuid(), "RT1", order, package.Id, 8, [new ReturnItem(package.Lines[0].Id, 1)], "damaged", null, 0.1m, None, Now)).Code);

        var shipped = Order.Place(Guid.NewGuid(), "RS2", 7, Address, [Item(2, 1, 50m, 1)], ShippingPolicy.Standard, Guid.NewGuid, Now);
        shipped.AttachPayment("p");
        shipped.MarkPaid("p", shipped.Total, Now);
        Assert.Equal("return.not_delivered", Assert.Throws<DomainException>(() => Request(shipped, shipped.Packages[0])).Code);
    }

    [Fact]
    public void Only_the_seller_decides_and_a_rejection_needs_a_note()
    {
        var (order, package) = Delivered(Item(1, 1, 200m, 1));
        var request = Request(order, package);

        Assert.Equal("return.not_found", Assert.Throws<DomainException>(() => request.Approve(2, "SC9", Now)).Code);
        Assert.Equal("return.note", Assert.Throws<DomainException>(() => request.Reject(1, "  ", Now)).Code);
        request.Reject(1, "Worn and washed", Now);
        Assert.Equal((ReturnStatus.Rejected, "Worn and washed"), (request.Status, request.RejectionNote));
        Assert.Equal("return.transition", Assert.Throws<DomainException>(() => request.Approve(1, "SC9", Now)).Code);
    }

    [Fact]
    public void An_approved_return_is_refunded_after_it_arrives_and_gives_back_the_commission()
    {
        var (order, package) = Delivered(Item(1, 1, 99.99m, 3));
        var request = Request(order, package, 3);
        request.Approve(1, "SC123", Now);
        Assert.Equal("return.transition", Assert.Throws<DomainException>(() => request.MarkRefunded("re_1", Now)).Code);

        Assert.True(request.MarkReceived(Now));
        Assert.False(request.MarkReceived(Now));
        var entries = request.MarkRefunded("re_1", Now);

        Assert.Equal(ReturnStatus.Refunded, request.Status);
        Assert.Equal([new LedgerEntry("refund", -299.97m), new LedgerEntry("commission_refund", 45.00m)], entries);
        Assert.IsType<ReturnRefunded>(request.DomainEvents[^1]);
    }
}
