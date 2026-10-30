using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ReapSeek.Api.Identity;

public sealed record SessionAudience(string Name, string CookieName, TimeSpan Lifetime, SameSiteMode SameSite)
{
    public static readonly SessionAudience Storefront = new("storefront", "rs_session", TimeSpan.FromDays(30), SameSiteMode.Lax);
    public static readonly SessionAudience Backoffice = new("backoffice", "rs_bo_session", TimeSpan.FromHours(12), SameSiteMode.Strict);
}

public sealed class SessionService(AccountRepository accounts, IConfiguration cfg)
{
    static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    const int MaxCacheEntries = 10_000;
    const int MaxTokenLength = 128;

    readonly ConcurrentDictionary<string, (UserInfo? User, DateTime Expires)> _cache = new();
    readonly bool _secureCookie = cfg["Auth:SecureCookie"] != "false";

    public async ValueTask<UserInfo?> GetCurrentAsync(HttpContext ctx, SessionAudience audience)
    {
        if (TokenHash(ctx.Request, audience) is not { } hash) return null;
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(hash, out var hit) && hit.Expires > now) return hit.User;

        var user = await accounts.FindSessionAsync(hash, audience.Name, ctx.RequestAborted);
        if (_cache.Count >= MaxCacheEntries) _cache.Clear();
        _cache[hash] = (user, now + CacheTtl);
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
            _cache.TryRemove(hash, out _);
            await accounts.DeleteSessionAsync(hash, ctx.RequestAborted);
        }
        ctx.Response.Cookies.Delete(audience.CookieName, CookieOptions(audience, null));
    }

    public async Task<bool> ChangePasswordAsync(HttpContext ctx, SessionAudience audience, long userId, string currentPassword, string newPassword)
    {
        var changed = await accounts.ChangePasswordAsync(userId, stored => PasswordHasher.Verify(currentPassword, stored),
            PasswordHasher.Hash(newPassword), TokenHash(ctx.Request, audience) ?? "", ctx.RequestAborted);
        if (changed) InvalidateUser(userId);
        return changed;
    }

    void InvalidateUser(long userId)
    {
        foreach (var (hash, entry) in _cache)
            if (entry.User?.Id == userId) _cache.TryRemove(hash, out _);
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
