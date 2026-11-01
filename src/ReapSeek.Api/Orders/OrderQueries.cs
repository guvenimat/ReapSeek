using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared;
using ReapSeek.Shared.Ordering;

namespace ReapSeek.Api.Orders;

public sealed record OrderSummary(
    string Number, string Status, decimal Total, string Currency, int ItemCount, int PackageCount, string? ImageUrl,
    DateTime PlacedAt, DateTime? PaidAt, string? CustomerEmail);
public sealed record OrdersPage(List<OrderSummary> Orders, long Total, int Page, int PageSize);
public sealed record OrderLineView(
    long ProductId, string ProductTitle, string VariantLabel, string Sku, string? ImageUrl, decimal UnitPrice, int Quantity, decimal Total, Guid Id, int ReturnableQuantity);
public sealed record TrackingEventView(string Kind, string Status, DateTime OccurredAt);
public sealed record OrderPackageView(
    Guid Id, long SellerId, string SellerName, string Status, decimal Subtotal, decimal ShippingFee, string? TrackingNumber, List<OrderLineView> Lines,
    string? ShipmentStatus, DateTime? ShippedAt, DateTime? DeliveredAt, DateTime? ReturnDeadline, bool Returnable, List<TrackingEventView> Tracking);
public sealed record ReturnLineView(Guid OrderLineId, string ProductTitle, string VariantLabel, int Quantity, decimal Total);
public sealed record ReturnView(
    Guid Id, string Number, Guid PackageId, string SellerName, string Status, string Reason, string? Comment, decimal Total, DateTime RequestedAt,
    string? RejectionNote, string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt, List<ReturnLineView> Lines, List<TrackingEventView> Tracking);
public sealed record OrderPaymentView(string Provider, string Status, string? FailureReason, DateTime CreatedAt);
public sealed record OrderDetail(
    string Number, string Status, DateTime PlacedAt, DateTime? PaidAt, DateTime PaymentDeadline, string? CloseReason, ShippingAddress Address,
    decimal Subtotal, decimal ShippingTotal, decimal Total, string Currency, List<OrderPackageView> Packages, List<OrderPaymentView> Payments,
    long CustomerId, string? CustomerEmail, string? PaymentUrl, List<ReturnView> Returns, decimal DiscountTotal = 0, string? CouponCode = null,
    bool Cancellable = false, DateTime? RefundedAt = null);

public sealed class OrderQueries(NpgsqlDataSource db, TimeProvider clock)
{
    public sealed record IdRecord(Guid Id);
    public sealed record ShipmentRecord(Guid Id, string? ShipmentStatus);
    public sealed record TrackingRecord(string TrackingNumber, string Kind, string Status, DateTime OccurredAt);
    public sealed record ReturnRowRecord(
        Guid Id, string Number, Guid PackageId, string SellerName, string Status, string Reason, string? Comment, decimal Total, DateTime RequestedAt,
        string? RejectionNote, string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt);
    public sealed record ReturnLineRecord(Guid ReturnId, Guid OrderLineId, string ProductTitle, string VariantLabel, int Quantity, decimal UnitPrice, string Status);
    public sealed record SummaryRecord(
        string Number, string Status, decimal Total, string Currency, long ItemCount, long PackageCount, string? ImageUrl,
        DateTime PlacedAt, DateTime? PaidAt, string? CustomerEmail);
    public sealed record PaymentRecord(string Provider, string Status, string? FailureReason, DateTime CreatedAt, string? RedirectUrl);

    const string SummarySelect = """
        SELECT o.number, o.status, o.total, o.currency,
            (SELECT COALESCE(sum(l.quantity), 0) FROM order_lines l WHERE l.order_id = o.id) AS item_count,
            (SELECT count(*) FROM order_packages p WHERE p.order_id = o.id) AS package_count,
            (SELECT l.image_url FROM order_lines l WHERE l.order_id = o.id ORDER BY l.product_title LIMIT 1) AS image_url,
            o.placed_at, o.paid_at, u.email AS customer_email
        FROM orders o JOIN users u ON u.id = o.customer_id
        """;

    public async Task<OrdersPage> ForCustomerAsync(long customerId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM orders WHERE customer_id = @CustomerId", new { CustomerId = customerId });
        var rows = await conn.QueryAsync<SummaryRecord>(SummarySelect + """

            WHERE o.customer_id = @CustomerId ORDER BY o.placed_at DESC LIMIT @Limit OFFSET @Offset
            """, new { CustomerId = customerId, Limit = pageSize, Offset = (page - 1) * pageSize });
        return new OrdersPage(rows.Select(ToSummary).ToList(), total, page, pageSize);
    }

    public async Task<OrdersPage> AllAsync(string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var args = new
        {
            Status = status,
            Search = search is null ? null : $"%{search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%",
            Limit = pageSize,
            Offset = (page - 1) * pageSize,
        };
        const string Where = """
            WHERE (@Status::text IS NULL OR o.status = @Status) AND (@Search::text IS NULL OR o.number ILIKE @Search OR u.email ILIKE @Search)
            """;
        await using var conn = await db.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM orders o JOIN users u ON u.id = o.customer_id\n" + Where, args);
        var rows = await conn.QueryAsync<SummaryRecord>(SummarySelect + "\n" + Where + "\nORDER BY o.placed_at DESC LIMIT @Limit OFFSET @Offset", args);
        return new OrdersPage(rows.Select(ToSummary).ToList(), total, page, pageSize);
    }

