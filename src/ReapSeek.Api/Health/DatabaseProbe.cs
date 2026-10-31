using Dapper;
using Npgsql;

namespace ReapSeek.Api.Health;

public sealed class DatabaseProbe(NpgsqlDataSource db, ILogger<DatabaseProbe> log)
{
    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            return true;
        }
        catch (NpgsqlException ex)
        {
            log.LogWarning("Health check failed: {Message}", ex.Message);
            return false;
        }
    }
}
