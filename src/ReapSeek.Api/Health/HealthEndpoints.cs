using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Health;

public sealed record HealthResponse(string Status, string Database);

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app) => app.MapGet("/healthz", Check);

    static async Task<IResult> Check(NpgsqlDataSource db, ILogger<HealthResponse> log, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            return ApiResults.Ok(new HealthResponse("ok", "ok"), AppJsonContext.Default.HealthResponse);
        }
        catch (NpgsqlException ex)
        {
            log.LogWarning("Health check failed: {Message}", ex.Message);
            return Results.Json(new HealthResponse("degraded", "unreachable"), AppJsonContext.Default.HealthResponse,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
