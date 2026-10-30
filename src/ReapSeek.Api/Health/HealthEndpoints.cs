using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Health;

public sealed record HealthResponse(string Status, string Database, string Search);

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app) => app.MapGet("/healthz", Check);

    static async Task<IResult> Check(NpgsqlDataSource db, OpenSearchClient search, ILogger<HealthResponse> log, CancellationToken ct)
    {
        var searchStatus = await SearchStatusAsync(search, ct);
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            return ApiResults.Ok(new HealthResponse("ok", "ok", searchStatus), AppJsonContext.Default.HealthResponse);
        }
        catch (NpgsqlException ex)
        {
            log.LogWarning("Health check failed: {Message}", ex.Message);
            return Results.Json(new HealthResponse("degraded", "unreachable", searchStatus), AppJsonContext.Default.HealthResponse,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    static async Task<string> SearchStatusAsync(OpenSearchClient search, CancellationToken ct)
    {
        if (!search.IsConfigured) return "not configured";
        try
        {
            return await search.ExistsAsync(ProductIndex.Alias, ct) ? "ok" : "indexing";
        }
        catch (Exception ex) when (ex is HttpRequestException or OpenSearchException or TaskCanceledException)
        {
            return "unreachable";
        }
    }
}
