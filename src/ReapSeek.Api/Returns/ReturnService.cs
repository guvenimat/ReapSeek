using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared.Providers;
using ReapSeek.Shared;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Returns;

namespace ReapSeek.Api.Returns;

public sealed record ReturnItemRequest(Guid? OrderLineId, int? Quantity);
public sealed record CreateReturnRequest(Guid? PackageId, List<ReturnItemRequest>? Items, string? Reason, string? Comment);
public sealed record CreatedReturn(Guid Id, string Number, string Status, decimal Total);
public sealed record RejectReturnRequest(string? Note);
public sealed record SellerReturnLine(string ProductTitle, string VariantLabel, string Sku, int Quantity, decimal UnitPrice);
public sealed record SellerReturnRow(
    Guid Id, string Number, string OrderNumber, string Status, string Reason, string? Comment, decimal Total, DateTime RequestedAt, string? RejectionNote,
    string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt, string CustomerName, List<SellerReturnLine> Lines);
public sealed record SellerReturnsResponse(List<SellerReturnRow> Returns);

public sealed class ReturnService(NpgsqlDataSource db, ICarrier carrier, TimeProvider clock)
{
    public sealed record ReturnRowRecord(
        Guid Id, string Number, string OrderNumber, string Status, string Reason, string? Comment, decimal Total, DateTime RequestedAt, string? RejectionNote,
        string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt, string CustomerName);
    public sealed record ReturnLineRecord(Guid ReturnId, string ProductTitle, string VariantLabel, string Sku, int Quantity, decimal UnitPrice);


    public async Task<CreatedReturn> RequestAsync(UserInfo customer, string orderNumber, CreateReturnRequest req, CancellationToken ct)
    {
        Ensure.That(req.PackageId is not null, "package_id is required");
        Ensure.That(req.Items is { Count: > 0 and <= Order.MaxLines }, "items must list between 1 and 50 items");
        var items = req.Items!.Select(i => new ReturnItem(i.OrderLineId ?? throw new ValidationException("order_line_id is required"), i.Quantity ?? 0)).ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var order = await OrderStore.LoadByNumberAsync(conn, tx, orderNumber);
        if (order is null || order.CustomerId != customer.Id) throw new NotFoundException("Order not found");
        var rate = await conn.ExecuteScalarAsync<decimal?>("SELECT commission_rate FROM order_packages WHERE id = @Id AND order_id = @OrderId FOR UPDATE",
            new { Id = req.PackageId!.Value, OrderId = order.Id }, tx) ?? throw new NotFoundException("Package not found");
        var returned = await ReturnStore.ReturnedQuantitiesAsync(conn, tx, req.PackageId.Value);
        var request = ReturnRequest.Request(Guid.NewGuid(), await ReturnStore.NextNumberAsync(conn, tx, now), order, req.PackageId.Value, customer.Id, items,
            req.Reason, req.Comment, rate, returned, now);
        await ReturnStore.InsertAsync(conn, tx, request);
        await tx.CommitAsync(ct);
        return new CreatedReturn(request.Id, request.Number, ReturnStatusRules.Name(request.Status), request.Total.Amount);
    }

    public async Task<SellerReturnRow> ApproveAsync(long sellerId, Guid returnId, CancellationToken ct)
    {
        await using (var conn = await db.OpenConnectionAsync(ct))
        {
            var current = await ReturnStore.LoadAsync(conn, null, returnId);
            if (current is null || current.SellerId != sellerId) throw new NotFoundException("Return not found");
            if (current.Status != ReturnStatus.Requested)
                throw new DomainException("return.transition", $"A return that is {ReturnStatusRules.Name(current.Status)} cannot be approved");
        }
        var shipment = await carrier.CreateShipmentAsync(returnId.ToString(), ShipmentKinds.Return, ct);
        await DecideAsync(sellerId, returnId, r => r.Approve(sellerId, shipment.TrackingNumber, clock.GetUtcNow().UtcDateTime), ct);
        return (await SellerReturnsAsync(sellerId, null, returnId, ct)).Single();
    }

    public async Task<SellerReturnRow> RejectAsync(long sellerId, Guid returnId, RejectReturnRequest req, CancellationToken ct)
    {
        await DecideAsync(sellerId, returnId, r => r.Reject(sellerId, req.Note, clock.GetUtcNow().UtcDateTime), ct);
        return (await SellerReturnsAsync(sellerId, null, returnId, ct)).Single();
    }

    async Task DecideAsync(long sellerId, Guid returnId, Action<ReturnRequest> decide, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var request = await ReturnStore.LoadAsync(conn, tx, returnId);
        if (request is null || request.SellerId != sellerId) throw new NotFoundException("Return not found");
        decide(request);
        await ReturnStore.SaveAsync(conn, tx, request);
        await tx.CommitAsync(ct);
    }

    public async Task<List<SellerReturnRow>> SellerReturnsAsync(long sellerId, string? status, Guid? id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<ReturnRowRecord>("""
            SELECT r.id, r.number, r.order_number, r.status, r.reason, r.comment, r.total, r.requested_at, r.rejection_note, r.return_tracking,
                r.received_at, r.refunded_at, u.name AS customer_name
            FROM returns r JOIN users u ON u.id = r.customer_id
            WHERE r.seller_id = @SellerId AND (@Status::text IS NULL OR r.status = @Status) AND (@Id::uuid IS NULL OR r.id = @Id)
            ORDER BY (r.status = 'requested') DESC, r.requested_at DESC LIMIT 100
            """, new { SellerId = sellerId, Status = status, Id = id })).AsList();
        if (rows.Count == 0) return [];
        var lines = (await conn.QueryAsync<ReturnLineRecord>("""
            SELECT return_id, product_title, variant_label, sku, quantity, unit_price FROM return_lines WHERE return_id = ANY(@Ids) ORDER BY product_title
            """, new { Ids = rows.Select(r => r.Id).ToArray() })).ToLookup(l => l.ReturnId);
        return rows.Select(r => new SellerReturnRow(r.Id, r.Number, r.OrderNumber, r.Status, r.Reason, r.Comment, r.Total, r.RequestedAt, r.RejectionNote,
            r.ReturnTracking, r.ReceivedAt, r.RefundedAt, r.CustomerName,
            lines[r.Id].Select(l => new SellerReturnLine(l.ProductTitle, l.VariantLabel, l.Sku, l.Quantity, l.UnitPrice)).ToList())).ToList();
    }
}
