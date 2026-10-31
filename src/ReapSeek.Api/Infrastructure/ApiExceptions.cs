namespace ReapSeek.Api.Infrastructure;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

