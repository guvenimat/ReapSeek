using ReapSeek.Api.Carts;
using ReapSeek.Api.Payments;
using ReapSeek.Domain.Carts;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;
using ReapSeek.Sandbox.Payments;
using ReapSeek.Worker.Orders;

namespace ReapSeek.UnitTests;

public class CartPricingTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    static CartRepository.OfferRecord Offer(long id, long seller, decimal price, int stock, string status = "active", string sellerStatus = "active", string productStatus = "active") =>
        new(id, price, null, stock, status, 2, seller, $"Seller {seller}", sellerStatus, 4.5m, id * 10, $"product-{id}", $"Product {id}", productStatus,
            "Brand", id * 100, $"SKU{id}", """{"Size":"M","Color":"Black"}""", null);

    static Cart CartWith(params (long OfferId, int Quantity, decimal AddedPrice)[] lines)
    {
        var cart = Cart.ForCustomer(Guid.NewGuid(), 1);
        foreach (var l in lines) cart.Add(l.OfferId, l.Quantity, Money.Of(l.AddedPrice), Now);
        return cart;
    }

    [Fact]
    public void Lines_are_grouped_by_seller_with_their_own_shipping()
    {
        var view = CartPricing.Build(CartWith((1, 2, 300m), (2, 1, 100m)), [Offer(1, 1, 300m, 10), Offer(2, 2, 100m, 10)], ShippingPolicy.Standard);
        Assert.Equal(2, view.Packages.Count);
        Assert.Equal(0m, view.Packages.Single(p => p.SellerId == 1).ShippingFee);
        var small = view.Packages.Single(p => p.SellerId == 2);
        Assert.Equal(39.90m, small.ShippingFee);
        Assert.Equal(400m, small.FreeShippingRemaining);
        Assert.Equal(739.90m, view.Total);
        Assert.Equal(3, view.ItemCount);
        Assert.True(view.CanCheckout);
    }

    [Fact]
    public void Price_changes_are_flagged_but_do_not_block_checkout()
    {
        var view = CartPricing.Build(CartWith((1, 1, 300m)), [Offer(1, 1, 280m, 10)], ShippingPolicy.Standard);
        var line = view.Packages[0].Lines[0];
        Assert.Equal(CartLineStatus.PriceChanged, line.Status);
        Assert.Equal(280m, line.UnitPrice);
        Assert.True(view.CanCheckout);
    }

    [Fact]
    public void Low_stock_and_unavailable_items_block_checkout()
    {
        var low = CartPricing.Build(CartWith((1, 3, 100m)), [Offer(1, 1, 100m, 2)], ShippingPolicy.Standard);
        Assert.Equal(CartLineStatus.LowStock, low.Packages[0].Lines[0].Status);
        Assert.False(low.CanCheckout);

        var suspended = CartPricing.Build(CartWith((1, 1, 100m)), [Offer(1, 1, 100m, 5, sellerStatus: "suspended")], ShippingPolicy.Standard);
        Assert.Equal(CartLineStatus.Unavailable, suspended.Packages[0].Lines[0].Status);
        Assert.Equal(0m, suspended.Total);
        Assert.False(suspended.CanCheckout);
        Assert.NotEmpty(suspended.Warnings);
    }

    [Fact]
    public void Removed_offers_are_dropped_with_a_warning()
    {
        var view = CartPricing.Build(CartWith((1, 1, 100m), (2, 1, 50m)), [Offer(1, 1, 100m, 5)], ShippingPolicy.Standard);
        Assert.Single(view.Packages.SelectMany(p => p.Lines));
        Assert.Contains(view.Warnings, w => w.Contains("no longer sold"));
    }

    [Fact]
    public void Variant_labels_list_color_before_size() =>
        Assert.Equal("Color: Black · Size: M", CartPricing.VariantLabel("""{"Size":"M","Color":"Black"}"""));
}

