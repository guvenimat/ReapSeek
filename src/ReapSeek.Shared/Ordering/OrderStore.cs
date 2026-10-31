using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Shared.Ordering;

public sealed class InsufficientStockException(long offerId) : Exception($"Offer {offerId} does not have enough stock")
{
    public long OfferId { get; } = offerId;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ShippingAddress))]
public sealed partial class OrderingJsonContext : JsonSerializerContext;

public static class OrderStore
{
    public sealed record OrderRecord(
        Guid Id, string Number, long CustomerId, string Status, string Currency, string ShippingAddress, DateTime PlacedAt,
        DateTime? PaidAt, string? PaymentReference, string? CloseReason);
    public sealed record OrderInsertRecord(
        Guid Id, string Number, long CustomerId, string Status, string Currency, decimal Subtotal, decimal ShippingTotal, decimal Total,
        string Address, DateTime PlacedAt);
    public sealed record PackageRecord(
        Guid Id, long SellerId, string SellerName, string Status, decimal ShippingFee, string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt);
    public sealed record LineRecord(
        Guid Id, Guid PackageId, long OfferId, long ProductId, long VariantId, string ProductTitle, string VariantLabel, string Sku,
        string? ImageUrl, decimal UnitPrice, int Quantity);

    public static async Task<string> NextNumberAsync(NpgsqlConnection conn, NpgsqlTransaction tx, DateTime now) =>
        OrderNumber.Format(now, await conn.ExecuteScalarAsync<long>("SELECT nextval('order_number_seq')", transaction: tx));

    public static async Task ReserveStockAsync(NpgsqlConnection conn, NpgsqlTransaction tx, IEnumerable<(long OfferId, int Quantity)> items)
    {
        foreach (var (offerId, quantity) in items.OrderBy(i => i.OfferId))
        {
            var updated = await conn.ExecuteAsync("""
                UPDATE offers SET stock = stock - @Quantity, updated_at = now()
                WHERE id = @OfferId AND status = 'active' AND stock >= @Quantity
                """, new { OfferId = offerId, Quantity = quantity }, tx);
            if (updated != 1) throw new InsufficientStockException(offerId);
        }
    }

