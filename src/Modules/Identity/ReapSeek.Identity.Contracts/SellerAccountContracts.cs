namespace ReapSeek.Identity.Contracts;

public sealed record SellerLogin(long Id, string Email, string Name, DateTime CreatedAt);

public interface ISellerAccounts
{
    Task<List<SellerLogin>> ListAsync(long sellerId, CancellationToken ct);
    Task<SellerLogin> CreateAsync(long sellerId, string? email, string? name, string? password, CancellationToken ct);
}
