using Dapper;
using Npgsql;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Shared.Returns;

public static class ReturnStore
{
    public sealed record ReturnRecord(
        Guid Id, string Number, Guid OrderId, string OrderNumber, Guid PackageId, long SellerId, long CustomerId, string Status, string Reason, string? Comment,
        decimal CommissionRate, string Currency, DateTime RequestedAt, DateTime? DecidedAt, string? RejectionNote, string? ReturnTracking,
        DateTime? ReceivedAt, DateTime? RefundedAt, string? RefundReference, decimal RefundAmount);
    public sealed record ReturnLineRecord(Guid OrderLineId, long ProductId, string ProductTitle, string VariantLabel, string Sku, decimal UnitPrice, int Quantity);
    public sealed record ReturnedRecord(Guid OrderLineId, int Quantity);
    public sealed record ReturnInsertRecord(
        Guid Id, string Number, Guid OrderId, string OrderNumber, Guid PackageId, long SellerId, long CustomerId, string Status, string Reason, string? Comment,
        decimal CommissionRate, decimal Total, decimal RefundAmount, string Currency, DateTime RequestedAt, Guid[] LineIds, long[] ProductIds, string[] Titles, string[] Labels,
        string[] Skus, decimal[] Prices, int[] Quantities);
    public sealed record ReturnUpdateRecord(
        Guid Id, string Status, DateTime? DecidedAt, string? RejectionNote, string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt, string? RefundReference);

    const string Columns = """
        id, number, order_id, order_number, package_id, seller_id, customer_id, status, reason, comment, commission_rate, currency, requested_at,
        decided_at, rejection_note, return_tracking, received_at, refunded_at, refund_reference, refund_amount
        """;

    public static async Task<string> NextNumberAsync(NpgsqlConnection conn, NpgsqlTransaction tx, DateTime now) =>
        ReturnNumber.Format(now, await conn.ExecuteScalarAsync<long>("SELECT nextval('return_number_seq')", transaction: tx));

    public static async Task<Dictionary<Guid, int>> ReturnedQuantitiesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid packageId) =>
        (await conn.QueryAsync<ReturnedRecord>("""
            SELECT l.order_line_id, sum(l.quantity)::int AS quantity FROM return_lines l JOIN returns r ON r.id = l.return_id
            WHERE r.package_id = @PackageId AND r.status <> 'rejected' GROUP BY l.order_line_id
            """, new { PackageId = packageId }, tx)).ToDictionary(r => r.OrderLineId, r => r.Quantity);

    public static async Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ReturnRequest r)
    {
        await conn.ExecuteAsync("""
            INSERT INTO returns (id, number, order_id, order_number, package_id, seller_id, customer_id, status, reason, comment, commission_rate, total, refund_amount, currency, requested_at)
            VALUES (@Id, @Number, @OrderId, @OrderNumber, @PackageId, @SellerId, @CustomerId, @Status, @Reason, @Comment, @CommissionRate, @Total, @RefundAmount, @Currency, @RequestedAt);
            INSERT INTO return_lines (return_id, order_line_id, product_id, product_title, variant_label, sku, unit_price, quantity)
            SELECT @Id, l.id, l.product_id, l.title, l.label, l.sku, l.price, l.quantity
            FROM unnest(@LineIds, @ProductIds, @Titles, @Labels, @Skus, @Prices, @Quantities) AS l(id, product_id, title, label, sku, price, quantity);
            """, new ReturnInsertRecord(r.Id, r.Number, r.OrderId, r.OrderNumber, r.PackageId, r.SellerId, r.CustomerId, ReturnStatusRules.Name(r.Status), r.Reason,
            r.Comment, r.CommissionRate, r.Total.Amount, r.RefundAmount.Amount, r.Total.Currency, r.RequestedAt, r.Lines.Select(l => l.OrderLineId).ToArray(),
            r.Lines.Select(l => l.ProductId).ToArray(), r.Lines.Select(l => l.ProductTitle).ToArray(), r.Lines.Select(l => l.VariantLabel).ToArray(),
            r.Lines.Select(l => l.Sku).ToArray(), r.Lines.Select(l => l.UnitPrice.Amount).ToArray(), r.Lines.Select(l => l.Quantity).ToArray()), tx);
        await PublishEventsAsync(conn, tx, r);
    }

    public static async Task<ReturnRequest?> LoadAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid id) =>
        await RestoreAsync(conn, tx, await conn.QuerySingleOrDefaultAsync<ReturnRecord>(
            "SELECT " + Columns + " FROM returns WHERE id = @Id" + (tx is null ? "" : " FOR UPDATE"), new { Id = id }, tx));

    public static async Task<ReturnRequest?> LoadByTrackingAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string tracking) =>
        await RestoreAsync(conn, tx, await conn.QuerySingleOrDefaultAsync<ReturnRecord>(
            "SELECT " + Columns + " FROM returns WHERE return_tracking = @Tracking FOR UPDATE", new { Tracking = tracking }, tx));

    static async Task<ReturnRequest?> RestoreAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, ReturnRecord? r)
    {
        if (r is null) return null;
        var lines = await conn.QueryAsync<ReturnLineRecord>("""
            SELECT order_line_id, product_id, product_title, variant_label, sku, unit_price, quantity FROM return_lines
            WHERE return_id = @Id ORDER BY product_title, order_line_id
            """, new { r.Id }, tx);
        return ReturnRequest.Restore(r.Id, r.Number, r.OrderId, r.OrderNumber, r.PackageId, r.SellerId, r.CustomerId, r.Reason, r.Comment, r.CommissionRate,
            lines.Select(l => new ReturnLine(l.OrderLineId, l.ProductId, l.ProductTitle, l.VariantLabel, l.Sku, Money.Of(l.UnitPrice, r.Currency), l.Quantity)),
            ReturnStatusRules.Parse(r.Status), r.RequestedAt, r.DecidedAt, r.RejectionNote, r.ReturnTracking, r.ReceivedAt, r.RefundedAt, r.RefundReference,
            Money.Of(r.RefundAmount, r.Currency));
    }

    public static async Task SaveAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ReturnRequest r)
    {
        await conn.ExecuteAsync("""
            UPDATE returns SET status = @Status, decided_at = @DecidedAt, rejection_note = @RejectionNote, return_tracking = @ReturnTracking,
                received_at = @ReceivedAt, refunded_at = @RefundedAt, refund_reference = @RefundReference
            WHERE id = @Id
            """, new ReturnUpdateRecord(r.Id, ReturnStatusRules.Name(r.Status), r.DecidedAt, r.RejectionNote, r.ReturnTracking, r.ReceivedAt, r.RefundedAt,
            r.RefundReference), tx);
        await PublishEventsAsync(conn, tx, r);
    }

    static async Task PublishEventsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ReturnRequest r)
    {
        foreach (var e in r.DomainEvents)
        {
            var type = e switch
            {
                ReturnRequested => OutboxTypes.ReturnRequested,
                ReturnApproved => OutboxTypes.ReturnApproved,
                ReturnRejected => OutboxTypes.ReturnRejected,
                ReturnReceived => OutboxTypes.ReturnReceived,
                ReturnRefunded => OutboxTypes.ReturnRefunded,
                _ => throw new InvalidOperationException($"Unknown domain event {e.GetType().Name}"),
            };
            await OutboxWriter.EnqueueAsync(conn, tx, type, new ReturnEventMessage(r.Id, r.Number, e.OccurredAt), OutboxJsonContext.Default.ReturnEventMessage);
        }
        r.ClearDomainEvents();
    }
}
