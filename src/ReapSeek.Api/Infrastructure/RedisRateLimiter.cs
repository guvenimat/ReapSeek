using System.Threading.RateLimiting;
using ReapSeek.Shared.Caching;
using StackExchange.Redis;

namespace ReapSeek.Api.Infrastructure;

public sealed class RedisFixedWindowLimiter(RedisCache redis, string key, int permitLimit, TimeSpan window, RateLimiter fallback, ILogger log) : RateLimiter
{
    const string Script = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        return { count, redis.call('PTTL', KEYS[1]) }
        """;

    long _lastUsedTicks = DateTime.UtcNow.Ticks;

    public override TimeSpan? IdleDuration => DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Lease.Pending;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken ct)
    {
        Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
        if (redis.Database is not { } db) return await fallback.AcquireAsync(permitCount, ct);
        try
        {
            var result = (RedisResult[])(await db.ScriptEvaluateAsync(Script, [key], [(long)window.TotalMilliseconds]))!;
            var count = (long)result[0];
            return count <= permitLimit ? Lease.Acquired : Lease.Rejected(TimeSpan.FromMilliseconds(Math.Max(0, (long)result[1])));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            log.LogWarning("Shared rate limit unavailable, using the local limit: {Message}", ex.Message);
            return await fallback.AcquireAsync(permitCount, ct);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) fallback.Dispose();
        base.Dispose(disposing);
    }

    sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public static readonly Lease Pending = new(false, null);
        public static readonly Lease Acquired = new(true, null);
        public static Lease Rejected(TimeSpan retryAfter) => new(false, retryAfter);

        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = metadataName == MetadataName.RetryAfter.Name ? retryAfter : null;
            return metadata is not null;
        }
    }
}
