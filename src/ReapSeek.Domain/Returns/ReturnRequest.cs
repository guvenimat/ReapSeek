using System.Globalization;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Ledger;
using ReapSeek.Domain.Ordering;

namespace ReapSeek.Domain.Returns;

public enum ReturnStatus { Requested, Approved, Rejected, Received, Refunded }

public static class ReturnReasons
{
    public static readonly string[] All = ["changed_mind", "wrong_size", "damaged", "not_as_described", "wrong_item"];
}

public sealed record ReturnLine(Guid OrderLineId, long ProductId, string ProductTitle, string VariantLabel, string Sku, Money UnitPrice, int Quantity)
{
    public Money Total => UnitPrice * Quantity;
}

public sealed record ReturnItem(Guid OrderLineId, int Quantity);

public sealed record ReturnRequested(Guid ReturnId, string Number, long SellerId, DateTime OccurredAt) : IDomainEvent;
public sealed record ReturnApproved(Guid ReturnId, string Number, long CustomerId, DateTime OccurredAt) : IDomainEvent;
public sealed record ReturnRejected(Guid ReturnId, string Number, long CustomerId, DateTime OccurredAt) : IDomainEvent;
public sealed record ReturnReceived(Guid ReturnId, string Number, DateTime OccurredAt) : IDomainEvent;
public sealed record ReturnRefunded(Guid ReturnId, string Number, long CustomerId, DateTime OccurredAt) : IDomainEvent;

public static class ReturnStatusRules
{
    static readonly Dictionary<ReturnStatus, ReturnStatus[]> Allowed = new()
    {
        [ReturnStatus.Requested] = [ReturnStatus.Approved, ReturnStatus.Rejected],
        [ReturnStatus.Approved] = [ReturnStatus.Received],
        [ReturnStatus.Received] = [ReturnStatus.Refunded],
        [ReturnStatus.Rejected] = [],
        [ReturnStatus.Refunded] = [],
    };

    public static bool CanMove(ReturnStatus from, ReturnStatus to) => Allowed[from].Contains(to);

