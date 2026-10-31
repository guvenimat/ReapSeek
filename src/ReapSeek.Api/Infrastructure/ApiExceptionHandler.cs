using Microsoft.AspNetCore.Diagnostics;
using ReapSeek.Api.Payments;
using ReapSeek.Domain.Common;
using ReapSeek.Shared.Providers;
using ReapSeek.Shared;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Ordering;
using ReapSeek.Shared.Search;

namespace ReapSeek.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var (status, message, code) = exception switch
        {
            ValidationException e => (StatusCodes.Status400BadRequest, e.Message, null),
            DomainException e => (StatusCodes.Status400BadRequest, e.Message, e.Code),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message, null),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message, e.Code),
            InsufficientStockException => (StatusCodes.Status409Conflict, "An item in your cart just sold out. Please review your cart.", "out_of_stock"),
            PaymentProviderException => (StatusCodes.Status503ServiceUnavailable, "Payments are temporarily unavailable. Your order was not placed.", "payment_unavailable"),
            AudienceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "ReapDeep is not reachable, so audiences cannot be loaded right now.", "reapdeep_unavailable"),
            ShippingProviderException => (StatusCodes.Status503ServiceUnavailable, "The carrier is temporarily unavailable. Try shipping again in a moment.", "shipping_unavailable"),
            BadHttpRequestException e => (e.StatusCode, "Invalid request body", (string?)null),
            OpenSearchException or HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Search is temporarily unavailable", null),
            _ => (0, "", null),
        };
        if (status == 0) return false;
        if (exception is OpenSearchException or HttpRequestException) log.LogError(exception, "Search request failed");

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse(message, code), AppJsonContext.Default.ErrorResponse, cancellationToken: ct);
        return true;
    }
}