public class PaymentTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    const string Secret = "whsec_test";
    const string Body = """{"id":"evt_1","type":"payment.succeeded"}""";

    [Fact]
    public void Signatures_from_the_sandbox_are_accepted()
    {
        var header = WebhookSignature.Create(Secret, Now.ToUnixTimeSeconds(), Body);
        Assert.True(WebhookVerifier.IsValid(header, Body, Secret, Now));
        Assert.True(WebhookVerifier.IsValid(header, Body, Secret, Now.AddMinutes(4)));
    }

    [Fact]
    public void Tampered_stale_or_malformed_signatures_are_rejected()
    {
        var header = WebhookSignature.Create(Secret, Now.ToUnixTimeSeconds(), Body);
        Assert.False(WebhookVerifier.IsValid(header, Body.Replace("succeeded", "failed"), Secret, Now));
        Assert.False(WebhookVerifier.IsValid(header, Body, "whsec_other", Now));
        Assert.False(WebhookVerifier.IsValid(header, Body, Secret, Now.AddMinutes(6)));
        Assert.False(WebhookVerifier.IsValid(null, Body, Secret, Now));
        Assert.False(WebhookVerifier.IsValid("t=abc,v1=00", Body, Secret, Now));
        Assert.False(WebhookVerifier.IsValid($"t={Now.ToUnixTimeSeconds()},v1=not-hex", Body, Secret, Now));
        Assert.False(WebhookVerifier.IsValid(header, Body, "", Now));
    }

    [Theory]
    [InlineData("4242424242424242", CardOutcome.RequiresAuthentication)]
    [InlineData("4000000000000002", CardOutcome.Declined)]
    [InlineData("4000000000009995", CardOutcome.InsufficientFunds)]
    [InlineData("4242424242424241", CardOutcome.Invalid)]
    [InlineData("4242", CardOutcome.Invalid)]
    public void Test_cards_drive_the_outcome(string card, CardOutcome expected) =>
        Assert.Equal(expected, CardRules.Check(card, "12/30", "123", Now.UtcDateTime).Outcome);

    [Fact]
    public void Expired_cards_and_bad_security_codes_are_invalid()
    {
        Assert.Equal(CardOutcome.Invalid, CardRules.Check("4242424242424242", "09/26", "123", Now.UtcDateTime).Outcome);
        Assert.Equal(CardOutcome.RequiresAuthentication, CardRules.Check("4242424242424242", "10/26", "123", Now.UtcDateTime).Outcome);
        Assert.Equal(CardOutcome.Invalid, CardRules.Check("4242424242424242", "13/30", "123", Now.UtcDateTime).Outcome);
        Assert.Equal(CardOutcome.Invalid, CardRules.Check("4242424242424242", "12/30", "12", Now.UtcDateTime).Outcome);
    }

    [Fact]
    public void Card_brands_and_digits()
    {
        Assert.Equal("4242424242424242", CardRules.Digits("4242 4242-4242 4242"));
        Assert.Equal("Visa", CardRules.Brand("4242424242424242"));
        Assert.Equal("Mastercard", CardRules.Brand("5555555555554444"));
        Assert.Equal("Troy", CardRules.Brand("9792000000000001"));
    }

    [Fact]
    public void Purchase_events_are_one_per_line_with_stable_ids()
    {
        var now = Now.UtcDateTime;
        var address = ShippingAddress.Create("Ayse", "5321234567", "Izmir", "Konak", "Street 1", "35210");
        var order = Order.Place(Guid.NewGuid(), "RS261030000009", 42, address,
        [
            new OrderItem(1, 10, 100, 7, "Seller", "Dress", "Size: M", "SKU1", null, Money.Of(250m), 2),
            new OrderItem(2, 20, 200, 8, "Other", "Shoe", "Size: 40", "SKU2", null, Money.Of(600m), 1),
        ], ShippingPolicy.Standard, Guid.NewGuid, now);
        order.MarkPaid("pay_1", order.Total, now.AddMinutes(1));
        var events = PurchaseEvents.For(order);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("customer-42", e.DistinctId));
        Assert.All(events, e => Assert.Equal(now.AddMinutes(1), e.Timestamp));
        var dress = events.Single(e => e.Properties.ItemId == "10");
        Assert.Equal((250m, 2, 500m, "RS261030000009", 7L), (dress.Properties.Price, dress.Properties.Quantity, dress.Properties.Amount, dress.Properties.OrderId, dress.Properties.SellerId));
        Assert.Equal(events.Select(e => e.EventId), PurchaseEvents.For(order).Select(e => e.EventId));
    }
}
