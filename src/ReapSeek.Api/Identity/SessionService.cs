using System.Security.Cryptography;
using System.Text;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Api.Identity;

public sealed record SessionAudience(string Name, string CookieName, TimeSpan Lifetime, SameSiteMode SameSite)
{
    public static readonly SessionAudience Storefront = new("storefront", "rs_session", TimeSpan.FromDays(30), SameSiteMode.Lax);
    public static readonly SessionAudience Backoffice = new("backoffice", "rs_bo_session", TimeSpan.FromHours(12), SameSiteMode.Strict);
}

public sealed class SessionService(AccountRepository accounts, RedisCache cache, IConfiguration cfg)
{
    static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    const int MaxTokenLength = 128;

    readonly bool _secureCookie = cfg["Auth:SecureCookie"] != "false";

    public async ValueTask<UserInfo?> GetCurrentAsync(HttpContext ctx, SessionAudience audience)
    {
        if (TokenHash(ctx.Request, audience) is not { } hash) return null;
        if (ctx.Items.TryGetValue(audience, out var resolved)) return resolved as UserInfo;
        var user = await cache.GetOrLoadAsync(CacheKeys.Session(hash), CacheJsonContext.Default.UserInfo, CacheTtl,
            () => accounts.FindSessionAsync(hash, audience.Name, ctx.RequestAborted));
        ctx.Items[audience] = user;
        return user;
    }

    public async Task<AccountRepository.CredentialsRecord?> AuthenticateAsync(string email, string password, CancellationToken ct)
    {
        var credentials = await accounts.FindCredentialsAsync(IdentityRules.NormalizeEmail(email), ct);
        return PasswordHasher.Verify(password, credentials?.PasswordHash) ? credentials : null;
    }

    public async Task<UserInfo> SignInAsync(HttpContext ctx, SessionAudience audience, long userId)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(token);
        var expires = DateTime.UtcNow + audience.Lifetime;
        await accounts.CreateSessionAsync(userId, hash, audience.Name, expires, ctx.RequestAborted);
        ctx.Response.Cookies.Append(audience.CookieName, token, CookieOptions(audience, expires));
        return await accounts.FindSessionAsync(hash, audience.Name, ctx.RequestAborted)
            ?? throw new InvalidOperationException("Session was not found right after it was created");
    }

    public async Task SignOutAsync(HttpContext ctx, SessionAudience audience)
    {
        if (TokenHash(ctx.Request, audience) is { } hash)
        {
            await accounts.DeleteSessionAsync(hash, ctx.RequestAborted);
            await cache.RemoveAsync(CacheKeys.Session(hash));
        }
        ctx.Response.Cookies.Delete(audience.CookieName, CookieOptions(audience, null));
    }

    public async Task<bool> ChangePasswordAsync(HttpContext ctx, SessionAudience audience, long userId, string currentPassword, string newPassword)
    {
        var revoked = await accounts.ChangePasswordAsync(userId, stored => PasswordHasher.Verify(currentPassword, stored),
            PasswordHasher.Hash(newPassword), TokenHash(ctx.Request, audience) ?? "", ctx.RequestAborted);
        if (revoked is null) return false;
        await cache.RemoveAsync(revoked.Select(CacheKeys.Session).ToArray());
        return true;
    }

    CookieOptions CookieOptions(SessionAudience audience, DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = _secureCookie,
        SameSite = audience.SameSite,
        Path = "/",
        Expires = expires,
    };

    static string? TokenHash(HttpRequest req, SessionAudience audience) =>
        req.Cookies.TryGetValue(audience.CookieName, out var token) && token is { Length: > 0 and <= MaxTokenLength } ? Hash(token) : null;

    static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
