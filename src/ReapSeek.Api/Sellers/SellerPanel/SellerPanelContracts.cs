using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Api.Sellers.SellerPanel;

public sealed record SellerAccount(long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating);

public sealed record ProposalsResponse(List<ProposalRow> Proposals);

public sealed record CreateSellerUserRequest(string? Email, string? Name, string? Password);
public sealed record SellerUserRow(long Id, string Email, string Name, DateTime CreatedAt);
public sealed record SellerUsersResponse(List<SellerUserRow> Users);

public sealed record ReviewRequest(string? Decision, string? Note);
