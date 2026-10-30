using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Identity;

public sealed class AccountRepository(NpgsqlDataSource db)
{
    public sealed record CredentialsRecord(long Id, string PasswordHash, string Role);

    const string UniqueViolation = "23505";

    public async Task<CredentialsRecord?> FindCredentialsAsync(string email, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CredentialsRecord>(
            "SELECT id, password_hash, role FROM users WHERE email = @Email", new { Email = email });
    }

    public async Task<long?> CreateCustomerAsync(string email, string name, string passwordHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var id = await conn.ExecuteScalarAsync<long>("""
                INSERT INTO users (email, name, password_hash, role) VALUES (@Email, @Name, @Hash, 'customer') RETURNING id
                """, new { Email = email, Name = name, Hash = passwordHash }, tx);
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.UserRegistered, new UserRegistered(id, email, name),
                OutboxJsonContext.Default.UserRegistered);
            await tx.CommitAsync(ct);
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public async Task<UserInfo?> FindSessionAsync(string tokenHash, string audience, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserInfo>("""
            SELECT u.id, u.email, u.name, u.role FROM sessions s JOIN users u ON u.id = s.user_id
            WHERE s.token_hash = @TokenHash AND s.audience = @Audience AND s.expires_at > now()
            """, new { TokenHash = tokenHash, Audience = audience });
    }

    public async Task CreateSessionAsync(long userId, string tokenHash, string audience, DateTime expiresAt, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            DELETE FROM sessions WHERE user_id = @UserId AND expires_at < now();
            INSERT INTO sessions (token_hash, user_id, audience, expires_at) VALUES (@TokenHash, @UserId, @Audience, @ExpiresAt);
            """, new { UserId = userId, TokenHash = tokenHash, Audience = audience, ExpiresAt = expiresAt });
    }

    public async Task DeleteSessionAsync(string tokenHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("DELETE FROM sessions WHERE token_hash = @TokenHash", new { TokenHash = tokenHash });
    }

    public async Task<bool> ChangePasswordAsync(long userId, Func<string?, bool> verifyCurrent, string newHash, string keepTokenHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var current = await conn.ExecuteScalarAsync<string?>(
            "SELECT password_hash FROM users WHERE id = @UserId FOR UPDATE", new { UserId = userId }, tx);
        if (!verifyCurrent(current)) return false;

        await conn.ExecuteAsync("""
            UPDATE users SET password_hash = @Hash WHERE id = @UserId;
            DELETE FROM sessions WHERE user_id = @UserId AND token_hash <> @Keep;
            """, new { UserId = userId, Hash = newHash, Keep = keepTokenHash }, tx);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> CreateFirstAdminAsync(string email, string name, string passwordHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long?>("""
            INSERT INTO users (email, name, password_hash, role)
            SELECT @Email, @Name, @Hash, 'admin' WHERE NOT EXISTS (SELECT 1 FROM users WHERE role = 'admin')
            ON CONFLICT (email) DO NOTHING
            RETURNING id
            """, new { Email = email, Name = name, Hash = passwordHash });
        return id is not null;
    }

    public async Task<bool> AdminExistsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM users WHERE role = 'admin')");
    }
}
