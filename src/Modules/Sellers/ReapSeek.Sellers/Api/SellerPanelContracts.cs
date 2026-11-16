using ReapSeek.Catalog.Contracts;

namespace ReapSeek.Sellers.Api;

internal sealed record SellerAccount(long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating);

internal sealed record ProposalsResponse(List<ProposalRow> Proposals);

internal sealed record CreateSellerUserRequest(string? Email, string? Name, string? Password);
internal sealed record SellerUserRow(long Id, string Email, string Name, DateTime CreatedAt);
internal sealed record SellerUsersResponse(List<SellerUserRow> Users);

internal sealed record ReviewRequest(string? Decision, string? Note);
