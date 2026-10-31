using ReapSeek.Domain.Common;
using ReapSeek.Domain.Marketing;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;

namespace ReapSeek.UnitTests;

public class CouponTests
{
    static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    static Coupon Percent(decimal value, decimal min = 0, decimal? cap = null, long? owner = 7) =>
        new("RS-ABCDEFGH", owner, DiscountKind.Percent, value, Money.Of(min), cap is { } c ? Money.Of(c) : null, Now.AddDays(1), null);

    [Fact]
    public void A_percentage_is_rounded_and_capped()
    {
        Assert.Equal(Money.Of(30m), Percent(10).DiscountFor(Money.Of(300m), 7, Now));
        Assert.Equal(Money.Of(33.33m), Percent(33.33m).DiscountFor(Money.Of(100m), 7, Now));
        Assert.Equal(Money.Of(50m), Percent(20, cap: 50).DiscountFor(Money.Of(1000m), 7, Now));
    }

    [Fact]
    public void A_fixed_discount_never_exceeds_the_basket()
    {
        var coupon = new Coupon("RS-FIXED123", null, DiscountKind.Fixed, 100m, Money.Of(0), null, Now.AddDays(1), null);
        Assert.Equal(Money.Of(100m), coupon.DiscountFor(Money.Of(250m), 1, Now));
        Assert.Equal(Money.Of(60m), coupon.DiscountFor(Money.Of(60m), 1, Now));
    }

    [Fact]
    public void A_code_belongs_to_one_customer_and_works_once_before_it_expires()
    {
        Assert.Equal("coupon.not_found", Assert.Throws<DomainException>(() => Percent(10).DiscountFor(Money.Of(300m), 8, Now)).Code);
        Assert.Equal("coupon.used", Assert.Throws<DomainException>(() => (Percent(10) with { UsedOrderId = Guid.NewGuid() }).DiscountFor(Money.Of(300m), 7, Now)).Code);
        Assert.Equal("coupon.expired", Assert.Throws<DomainException>(() => Percent(10).DiscountFor(Money.Of(300m), 7, Now.AddDays(1))).Code);
        Assert.Equal("coupon.min_subtotal", Assert.Throws<DomainException>(() => Percent(10, min: 200).DiscountFor(Money.Of(150m), 7, Now)).Code);
    }

    [Fact]
    public void Campaign_terms_are_checked()
    {
        Assert.Equal("coupon.value", Assert.Throws<DomainException>(() => Coupon.ValidateTerms(DiscountKind.Percent, 95, 0, null)).Code);
        Assert.Equal("coupon.value", Assert.Throws<DomainException>(() => Coupon.ValidateTerms(DiscountKind.Fixed, 300, 200, null)).Code);
        Assert.Equal("coupon.max_discount", Assert.Throws<DomainException>(() => Coupon.ValidateTerms(DiscountKind.Percent, 10, 0, 0)).Code);
        Coupon.ValidateTerms(DiscountKind.Fixed, 50, 200, null);
        Assert.Matches("^RS-[A-Z2-9]{8}$", Coupon.NewCode("RS"));
    }

    [Fact]
    public void An_order_subtracts_the_discount_from_the_total()
    {
        var address = ShippingAddress.Create("Ada Kaya", "01000000001", "Izmir", "Konak", "Ataturk Cd. 1", "35210");
        var item = new OrderItem(1, 10, 100, 1, "Seller", "Dress", "M", "SKU1", null, Money.Of(150m), 2);
        var order = Order.Place(Guid.NewGuid(), "RS1", 7, address, [item], ShippingPolicy.Standard, Guid.NewGuid, Now, new OrderDiscount("RS-X", Money.Of(30m)));

        Assert.Equal(Money.Of(300m), order.Subtotal);
        Assert.Equal(Money.Of(309.90m), order.Total);
        Assert.Equal(Money.Of(300m), order.Packages[0].Subtotal);
        Assert.Equal("order.discount", Assert.Throws<DomainException>(() =>
            Order.Place(Guid.NewGuid(), "RS2", 7, address, [item], ShippingPolicy.Standard, Guid.NewGuid, Now, new OrderDiscount("RS-X", Money.Of(301m)))).Code);
    }

    [Fact]
    public void A_return_refunds_the_discounted_share()
    {
        Assert.Equal(Money.Of(135m), ReturnRequest.RefundFor(Money.Of(150m), Money.Of(300m), Money.Of(30m)));
        Assert.Equal(Money.Of(150m), ReturnRequest.RefundFor(Money.Of(150m), Money.Of(300m), Money.Of(0m)));
        Assert.Equal(Money.Of(66.66m), ReturnRequest.RefundFor(Money.Of(99.99m), Money.Of(99.99m), Money.Of(33.33m)));
    }
}
