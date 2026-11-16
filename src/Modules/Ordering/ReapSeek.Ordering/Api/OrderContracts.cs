using ReapSeek.Domain.Ordering;

namespace ReapSeek.Ordering.Api;

internal sealed record OrderSummary(
    string Number, string Status, decimal Total, string Currency, int ItemCount, int PackageCount, string? ImageUrl,
    DateTime PlacedAt, DateTime? PaidAt, string? CustomerEmail);
internal sealed record OrdersPage(List<OrderSummary> Orders, long Total, int Page, int PageSize);
internal sealed record OrderLineView(
    long ProductId, string ProductTitle, string VariantLabel, string Sku, string? ImageUrl, decimal UnitPrice, int Quantity, decimal Total, Guid Id, int ReturnableQuantity);
internal sealed record TrackingEventView(string Kind, string Status, DateTime OccurredAt);
internal sealed record OrderPackageView(
    Guid Id, long SellerId, string SellerName, string Status, decimal Subtotal, decimal ShippingFee, string? TrackingNumber, List<OrderLineView> Lines,
    string? ShipmentStatus, DateTime? ShippedAt, DateTime? DeliveredAt, DateTime? ReturnDeadline, bool Returnable, List<TrackingEventView> Tracking);
internal sealed record ReturnLineView(Guid OrderLineId, string ProductTitle, string VariantLabel, int Quantity, decimal Total);
internal sealed record ReturnView(
    Guid Id, string Number, Guid PackageId, string SellerName, string Status, string Reason, string? Comment, decimal Total, DateTime RequestedAt,
    string? RejectionNote, string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt, List<ReturnLineView> Lines, List<TrackingEventView> Tracking);
internal sealed record OrderPaymentView(string Provider, string Status, string? FailureReason, DateTime CreatedAt);
internal sealed record OrderDetail(
    string Number, string Status, DateTime PlacedAt, DateTime? PaidAt, DateTime PaymentDeadline, string? CloseReason, ShippingAddress Address,
    decimal Subtotal, decimal ShippingTotal, decimal Total, string Currency, List<OrderPackageView> Packages, List<OrderPaymentView> Payments,
    long CustomerId, string? CustomerEmail, string? PaymentUrl, List<ReturnView> Returns, decimal DiscountTotal = 0, string? CouponCode = null,
    bool Cancellable = false, DateTime? RefundedAt = null);
