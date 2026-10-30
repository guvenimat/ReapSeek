using Microsoft.AspNetCore.Diagnostics;
using ReapSeek.Shared;

namespace ReapSeek.Api.Infrastructure;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var (status, message) = exception switch
        {
            ValidationException e => (StatusCodes.Status400BadRequest, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message),
            BadHttpRequestException e => (e.StatusCode, "Invalid request body"),
            _ => (0, ""),
        };
        if (status == 0) return false;

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse(message), AppJsonContext.Default.ErrorResponse, cancellationToken: ct);
        return true;
    }
}
