namespace ReapSeek.Ordering.Api;

internal sealed record ReturnItemRequest(Guid? OrderLineId, int? Quantity);
internal sealed record CreateReturnRequest(Guid? PackageId, List<ReturnItemRequest>? Items, string? Reason, string? Comment);
internal sealed record CreatedReturn(Guid Id, string Number, string Status, decimal Total);
internal sealed record RejectReturnRequest(string? Note);
internal sealed record SellerReturnLine(string ProductTitle, string VariantLabel, string Sku, int Quantity, decimal UnitPrice);
internal sealed record SellerReturnRow(
    Guid Id, string Number, string OrderNumber, string Status, string Reason, string? Comment, decimal Total, DateTime RequestedAt, string? RejectionNote,
    string? ReturnTracking, DateTime? ReceivedAt, DateTime? RefundedAt, string CustomerName, List<SellerReturnLine> Lines);
internal sealed record SellerReturnsResponse(List<SellerReturnRow> Returns);

