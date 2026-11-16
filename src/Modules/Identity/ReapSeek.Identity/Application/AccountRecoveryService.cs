using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Shared;
using ReapSeek.Shared.Caching;
using ReapSeek.Modules.Identity;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Identity.Contracts;

namespace ReapSeek.Identity.Application;

internal sealed class AccountRecoveryService(NpgsqlDataSource db, RedisCache cache, ILogger<AccountRecoveryService> log)
{
    static readonly ModuleOutbox Outbox = new(MessagingModules.Identity);

    internal sealed record UserLookupRecord(string Email, string Role);
    internal sealed record UserRecord(long Id, string Role, bool Suspended, bool Verified);
    internal sealed record RecentRecord(long UserId, string Purpose, DateTime Since, string EventType);
    internal sealed record TokenLookupRecord(string TokenHash, string Purpose);
    internal sealed record TokenRecord(string TokenHash, long UserId);
    internal sealed record PasswordRecord(long UserId, string Hash);
    internal sealed record UserIdRecord(long UserId);

    public async Task RequestPasswordResetAsync(string? email, string role, CancellationToken ct)
    {
        var normalized = IdentityRules.NormalizeEmail(email ?? "");
        Ensure.That(IdentityRules.IsValidEmail(normalized), "A valid email address is required");
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var user = await conn.QuerySingleOrDefaultAsync<UserRecord>("""
            SELECT id, role, suspended_at IS NOT NULL AS suspended, email_verified_at IS NOT NULL AS verified
            FROM users WHERE email = @Email AND role = @Role FOR UPDATE
            """, new UserLookupRecord(normalized, role), tx);
        if (user is null || user.Suspended)
        {
            log.LogInformation("Password reset requested for an unknown or suspended {Role} account", role);
            return;
        }
        if (!await EnqueueIfAllowedAsync(conn, tx, user.Id, AccountTokens.PasswordReset)) return;
        await tx.CommitAsync(ct);
    }

    public async Task<bool> CheckAsync(string? token, string purpose, CancellationToken ct)
    {
        if (!AccountTokens.LooksValid(token)) return false;
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>("""
            SELECT t.user_id FROM account_tokens t JOIN users u ON u.id = t.user_id
            WHERE t.token_hash = @TokenHash AND t.purpose = @Purpose AND t.used_at IS NULL AND t.expires_at > now() AND u.suspended_at IS NULL
            """, new TokenLookupRecord(AccountTokens.Hash(token!), purpose)) is not null;
    }

    public async Task<bool> ResetPasswordAsync(string? token, string? password, CancellationToken ct)
    {
        Ensure.That(PasswordHasher.IsValidLength(password), PasswordHasher.LengthRule);
        if (!AccountTokens.LooksValid(token)) return false;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var userId = await ClaimAsync(conn, tx, token!, AccountTokens.PasswordReset);
        if (userId is null) return false;
        var args = new UserIdRecord(userId.Value);
        await conn.ExecuteAsync("UPDATE users SET password_hash = @Hash, email_verified_at = coalesce(email_verified_at, now()) WHERE id = @UserId",
            new PasswordRecord(userId.Value, PasswordHasher.Hash(password!)), tx);
        await conn.ExecuteAsync("UPDATE account_tokens SET used_at = now() WHERE user_id = @UserId AND purpose = 'password_reset' AND used_at IS NULL", args, tx);
        var revoked = (await conn.QueryAsync<string>("DELETE FROM sessions WHERE user_id = @UserId RETURNING token_hash", args, tx)).ToArray();
        await Outbox.PublishAsync(conn, tx, AccountEmailV1.EventName, new AccountEmailV1(userId.Value, AccountEmailKinds.PasswordChanged),
            IdentityEventsJsonContext.Default.AccountEmailV1);
        await tx.CommitAsync(ct);
        await cache.RemoveAsync(revoked.Select(CacheKeys.Session).ToArray());
        log.LogInformation("User {User} reset their password; {Sessions} sessions revoked", userId, revoked.Length);
        return true;
    }

