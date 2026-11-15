using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Api;
using ReapSeek.Identity.Contracts;
using ReapSeek.Identity.Infrastructure;
using ReapSeek.Modules.Identity;

namespace ReapSeek.Identity.Application;

internal sealed class SellerAccounts(AccountRepository accounts) : ISellerAccounts
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
