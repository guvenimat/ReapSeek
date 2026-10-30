using Microsoft.AspNetCore.Diagnostics;
using ReapSeek.Shared;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var (status, message) = exception switch
        {
            ValidationException e => (StatusCodes.Status400BadRequest, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message),
            BadHttpRequestException e => (e.StatusCode, "Invalid request body"),
            OpenSearchException or HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Search is temporarily unavailable"),
            _ => (0, ""),
        };
        if (status == 0) return false;
        if (status == StatusCodes.Status503ServiceUnavailable) log.LogError(exception, "Search request failed");

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse(message), AppJsonContext.Default.ErrorResponse, cancellationToken: ct);
        return true;
    }
}
