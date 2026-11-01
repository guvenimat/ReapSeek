using Dapper;
using Npgsql;
using ReapSeek.Api.Carts;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Payments;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Marketing;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Providers;
using ReapSeek.Shared;
using ReapSeek.Shared.Marketing;
using ReapSeek.Shared.Observability;
using ReapSeek.Shared.Ordering;

namespace ReapSeek.Api.Checkout;

public sealed class CheckoutService(NpgsqlDataSource db, IPaymentGateway payments, IConfiguration cfg, TimeProvider clock, ILogger<CheckoutService> log)
{
    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _webhookUrl = cfg["Payments:WebhookUrl"] ?? "http://localhost:8100/api/v1/payments/sandbox/webhook";

    public async Task<PlaceOrderResponse> PlaceOrderAsync(UserInfo customer, PlaceOrderRequest req, CancellationToken ct)
    {
        Ensure.That(req.Address is not null, "address is required");
        Ensure.That(req.ExpectedTotal is >= 0, "expected_total is required");
        var a = req.Address!;
        var address = ShippingAddress.Create(a.FullName, a.Phone, a.City, a.District, a.Line, a.PostalCode);
        var now = clock.GetUtcNow().UtcDateTime;

        Order order;
        await using (var conn = await db.OpenConnectionAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            if (await OrderStore.LockPendingCountAsync(conn, tx, customer.Id) >= Order.MaxPendingPerCustomer)
                throw new ConflictException($"You already have {Order.MaxPendingPerCustomer} orders waiting for payment. Pay or cancel one of them first.", "too_many_pending");
            var cart = await CartRepository.FindAsync(conn, tx, new CartOwner(customer.Id, null));
            if (cart is null || cart.Lines.Count == 0) throw new ConflictException("Your cart is empty", "cart_empty");
            var offers = await CartRepository.OffersAsync(conn, tx, cart.Lines.Select(l => l.OfferId).ToArray(), forUpdate: true);
            var view = CartPricing.Build(cart, offers, ShippingPolicy.Standard);
            if (!view.CanCheckout) throw new ConflictException("Some items in your cart are unavailable or low on stock", "cart_needs_attention");
            OrderDiscount? discount = null;
            long? couponId = null;
            if (req.CouponCode?.Trim() is { Length: > 0 } code)
            {
                var found = await CouponStore.FindAsync(conn, tx, code, forUpdate: true) ?? throw new DomainException("coupon.not_found", "This code is not valid");
                couponId = found.Id;
                discount = new OrderDiscount(found.Coupon.Code, found.Coupon.DiscountFor(Money.Of(view.Subtotal), customer.Id, now));
            }
            var expected = view.Total - (discount?.Amount.Amount ?? 0);
            if (expected != req.ExpectedTotal)
                throw new ConflictException($"Prices changed while you were checking out. The new total is {expected:0.00} {view.Currency}.", "price_changed");

            var byId = offers.ToDictionary(o => o.OfferId);
            var items = cart.Lines.Select(l =>
            {
                var o = byId[l.OfferId];
                return new OrderItem(o.OfferId, o.ProductId, o.VariantId, o.SellerId, o.SellerName, o.ProductTitle, CartPricing.VariantLabel(o.Options),
                    o.Sku, o.ImageUrl, Money.Of(o.Price), l.Quantity);
            }).ToList();

            var number = await OrderStore.NextNumberAsync(conn, tx, now);
            order = Order.Place(Guid.CreateVersion7(), number, customer.Id, address, items, ShippingPolicy.Standard, Guid.CreateVersion7, now, discount);
            if (order.Total.Amount != expected) throw new InvalidOperationException("Order total differs from the cart total");
            await OrderStore.ReserveStockAsync(conn, tx, items.Select(i => (i.OfferId, i.Quantity)));
            await OrderStore.InsertAsync(conn, tx, order);
            if (couponId is { } id) await CouponStore.RedeemAsync(conn, tx, id, order.Id, now);
            await SaveAddressAsync(conn, tx, customer.Id, address);
            await tx.CommitAsync(ct);
        }

        PaymentSession payment;
        try
        {
            payment = await payments.CreatePaymentAsync(new NewPayment(order.Total.Amount, order.Total.Currency, order.Number,
                $"ReapSeek order {order.Number}", $"{_storefrontUrl}/checkout/complete?order={order.Number}", _webhookUrl), ct);
        }
        catch (PaymentProviderException ex)
        {
            log.LogError("Could not start the payment for {Order}: {Message}", order.Number, ex.Message);
            await CloseAsync(order.Id, "payment_provider_unavailable", ct);
            throw;
        }

        await using (var conn = await db.OpenConnectionAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var current = await OrderStore.LoadAsync(conn, tx, order.Id) ?? throw new InvalidOperationException("Order disappeared");
            current.AttachPayment(payment.Id);
            await OrderStore.SaveAsync(conn, tx, current);
            await PaymentRepository.InsertAsync(conn, tx, Guid.CreateVersion7(), order.Id, payments.Name, payment.Id, payment.Amount,
                payment.Currency, payment.RedirectUrl);
            await tx.CommitAsync(ct);
        }
        Telemetry.OrdersPlaced.Add(1);
        log.LogInformation("Placed order {Order} for {Total}", order.Number, order.Total);
        return new PlaceOrderResponse(order.Number, payment.RedirectUrl, order.Total.Amount, order.Total.Currency);
    }