    public async Task<bool> VerifyEmailAsync(string? token, CancellationToken ct)
    {
        if (!AccountTokens.LooksValid(token)) return false;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var userId = await ClaimAsync(conn, tx, token!, AccountTokens.EmailVerification);
        if (userId is null) return false;
        var args = new UserIdRecord(userId.Value);
        await conn.ExecuteAsync("UPDATE users SET email_verified_at = coalesce(email_verified_at, now()) WHERE id = @UserId", args, tx);
        await conn.ExecuteAsync("UPDATE account_tokens SET used_at = now() WHERE user_id = @UserId AND purpose = 'email_verification' AND used_at IS NULL", args, tx);
        var sessions = (await conn.QueryAsync<string>("SELECT token_hash FROM sessions WHERE user_id = @UserId", args, tx)).ToArray();
        await tx.CommitAsync(ct);
        await cache.RemoveAsync(sessions.Select(CacheKeys.Session).ToArray());
        return true;
    }

    public async Task<bool> ResendVerificationAsync(long userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var user = await LockUserAsync(conn, tx, userId);
        if (user is null || user.Verified) return false;
        if (await EnqueueIfAllowedAsync(conn, tx, userId, AccountTokens.EmailVerification)) await tx.CommitAsync(ct);
        return true;
    }

    public async Task<IResult> AdminPasswordResetAsync(long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var user = await LockUserAsync(conn, tx, customerId);
        if (user is null || user.Role != Roles.Customer) return ApiResults.NotFound("Customer not found");
        if (user.Suspended) return ApiResults.Conflict("Reinstate the account before sending a password reset");
        if (!await EnqueueIfAllowedAsync(conn, tx, customerId, AccountTokens.PasswordReset))
            return ApiResults.Error(StatusCodes.Status429TooManyRequests, "A reset link was sent several times in the last hour. Try again later.");
        await tx.CommitAsync(ct);
        return Results.Accepted();
    }

    static Task<UserRecord?> LockUserAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long userId) =>
        conn.QuerySingleOrDefaultAsync<UserRecord>("""
            SELECT id, role, suspended_at IS NOT NULL AS suspended, email_verified_at IS NOT NULL AS verified FROM users WHERE id = @UserId FOR UPDATE
            """, new UserIdRecord(userId), tx);

    static async Task<bool> EnqueueIfAllowedAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long userId, string purpose)
    {
        var recent = await conn.ExecuteScalarAsync<int>($"""
            SELECT (SELECT count(*) FROM account_tokens WHERE user_id = @UserId AND purpose = @Purpose AND created_at > @Since)
                 + (SELECT count(*) FROM {Outbox.Module.Outbox} WHERE event_type = @EventType AND published_at IS NULL
                        AND payload->>'user_id' = @UserId::text AND payload->>'kind' = @Purpose)
                 + (SELECT count(*) FROM {Outbox.Module.Inbox} WHERE event_type = @EventType AND processed_at IS NULL AND failed_at IS NULL
                        AND payload->>'user_id' = @UserId::text AND payload->>'kind' = @Purpose)
            """, new RecentRecord(userId, purpose, DateTime.UtcNow.AddHours(-1), AccountEmailV1.EventName), tx);
        if (recent >= AccountTokens.MaxRequestsPerHour) return false;
        await Outbox.PublishAsync(conn, tx, AccountEmailV1.EventName, new AccountEmailV1(userId, purpose), IdentityEventsJsonContext.Default.AccountEmailV1);
        return true;
    }

    static Task<long?> ClaimAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string token, string purpose) =>
        conn.ExecuteScalarAsync<long?>("""
            UPDATE account_tokens t SET used_at = now()
            FROM users u
            WHERE t.token_hash = @TokenHash AND t.purpose = @Purpose AND t.used_at IS NULL AND t.expires_at > now()
              AND u.id = t.user_id AND u.suspended_at IS NULL
            RETURNING t.user_id
            """, new TokenLookupRecord(AccountTokens.Hash(token), purpose), tx);
}
