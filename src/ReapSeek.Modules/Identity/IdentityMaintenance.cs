using Dapper;
using Npgsql;
using ReapSeek.Identity.Contracts;

namespace ReapSeek.Modules.Identity;

public sealed record IdentityPurgeRecord(DateTime Now, DateTime TokenCutoff);

public static class IdentityMaintenance
{
    public static Task<IdentityPurgeCounts> PurgeAsync(NpgsqlConnection conn, DateTime now, DateTime tokenCutoff) =>
        conn.QuerySingleAsync<IdentityPurgeCounts>("""
            WITH s AS (DELETE FROM identity.sessions WHERE expires_at < @Now RETURNING 1),
                 t AS (DELETE FROM identity.account_tokens WHERE expires_at < @TokenCutoff RETURNING 1)
            SELECT (SELECT count(*)::int FROM s) AS sessions, (SELECT count(*)::int FROM t) AS tokens
            """, new IdentityPurgeRecord(now, tokenCutoff));
}
