using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Health;

public sealed record HealthResponse(string Status, string Database, string Search);

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/healthz", Check);
        app.MapGet("/livez", () => Results.Text("ok"));
    }

    static async Task<IResult> Check(DatabaseProbe database, OpenSearchClient search, CancellationToken ct)
    {
        var searchStatus = await SearchStatusAsync(search, ct);
        return await database.IsReachableAsync(ct)
            ? ApiResults.Ok(new HealthResponse("ok", "ok", searchStatus), AppJsonContext.Default.HealthResponse)
            : Results.Json(new HealthResponse("degraded", "unreachable", searchStatus), AppJsonContext.Default.HealthResponse,
                statusCode: StatusCodes.Status503ServiceUnavailable);
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