    public static async Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order order)
    {
        var row = new OrderInsertRecord(order.Id, order.Number, order.CustomerId, OrderStatusRules.Name(order.Status), order.Total.Currency,
            order.Subtotal.Amount, order.ShippingTotal.Amount, order.Total.Amount,
            JsonSerializer.Serialize(order.Address, OrderingJsonContext.Default.ShippingAddress), order.PlacedAt);
        await conn.ExecuteAsync("""
            INSERT INTO orders (id, number, customer_id, status, currency, subtotal, shipping_total, total, shipping_address, placed_at)
            VALUES (@Id, @Number, @CustomerId, @Status, @Currency, @Subtotal, @ShippingTotal, @Total, @Address::jsonb, @PlacedAt)
            """, row, tx);
        foreach (var p in order.Packages)
        {
            await conn.ExecuteAsync("""
                INSERT INTO order_packages (id, order_id, seller_id, seller_name, status, shipping_fee, commission_rate)
                SELECT @Id, @OrderId, @SellerId, @SellerName, @Status, @ShippingFee, s.commission_rate FROM sellers s WHERE s.id = @SellerId
                """, new { p.Id, OrderId = order.Id, p.SellerId, p.SellerName, Status = OrderStatusRules.Name(p.Status), ShippingFee = p.ShippingFee.Amount }, tx);
            await conn.ExecuteAsync("""
                INSERT INTO order_lines (id, order_id, package_id, offer_id, product_id, variant_id, product_title, variant_label, sku, image_url, unit_price, quantity)
                SELECT l.id, @OrderId, @PackageId, l.offer_id, l.product_id, l.variant_id, l.product_title, l.variant_label, l.sku, l.image_url, l.unit_price, l.quantity
                FROM unnest(@Ids, @OfferIds, @ProductIds, @VariantIds, @Titles, @Labels, @Skus, @Images, @Prices, @Quantities)
                    AS l(id, offer_id, product_id, variant_id, product_title, variant_label, sku, image_url, unit_price, quantity)
                """, new
            {
                OrderId = order.Id,
                PackageId = p.Id,
                Ids = p.Lines.Select(l => l.Id).ToArray(),
                OfferIds = p.Lines.Select(l => l.OfferId).ToArray(),
                ProductIds = p.Lines.Select(l => l.ProductId).ToArray(),
                VariantIds = p.Lines.Select(l => l.VariantId).ToArray(),
                Titles = p.Lines.Select(l => l.ProductTitle).ToArray(),
                Labels = p.Lines.Select(l => l.VariantLabel).ToArray(),
                Skus = p.Lines.Select(l => l.Sku).ToArray(),
                Images = p.Lines.Select(l => l.ImageUrl).ToArray(),
                Prices = p.Lines.Select(l => l.UnitPrice.Amount).ToArray(),
                Quantities = p.Lines.Select(l => l.Quantity).ToArray(),
            }, tx);
        }
        await PublishEventsAsync(conn, tx, order);
    }

    const string OrderColumns = "id, number, customer_id, status, currency, shipping_address::text AS shipping_address, placed_at, paid_at, payment_reference, close_reason";

    public static async Task<Order?> LoadAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id) =>
        await RestoreAsync(conn, tx, await conn.QuerySingleOrDefaultAsync<OrderRecord>(
            "SELECT " + OrderColumns + " FROM orders WHERE id = @Id FOR UPDATE", new { Id = id }, tx));

    public static async Task<Order?> LoadByNumberAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string number) =>
        await RestoreAsync(conn, tx, await conn.QuerySingleOrDefaultAsync<OrderRecord>(
            "SELECT " + OrderColumns + " FROM orders WHERE number = @Number", new { Number = number }, tx));

    public static async Task<Order?> LoadByPaymentReferenceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string reference) =>
        await RestoreAsync(conn, tx, await conn.QuerySingleOrDefaultAsync<OrderRecord>(
            "SELECT " + OrderColumns + " FROM orders WHERE payment_reference = @Reference FOR UPDATE", new { Reference = reference }, tx));

    static async Task<Order?> RestoreAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, OrderRecord? o)
    {
        if (o is null) return null;
        var packages = (await conn.QueryAsync<PackageRecord>("""
            SELECT id, seller_id, seller_name, status, shipping_fee, tracking_number, shipped_at, delivered_at
            FROM order_packages WHERE order_id = @OrderId ORDER BY seller_name, id
            """, new { OrderId = o.Id }, tx)).AsList();
        var lines = (await conn.QueryAsync<LineRecord>("""
            SELECT id, package_id, offer_id, product_id, variant_id, product_title, variant_label, sku, image_url, unit_price, quantity
            FROM order_lines WHERE order_id = @OrderId ORDER BY product_title, id
            """, new { OrderId = o.Id }, tx)).AsList();

        var address = JsonSerializer.Deserialize(o.ShippingAddress, OrderingJsonContext.Default.ShippingAddress)!;
        return Order.Restore(o.Id, o.Number, o.CustomerId, address,
            packages.Select(p => SellerPackage.Restore(p.Id, p.SellerId, p.SellerName,
                lines.Where(l => l.PackageId == p.Id).Select(l => new OrderLine(l.Id, l.OfferId, l.ProductId, l.VariantId, l.ProductTitle,
                    l.VariantLabel, l.Sku, l.ImageUrl, Money.Of(l.UnitPrice, o.Currency), l.Quantity)),
                Money.Of(p.ShippingFee, o.Currency), OrderStatusRules.ParsePackage(p.Status), p.TrackingNumber, p.ShippedAt, p.DeliveredAt)),
            OrderStatusRules.ParseOrder(o.Status), o.PlacedAt, o.PaidAt, o.PaymentReference, o.CloseReason);
    }

    public static async Task SaveAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order order)
    {
        await conn.ExecuteAsync("""
            UPDATE orders SET status = @Status, paid_at = @PaidAt, payment_reference = @PaymentReference, close_reason = @CloseReason, updated_at = now()
            WHERE id = @Id
            """, new { order.Id, Status = OrderStatusRules.Name(order.Status), order.PaidAt, order.PaymentReference, order.CloseReason }, tx);
        foreach (var p in order.Packages)
            await conn.ExecuteAsync("""
                UPDATE order_packages SET status = @Status, tracking_number = @TrackingNumber, shipped_at = @ShippedAt, delivered_at = @DeliveredAt
                WHERE id = @Id
                """, new { p.Id, Status = OrderStatusRules.Name(p.Status), p.TrackingNumber, p.ShippedAt, p.DeliveredAt }, tx);
        await PublishEventsAsync(conn, tx, order);
    }

    static async Task PublishEventsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order order)
    {
        foreach (var e in order.DomainEvents)
        {
            if (e is IReleasesStock)
            {
                foreach (var line in order.Lines)
                    await conn.ExecuteAsync("UPDATE offers SET stock = stock + @Quantity, updated_at = now() WHERE id = @OfferId",
                        new { line.OfferId, line.Quantity }, tx);
            }
            if (e is PackageShipped or PackageDelivered)
            {
                var (packageType, message) = e switch
                {
                    PackageShipped s => (OutboxTypes.PackageShipped, new PackageEventMessage(s.OrderId, s.Number, s.CustomerId, s.PackageId, s.TrackingNumber, s.OccurredAt)),
                    PackageDelivered d => (OutboxTypes.PackageDelivered, new PackageEventMessage(d.OrderId, d.Number, d.CustomerId, d.PackageId, null, d.OccurredAt)),
                    _ => throw new InvalidOperationException(),
                };
                await OutboxWriter.EnqueueAsync(conn, tx, packageType, message, OutboxJsonContext.Default.PackageEventMessage);
                continue;
            }
            var (type, reason) = e switch
            {
                OrderPlaced => (OutboxTypes.OrderPlaced, null),
                OrderPaid => (OutboxTypes.OrderPaid, null),
                OrderPaymentFailed f => (OutboxTypes.OrderPaymentFailed, f.Reason),
                OrderCancelled c => (OutboxTypes.OrderCancelled, c.Reason),
                _ => throw new InvalidOperationException($"Unknown domain event {e.GetType().Name}"),
            };
            await OutboxWriter.EnqueueAsync(conn, tx, type, new OrderEventMessage(order.Id, order.Number, order.CustomerId, reason, e.OccurredAt),
                OutboxJsonContext.Default.OrderEventMessage);
            if (e is OrderPlaced or IReleasesStock)
                await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged,
                    new ProductsChanged(order.Lines.Select(l => l.ProductId).Distinct().ToList()), OutboxJsonContext.Default.ProductsChanged);
        }
        order.ClearDomainEvents();
    }
}
