namespace ReapSeek.Worker.Outbox;

public static class OutboxPolicy
{
    public const int MaxAttempts = 10;
    public const int MaxErrorLength = 2000;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    public static DateTime? RetryAt(int attempts, DateTime now)
    {
        if (attempts >= MaxAttempts) return null;
        var seconds = Math.Min(BaseDelay.TotalSeconds * Math.Pow(2, attempts - 1), MaxDelay.TotalSeconds);
        return now.AddSeconds(seconds);
    }

    public static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}
