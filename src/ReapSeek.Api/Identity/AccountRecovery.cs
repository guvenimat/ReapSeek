using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Caching;
using ReapSeek.Shared.Identity;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Identity;

public sealed record ForgotPasswordRequest(string? Email);
public sealed record ResetPasswordRequest(string? Token, string? Password);
public sealed record TokenRequest(string? Token);
public sealed record TokenCheckResponse(bool Valid);

public sealed class AccountRecoveryService(NpgsqlDataSource db, RedisCache cache, ILogger<AccountRecoveryService> log)
{
    public sealed record UserLookupRecord(string Email, string Role);
    public sealed record UserRecord(long Id, string Role, bool Suspended, bool Verified);
    public sealed record RecentRecord(long UserId, string Purpose, DateTime Since);
    public sealed record TokenLookupRecord(string TokenHash, string Purpose);
    public sealed record TokenRecord(string TokenHash, long UserId);
    public sealed record PasswordRecord(long UserId, string Hash);
    public sealed record UserIdRecord(long UserId);

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
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.AccountEmail, new AccountEmailMessage(userId.Value, AccountEmailKinds.PasswordChanged),
            OutboxJsonContext.Default.AccountEmailMessage);
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
        var recent = await conn.ExecuteScalarAsync<int>("""
            SELECT (SELECT count(*) FROM account_tokens WHERE user_id = @UserId AND purpose = @Purpose AND created_at > @Since)
                 + (SELECT count(*) FROM outbox WHERE type = 'account_email' AND processed_at IS NULL AND failed_at IS NULL
                        AND payload->>'user_id' = @UserId::text AND payload->>'kind' = @Purpose)
            """, new RecentRecord(userId, purpose, DateTime.UtcNow.AddHours(-1)), tx);
        if (recent >= AccountTokens.MaxRequestsPerHour) return false;
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.AccountEmail, new AccountEmailMessage(userId, purpose), OutboxJsonContext.Default.AccountEmailMessage);
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

public static class AccountRecoveryEndpoints
{
    const string InvalidLink = "This link is invalid or has expired. Ask for a new one.";

    public static void MapAccountRecovery(this RouteGroupBuilder auth, RouteGroupBuilder backofficeAuth, RouteGroupBuilder api)
    {
        auth.MapPost("/password/forgot", async (ForgotPasswordRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
        {
            await recovery.RequestPasswordResetAsync(req.Email, Roles.Customer, ct);
            return Results.Accepted();
        }).RequireRateLimiting(HostingExtensions.AuthRateLimit);
        backofficeAuth.MapPost("/password/forgot", async (ForgotPasswordRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
        {
            await recovery.RequestPasswordResetAsync(req.Email, Roles.Seller, ct);
            return Results.Accepted();
        }).RequireRateLimiting(HostingExtensions.AuthRateLimit);

        auth.MapGet("/password/reset", async (string? token, AccountRecoveryService recovery, CancellationToken ct) =>
            ApiResults.Ok(new TokenCheckResponse(await recovery.CheckAsync(token, AccountTokens.PasswordReset, ct)), AppJsonContext.Default.TokenCheckResponse));
        auth.MapPost("/password/reset", async (ResetPasswordRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
            await recovery.ResetPasswordAsync(req.Token, req.Password, ct) ? Results.NoContent() : ApiResults.BadRequest(InvalidLink))
            .RequireRateLimiting(HostingExtensions.AuthRateLimit);

        auth.MapPost("/email/verify", async (TokenRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
            await recovery.VerifyEmailAsync(req.Token, ct) ? Results.NoContent() : ApiResults.BadRequest(InvalidLink))
            .RequireRateLimiting(HostingExtensions.AuthRateLimit);
        auth.MapPost("/email/verification", async (UserInfo user, AccountRecoveryService recovery, CancellationToken ct) =>
            await recovery.ResendVerificationAsync(user.Id, ct) ? Results.Accepted() : ApiResults.Conflict("Your email address is already verified"))
            .RequireCustomer();

        api.MapPost("/backoffice/customers/{id:long}/password-reset", (long id, AccountRecoveryService recovery, CancellationToken ct) =>
            recovery.AdminPasswordResetAsync(id, ct)).RequireAdmin();
    }
}
