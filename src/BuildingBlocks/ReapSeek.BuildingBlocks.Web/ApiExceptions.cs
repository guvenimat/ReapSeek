namespace ReapSeek.BuildingBlocks.Web;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Content = "content";
}