    public static string Name(ReturnStatus status) => status switch
    {
        ReturnStatus.Requested => "requested",
        ReturnStatus.Approved => "approved",
        ReturnStatus.Rejected => "rejected",
        ReturnStatus.Received => "received",
        ReturnStatus.Refunded => "refunded",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static ReturnStatus Parse(string name) => Enum.GetValues<ReturnStatus>().First(s => Name(s) == name);
}

public static class ReturnNumber
{
    public static string Format(DateTime at, long sequence) =>
        $"RT{at.ToString("yyMMdd", CultureInfo.InvariantCulture)}{sequence % 1_000_000:000000}";
}

public sealed class ReturnRequest : AggregateRoot
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);
    public const int MaxCommentLength = 500;

    readonly List<ReturnLine> _lines;

    ReturnRequest(Guid id, string number, Guid orderId, string orderNumber, Guid packageId, long sellerId, long customerId, string reason, string? comment,
        decimal commissionRate, IEnumerable<ReturnLine> lines, ReturnStatus status, DateTime requestedAt, DateTime? decidedAt, string? rejectionNote,
        string? returnTracking, DateTime? receivedAt, DateTime? refundedAt, string? refundReference, Money? refundAmount)
    {
        RefundAmount = refundAmount ?? Money.Sum(lines.Select(l => l.Total));
        Id = id;
        Number = number;
        OrderId = orderId;
        OrderNumber = orderNumber;
        PackageId = packageId;
        SellerId = sellerId;
        CustomerId = customerId;
        Reason = reason;
        Comment = comment;
        CommissionRate = commissionRate;
        _lines = lines.ToList();
        Status = status;
        RequestedAt = requestedAt;
        DecidedAt = decidedAt;
        RejectionNote = rejectionNote;
        ReturnTracking = returnTracking;
        ReceivedAt = receivedAt;
        RefundedAt = refundedAt;
        RefundReference = refundReference;
    }

    public Guid Id { get; }
    public string Number { get; }
    public Guid OrderId { get; }
    public string OrderNumber { get; }
    public Guid PackageId { get; }
    public long SellerId { get; }
    public long CustomerId { get; }
    public string Reason { get; }
    public string? Comment { get; }
    public decimal CommissionRate { get; }
    public IReadOnlyList<ReturnLine> Lines => _lines;
    public ReturnStatus Status { get; private set; }
    public DateTime RequestedAt { get; }
    public DateTime? DecidedAt { get; private set; }
    public string? RejectionNote { get; private set; }
    public string? ReturnTracking { get; private set; }
    public DateTime? ReceivedAt { get; private set; }
    public DateTime? RefundedAt { get; private set; }
    public string? RefundReference { get; private set; }
    public Money Total => Money.Sum(_lines.Select(l => l.Total));
    public Money RefundAmount { get; }

    public static Money RefundFor(Money linesTotal, Money orderSubtotal, Money orderDiscount)
    {
        if (orderDiscount.IsZero || orderSubtotal.IsZero) return linesTotal;
        var share = Math.Round(linesTotal.Amount * orderDiscount.Amount / orderSubtotal.Amount, 2, MidpointRounding.AwayFromZero);
        return Money.Of(linesTotal.Amount - share, linesTotal.Currency);
    }

    public static DateTime Deadline(DateTime deliveredAt) => deliveredAt + Window;

    public static ReturnRequest Request(Guid id, string number, Order order, Guid packageId, long customerId, IReadOnlyList<ReturnItem> items,
        string? reason, string? comment, decimal commissionRate, IReadOnlyDictionary<Guid, int> alreadyReturned, DateTime now)
    {
        if (order.CustomerId != customerId) throw new DomainException("return.not_found", "Order not found");
        var package = order.Packages.FirstOrDefault(p => p.Id == packageId) ?? throw new DomainException("return.not_found", "Package not found");
        if (package.Status != PackageStatus.Delivered || package.DeliveredAt is not { } delivered)
            throw new DomainException("return.not_delivered", "Only a delivered package can be returned");
        if (now > Deadline(delivered))
            throw new DomainException("return.window_closed", $"Returns are accepted for {Window.TotalDays:0} days after delivery");
        if (reason is null || !ReturnReasons.All.Contains(reason))
            throw new DomainException("return.reason", $"reason: {string.Join(" | ", ReturnReasons.All)}");
        var note = comment?.Trim() is { Length: > 0 } c ? c : null;
        if (note?.Length > MaxCommentLength) throw new DomainException("return.comment", $"comment can be at most {MaxCommentLength} characters");
        if (items.Count == 0) throw new DomainException("return.empty", "Choose at least one item to return");
        if (items.Select(i => i.OrderLineId).Distinct().Count() != items.Count) throw new DomainException("return.duplicate", "Each item can be listed once");

        var lines = items.Select(item =>
        {
            var line = package.Lines.FirstOrDefault(l => l.Id == item.OrderLineId)
                ?? throw new DomainException("return.line", "An item does not belong to this package");
            var left = line.Quantity - alreadyReturned.GetValueOrDefault(line.Id);
            if (item.Quantity < 1 || item.Quantity > left)
                throw new DomainException("return.quantity", $"You can return at most {left} of {line.ProductTitle}");
            return new ReturnLine(line.Id, line.ProductId, line.ProductTitle, line.VariantLabel, line.Sku, line.UnitPrice, item.Quantity);
        }).ToList();

        var refund = RefundFor(Money.Sum(lines.Select(l => l.Total), order.Subtotal.Currency), order.Subtotal, order.DiscountTotal);
        var request = new ReturnRequest(id, number, order.Id, order.Number, package.Id, package.SellerId, customerId, reason, note, commissionRate, lines,
            ReturnStatus.Requested, now, null, null, null, null, null, null, refund);
        request.Raise(new ReturnRequested(id, number, package.SellerId, now));
        return request;
    }

    public static ReturnRequest Restore(Guid id, string number, Guid orderId, string orderNumber, Guid packageId, long sellerId, long customerId, string reason,
        string? comment, decimal commissionRate, IEnumerable<ReturnLine> lines, ReturnStatus status, DateTime requestedAt, DateTime? decidedAt,
        string? rejectionNote, string? returnTracking, DateTime? receivedAt, DateTime? refundedAt, string? refundReference, Money? refundAmount = null) =>
        new(id, number, orderId, orderNumber, packageId, sellerId, customerId, reason, comment, commissionRate, lines, status, requestedAt, decidedAt,
            rejectionNote, returnTracking, receivedAt, refundedAt, refundReference, refundAmount);

    public void Approve(long sellerId, string returnTracking, DateTime now)
    {
        EnsureSeller(sellerId);
        if (string.IsNullOrWhiteSpace(returnTracking)) throw new DomainException("return.tracking", "A return tracking number is required");
        Move(ReturnStatus.Approved);
        DecidedAt = now;
        ReturnTracking = returnTracking.Trim();
        Raise(new ReturnApproved(Id, Number, CustomerId, now));
    }

    public void Reject(long sellerId, string? note, DateTime now)
    {
        EnsureSeller(sellerId);
        var text = note?.Trim() is { Length: > 0 } n ? n : throw new DomainException("return.note", "Tell the customer why the return is rejected");
        if (text.Length > MaxCommentLength) throw new DomainException("return.note", $"note can be at most {MaxCommentLength} characters");
        Move(ReturnStatus.Rejected);
        DecidedAt = now;
        RejectionNote = text;
        Raise(new ReturnRejected(Id, Number, CustomerId, now));
    }

    public bool MarkReceived(DateTime now)
    {
        if (Status is ReturnStatus.Received or ReturnStatus.Refunded) return false;
        Move(ReturnStatus.Received);
        ReceivedAt = now;
        Raise(new ReturnReceived(Id, Number, now));
        return true;
    }

    public IReadOnlyList<LedgerEntry> MarkRefunded(string refundReference, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(refundReference)) throw new DomainException("return.refund", "A refund reference is required");
        Move(ReturnStatus.Refunded);
        RefundedAt = now;
        RefundReference = refundReference;
        Raise(new ReturnRefunded(Id, Number, CustomerId, now));
        return Settlement.ForRefund(Total.Amount, CommissionRate);
    }

    void EnsureSeller(long sellerId)
    {
        if (sellerId != SellerId) throw new DomainException("return.not_found", "Return not found");
    }

    void Move(ReturnStatus next)
    {
        if (!ReturnStatusRules.CanMove(Status, next))
            throw new DomainException("return.transition", $"A return cannot move from {ReturnStatusRules.Name(Status)} to {ReturnStatusRules.Name(next)}");
        Status = next;
    }
}
