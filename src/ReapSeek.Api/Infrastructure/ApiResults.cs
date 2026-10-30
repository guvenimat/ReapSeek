using System.Text.Json.Serialization.Metadata;

namespace ReapSeek.Api.Infrastructure;

public sealed record ErrorResponse(string Error, string? Code = null);

public static class ApiResults
{
    public static IResult Ok<T>(T value, JsonTypeInfo<T> type) => Results.Json(value, type);

    public static IResult Created<T>(T value, JsonTypeInfo<T> type) => Results.Json(value, type, statusCode: StatusCodes.Status201Created);

    public static IResult Error(int status, string message) =>
        Results.Json(new ErrorResponse(message), AppJsonContext.Default.ErrorResponse, statusCode: status);

    public static IResult BadRequest(string message) => Error(StatusCodes.Status400BadRequest, message);

    public static IResult Unauthorized(string message) => Error(StatusCodes.Status401Unauthorized, message);

    public static IResult Forbidden(string message) => Error(StatusCodes.Status403Forbidden, message);

    public static IResult NotFound(string message) => Error(StatusCodes.Status404NotFound, message);

    public static IResult Conflict(string message) => Error(StatusCodes.Status409Conflict, message);
}
