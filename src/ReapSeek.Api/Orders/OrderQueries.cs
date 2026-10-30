using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared;
using ReapSeek.Shared.Ordering;

namespace ReapSeek.Api.Orders;

public sealed record OrderSummary(
    string Number, string Status, decimal Total, string Currency, int ItemCount, int PackageCount, string? ImageUrl,
    DateTime PlacedAt, DateTime? PaidAt, string? CustomerEmail);
public sealed record OrdersPage(List<OrderSummary> Orders, long Total, int Page, int PageSize);
public sealed record OrderLineView(long ProductId, string ProductTitle, string VariantLabel, string Sku, string? ImageUrl, decimal UnitPrice, int Quantity, decimal Total);
public sealed record OrderPackageView(long SellerId, string SellerName, string Status, decimal Subtotal, decimal ShippingFee, string? TrackingNumber, List<OrderLineView> Lines);
public sealed record OrderPaymentView(string Provider, string Status, string? FailureReason, DateTime CreatedAt);
public sealed record OrderDetail(
    string Number, string Status, DateTime PlacedAt, DateTime? PaidAt, DateTime PaymentDeadline, string? CloseReason, ShippingAddress Address,
    decimal Subtotal, decimal ShippingTotal, decimal Total, string Currency, List<OrderPackageView> Packages, List<OrderPaymentView> Payments,
    long CustomerId, string? CustomerEmail, string? PaymentUrl);

public sealed class OrderQueries(NpgsqlDataSource db)
{
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
        var payments = (await conn.QueryAsync<PaymentRecord>("""
            SELECT p.provider, p.status, p.failure_reason, p.created_at, p.redirect_url FROM payments p
            JOIN orders o ON o.id = p.order_id WHERE o.number = @Number ORDER BY p.created_at
            """, new { Number = number })).AsList();
        var pending = order.Status == OrderStatus.PendingPayment ? payments.LastOrDefault(p => p.Status == "pending")?.RedirectUrl : null;
        return new OrderDetail(order.Number, OrderStatusRules.Name(order.Status), order.PlacedAt, order.PaidAt, order.PaymentDeadline, order.CloseReason,
            order.Address, order.Subtotal.Amount, order.ShippingTotal.Amount, order.Total.Amount, order.Total.Currency,
            order.Packages.Select(p => new OrderPackageView(p.SellerId, p.SellerName, OrderStatusRules.Name(p.Status), p.Subtotal.Amount, p.ShippingFee.Amount,
                p.TrackingNumber, p.Lines.Select(l => new OrderLineView(l.ProductId, l.ProductTitle, l.VariantLabel, l.Sku, l.ImageUrl, l.UnitPrice.Amount,
                    l.Quantity, l.Total.Amount)).ToList())).ToList(),
            payments.Select(p => new OrderPaymentView(p.Provider, p.Status, p.FailureReason, p.CreatedAt)).ToList(),
            order.CustomerId, email, pending);
    }

    static OrderSummary ToSummary(SummaryRecord r) =>
        new(r.Number, r.Status, r.Total, r.Currency, (int)r.ItemCount, (int)r.PackageCount, r.ImageUrl, r.PlacedAt, r.PaidAt, r.CustomerEmail);
}
