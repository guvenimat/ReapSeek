namespace ReapSeek.Api.Checkout;

public sealed record AddressInput(string? FullName, string? Phone, string? City, string? District, string? Line, string? PostalCode);
public sealed record PlaceOrderRequest(AddressInput? Address, decimal? ExpectedTotal);
public sealed record PlaceOrderResponse(string OrderNumber, string PaymentUrl, decimal Total, string Currency);

public sealed record SavedAddress(long Id, string FullName, string Phone, string City, string District, string Line, string PostalCode, DateTime LastUsedAt);
public sealed record AddressesResponse(List<SavedAddress> Addresses);
