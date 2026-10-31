using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Identity;

namespace ReapSeek.Api.Identity;

public sealed record SellerLogin(long Id, string Email, string Name, DateTime CreatedAt);

public sealed class SellerAccounts(AccountRepository accounts)
{
    public async Task<List<SellerLogin>> ListAsync(long sellerId, CancellationToken ct) =>
        (await accounts.SellerUsersAsync(sellerId, ct)).Select(u => new SellerLogin(u.Id, u.Email, u.Name, u.CreatedAt)).ToList();

    public async Task<SellerLogin> CreateAsync(long sellerId, string? email, string? name, string? password, CancellationToken ct)
    {
        var registration = IdentityRules.Validate(new RegisterRequest(email, name, password));
        var id = await accounts.CreateSellerUserAsync(sellerId, registration.Email, registration.Name, PasswordHasher.Hash(registration.Password), ct)
            ?? throw new ConflictException("An account with this email already exists");
        return new SellerLogin(id, registration.Email, registration.Name, DateTime.UtcNow);
    }
}