    public async Task<OrderDetail?> DetailAsync(string number, long? customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var order = await OrderStore.LoadByNumberAsync(conn, null, number);
        if (order is null || (customerId is not null && order.CustomerId != customerId)) return null;
        var email = await conn.ExecuteScalarAsync<string?>("SELECT email FROM users WHERE id = @Id", new { Id = order.CustomerId });
        var refundedAt = await conn.ExecuteScalarAsync<DateTime?>("SELECT refunded_at FROM orders WHERE id = @Id", new IdRecord(order.Id));
        var payments = (await conn.QueryAsync<PaymentRecord>("""
            SELECT p.provider, p.status, p.failure_reason, p.created_at, p.redirect_url FROM payments p
            JOIN orders o ON o.id = p.order_id WHERE o.number = @Number ORDER BY p.created_at
            """, new { Number = number })).AsList();
        var pending = order.Status == OrderStatus.PendingPayment ? payments.LastOrDefault(p => p.Status == "pending")?.RedirectUrl : null;
        var shipments = (await conn.QueryAsync<ShipmentRecord>("SELECT id, shipment_status FROM order_packages WHERE order_id = @Id", new { order.Id }))
            .ToDictionary(s => s.Id, s => s.ShipmentStatus);
        var returns = (await conn.QueryAsync<ReturnRowRecord>("""
            SELECT r.id, r.number, r.package_id, p.seller_name, r.status, r.reason, r.comment, r.refund_amount AS total, r.requested_at, r.rejection_note, r.return_tracking,
                r.received_at, r.refunded_at
            FROM returns r JOIN order_packages p ON p.id = r.package_id WHERE r.order_id = @Id ORDER BY r.requested_at
            """, new { order.Id })).AsList();
        var returnLines = (await conn.QueryAsync<ReturnLineRecord>("""
            SELECT l.return_id, l.order_line_id, l.product_title, l.variant_label, l.quantity, l.unit_price, r.status
            FROM return_lines l JOIN returns r ON r.id = l.return_id WHERE r.order_id = @Id
            """, new { order.Id })).AsList();
        var trackingNumbers = order.Packages.Select(p => p.TrackingNumber).Concat(returns.Select(r => r.ReturnTracking)).OfType<string>().ToArray();
        var tracking = (await conn.QueryAsync<TrackingRecord>("""
            SELECT tracking_number, kind, status, occurred_at FROM shipment_events WHERE tracking_number = ANY(@Numbers) ORDER BY occurred_at
            """, new { Numbers = trackingNumbers })).ToLookup(t => t.TrackingNumber);
        var returned = returnLines.Where(l => l.Status != "rejected").GroupBy(l => l.OrderLineId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var now = clock.GetUtcNow().UtcDateTime;
        List<TrackingEventView> Events(string? number) =>
            number is null ? [] : tracking[number].Select(t => new TrackingEventView(t.Kind, t.Status, t.OccurredAt)).ToList();
        return new OrderDetail(order.Number, OrderStatusRules.Name(order.Status), order.PlacedAt, order.PaidAt, order.PaymentDeadline, order.CloseReason,
            order.Address, order.Subtotal.Amount, order.ShippingTotal.Amount, order.Total.Amount, order.Total.Currency,
            order.Packages.Select(p =>
            {
                var deadline = p.DeliveredAt is { } d ? ReturnRequest.Deadline(d) : (DateTime?)null;
                var lines = p.Lines.Select(l => new OrderLineView(l.ProductId, l.ProductTitle, l.VariantLabel, l.Sku, l.ImageUrl, l.UnitPrice.Amount,
                    l.Quantity, l.Total.Amount, l.Id, deadline > now ? l.Quantity - returned.GetValueOrDefault(l.Id) : 0)).ToList();
                return new OrderPackageView(p.Id, p.SellerId, p.SellerName, OrderStatusRules.Name(p.Status), p.Subtotal.Amount, p.ShippingFee.Amount,
                    p.TrackingNumber, lines, shipments.GetValueOrDefault(p.Id), p.ShippedAt, p.DeliveredAt, deadline, lines.Any(l => l.ReturnableQuantity > 0),
                    Events(p.TrackingNumber));
            }).ToList(),
            payments.Select(p => new OrderPaymentView(p.Provider, p.Status, p.FailureReason, p.CreatedAt)).ToList(),
            order.CustomerId, email, pending,
            returns.Select(r => new ReturnView(r.Id, r.Number, r.PackageId, r.SellerName, r.Status, r.Reason, r.Comment, r.Total, r.RequestedAt, r.RejectionNote,
                r.ReturnTracking, r.ReceivedAt, r.RefundedAt,
                returnLines.Where(l => l.ReturnId == r.Id).Select(l => new ReturnLineView(l.OrderLineId, l.ProductTitle, l.VariantLabel, l.Quantity,
                    l.UnitPrice * l.Quantity)).ToList(), Events(r.ReturnTracking))).ToList(),
            order.DiscountTotal.Amount, order.Discount?.CouponCode,
            order.Status == OrderStatus.PendingPayment || order.CanCancelPaid, refundedAt);
    }

    static OrderSummary ToSummary(SummaryRecord r) =>
        new(r.Number, r.Status, r.Total, r.Currency, (int)r.ItemCount, (int)r.PackageCount, r.ImageUrl, r.PlacedAt, r.PaidAt, r.CustomerEmail);
}
