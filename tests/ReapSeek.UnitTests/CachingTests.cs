using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Caching;

namespace ReapSeek.UnitTests;

public class CachingTests
{
    static RedisCache Cache(string? connectionString) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = connectionString }).Build(),
        NullLogger<RedisCache>.Instance);

    static RedisFixedWindowLimiter Limiter(RedisCache cache, int permits) => new(cache, "ratelimit:test:1.2.3.4", permits, TimeSpan.FromMinutes(1),
        new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }),
        NullLogger.Instance);

    [Fact]
    public async Task Without_redis_the_local_limit_applies()
    {
        using var cache = Cache(null);
        using var limiter = Limiter(cache, 2);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        Assert.False((await limiter.AcquireAsync()).IsAcquired);
    }

    [Fact]
    public async Task Without_redis_reads_miss_and_loads_from_the_source()
    {
        using var cache = Cache(null);
        var loads = 0;
        var user = new UserInfo(1, "a@example.com", "A", "customer");
        for (var i = 0; i < 2; i++)
            Assert.Equal(user, await cache.GetOrLoadAsync("k", CacheJsonContext.Default.UserInfo, TimeSpan.FromMinutes(1), () => { loads++; return Task.FromResult<UserInfo?>(user); }));
        Assert.Equal(2, loads);
        Assert.False(await cache.RemoveAsync("k"));
    }

    [Fact]
    public void Keys_are_namespaced()
    {
        Assert.Equal("product:red-dress-p1", CacheKeys.Product("red-dress-p1"));
        Assert.StartsWith(CacheKeys.ProductPrefix, CacheKeys.Product("x"));
        Assert.Equal("ratelimit:auth:10.0.0.1", CacheKeys.RateLimit("auth", "10.0.0.1"));
    }
}