    public async Task<CouponPreview> PreviewCouponAsync(UserInfo customer, string? code, CancellationToken ct)
    {
        var text = Ensure.NotBlank(code, "code", 40);
        await using var conn = await db.OpenConnectionAsync(ct);
        var cart = await CartRepository.FindAsync(conn, null, new CartOwner(customer.Id, null));
        if (cart is null || cart.Lines.Count == 0) throw new ConflictException("Your cart is empty", "cart_empty");
        var offers = await CartRepository.OffersAsync(conn, null, cart.Lines.Select(l => l.OfferId).ToArray(), forUpdate: false);
        var view = CartPricing.Build(cart, offers, ShippingPolicy.Standard);
        var found = await CouponStore.FindAsync(conn, null, text, forUpdate: false) ?? throw new DomainException("coupon.not_found", "This code is not valid");
        var discount = found.Coupon.DiscountFor(Money.Of(view.Subtotal), customer.Id, clock.GetUtcNow().UtcDateTime);
        return new CouponPreview(found.Coupon.Code, discount.Amount, view.Total - discount.Amount, found.Coupon.ExpiresAt);
    }

    public async Task<List<SavedAddress>> AddressesAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<SavedAddress>("""
            SELECT id, full_name, phone, city, district, line, postal_code, last_used_at
            FROM addresses WHERE customer_id = @CustomerId ORDER BY last_used_at DESC LIMIT 10
            """, new { CustomerId = customerId })).AsList();
    }

    async Task CloseAsync(Guid orderId, string reason, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var order = await OrderStore.LoadAsync(conn, tx, orderId);
        if (order is not null && order.MarkPaymentFailed(reason, clock.GetUtcNow().UtcDateTime)) await OrderStore.SaveAsync(conn, tx, order);
        await tx.CommitAsync(ct);
    }

    static Task SaveAddressAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long customerId, ShippingAddress a) =>
        conn.ExecuteAsync("""
            INSERT INTO addresses (customer_id, full_name, phone, city, district, line, postal_code)
            VALUES (@CustomerId, @FullName, @Phone, @City, @District, @Line, @PostalCode)
            ON CONFLICT (customer_id, full_name, phone, city, district, line, postal_code) DO UPDATE SET last_used_at = now()
            """, new { CustomerId = customerId, a.FullName, a.Phone, a.City, a.District, a.Line, a.PostalCode }, tx);
}
