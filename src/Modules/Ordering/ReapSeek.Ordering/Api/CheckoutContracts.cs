namespace ReapSeek.Ordering.Api;

internal sealed record AddressInput(string? FullName, string? Phone, string? City, string? District, string? Line, string? PostalCode);
internal sealed record PlaceOrderRequest(AddressInput? Address, decimal? ExpectedTotal, string? CouponCode = null);
internal sealed record CouponPreview(string Code, decimal Discount, decimal Total, DateTime ExpiresAt);
internal sealed record PlaceOrderResponse(string OrderNumber, string PaymentUrl, decimal Total, string Currency);

internal sealed record SavedAddress(long Id, string FullName, string Phone, string City, string District, string Line, string PostalCode, DateTime LastUsedAt);
internal sealed record AddressesResponse(List<SavedAddress> Addresses);
