using ReapSeek.Api.Catalog;
using ReapSeek.Shared;

namespace ReapSeek.Api.Sellers.SellerPanel;

public sealed record SellerAccount(long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating);

public sealed record SellerOffersResponse(List<SellerOfferRow> Offers, long Total, int Page, int PageSize);

public sealed record VariantBuyBox(long VariantId, decimal? Price, bool Mine);
public sealed record SellerProductView(ProductDetail Product, List<VariantBuyBox> BuyBox, bool Editable);

public sealed record ProposalsResponse(List<ProposalRow> Proposals);

public sealed record CreateSellerUserRequest(string? Email, string? Name, string? Password);
public sealed record SellerUserRow(long Id, string Email, string Name, DateTime CreatedAt);
public sealed record SellerUsersResponse(List<SellerUserRow> Users);

public sealed record ReviewRequest(string? Decision, string? Note);

public static class ReviewDecision
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public static readonly string[] All = [Approve, Reject];
}
