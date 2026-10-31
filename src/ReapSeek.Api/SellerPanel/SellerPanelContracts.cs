using ReapSeek.Api.Catalog;
using ReapSeek.Shared;

namespace ReapSeek.Api.SellerPanel;

public sealed record SellerAccount(long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating);

public sealed record SellerOfferRow(
    long Id, long ProductId, string ProductTitle, string ProductSlug, string ProductStatus, long VariantId, string Sku, string VariantLabel,
    decimal Price, decimal? ListPrice, int Stock, int HandlingDays, string Status, decimal? BuyBoxPrice, bool IsBuyBox, DateTime UpdatedAt);
public sealed record SellerOffersResponse(List<SellerOfferRow> Offers, long Total, int Page, int PageSize);

public sealed record VariantBuyBox(long VariantId, decimal? Price, bool Mine);
public sealed record SellerProductView(ProductDetail Product, List<VariantBuyBox> BuyBox, bool Editable);

public sealed record ProposalRow(long Id, string Slug, string Title, string Brand, string CategoryName, string Status, string? ReviewNote, DateTime CreatedAt);
public sealed record ProposalsResponse(List<ProposalRow> Proposals);

public sealed record PackageAddress(string FullName, string Phone, string City, string District, string Line, string PostalCode);
public sealed record PackageLineRow(string ProductTitle, string VariantLabel, string Sku, decimal UnitPrice, int Quantity);
public sealed record SellerPackageRow(
    Guid Id, string OrderNumber, string Status, DateTime PlacedAt, DateTime? PaidAt, decimal Subtotal, decimal ShippingFee, decimal CommissionRate,
    string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt, PackageAddress Address, List<PackageLineRow> Lines);
public sealed record SellerPackagesResponse(List<SellerPackageRow> Packages, long Total, int Page, int PageSize);

public sealed record BalanceSummary(decimal InTransit, decimal Pending, decimal Available, decimal PaidOut, DateTime NextPayoutAt);
public sealed record LedgerRow(long Id, string OrderNumber, string Kind, decimal Amount, DateTime AvailableAt, long? PayoutId, DateTime CreatedAt);
public sealed record PayoutRow(long Id, long SellerId, string SellerName, DateTime PeriodEnd, decimal Amount, int EntryCount, string? TransferReference, DateTime CreatedAt);
public sealed record SellerBalanceResponse(BalanceSummary Summary, List<LedgerRow> Entries, List<PayoutRow> Payouts);
public sealed record PayoutsResponse(List<PayoutRow> Payouts);

public sealed record CreateSellerUserRequest(string? Email, string? Name, string? Password);
public sealed record SellerUserRow(long Id, string Email, string Name, DateTime CreatedAt);
public sealed record SellerUsersResponse(List<SellerUserRow> Users);

public sealed record ReviewRequest(string? Decision, string? Note);

public static class PackageFilter
{
    public static readonly string[] Statuses = ["preparing", "shipped", "delivered"];

    public static string? Status(string? value) =>
        value?.Trim() is { Length: > 0 } s ? Ensure.OneOf(s, "status", Statuses) : null;
}

public static class ReviewDecision
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public static readonly string[] All = [Approve, Reject];
}
