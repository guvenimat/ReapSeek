using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ReapSeek.Shared.Caching;

public static class CacheKeys
{
    public const string ProductPrefix = "product:";
    public static string Product(string slug) => ProductPrefix + slug;
    public const string Categories = "catalog:categories";
    public static string Session(string tokenHash) => $"session:{tokenHash}";
    public static string RateLimit(string policy, string partition) => $"ratelimit:{policy}:{partition}";
}

public sealed class RedisCache : IDisposable
{
    readonly ConnectionMultiplexer? _redis;
    readonly ILogger<RedisCache> _log;

    public RedisCache(IConfiguration cfg, ILogger<RedisCache> log)
    {
        _log = log;
        if (cfg.GetConnectionString("Redis") is not { Length: > 0 } connectionString)
        {
            log.LogWarning("ConnectionStrings:Redis is not set; caching and shared rate limits are disabled");
            return;
        }
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.SyncTimeout = 1000;
        options.AsyncTimeout = 1000;
        _redis = ConnectionMultiplexer.Connect(options);
    }

    public IDatabase? Database => _redis?.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, JsonTypeInfo<T> type) where T : class
    {
        if (Database is not { } db) return null;
        try
        {
            var value = await db.StringGetAsync(key);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize((byte[])value!, type);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            _log.LogWarning("Cache read of {Key} failed: {Message}", key, ex.Message);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, JsonTypeInfo<T> type, TimeSpan ttl)
    {
        if (Database is not { } db) return;
        try
        {
            await db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, type), ttl);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _log.LogWarning("Cache write of {Key} failed: {Message}", key, ex.Message);
        }
    }

    public async Task<T?> GetOrLoadAsync<T>(string key, JsonTypeInfo<T> type, TimeSpan ttl, Func<Task<T?>> load) where T : class
    {
        if (await GetAsync(key, type) is { } cached) return cached;
        var value = await load();
        if (value is not null) await SetAsync(key, value, type, ttl);
        return value;
    }

    public async Task<bool> RemoveAsync(params string[] keys)
    {
        if (keys.Length == 0) return true;
        if (Database is not { } db) return false;
        try
        {
            await db.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
            return true;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _log.LogError("Cache invalidation of {Count} key(s) failed, entries stay until they expire: {Message}", keys.Length, ex.Message);
            return false;
        }
    }

    public async Task<bool> RemoveByPrefixAsync(string prefix)
    {
        if (_redis is null) return false;
        try
        {
            var db = _redis.GetDatabase();
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (server.IsReplica) continue;
                var batch = new List<RedisKey>();
                await foreach (var key in server.KeysAsync(pattern: prefix + "*", pageSize: 500))
                {
                    batch.Add(key);
                    if (batch.Count == 500) { await db.KeyDeleteAsync(batch.ToArray()); batch.Clear(); }
                }
                if (batch.Count > 0) await db.KeyDeleteAsync(batch.ToArray());
            }
            return true;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _log.LogError("Cache invalidation of {Prefix}* failed, entries stay until they expire: {Message}", prefix, ex.Message);
            return false;
        }
    }

    public void Dispose() => _redis?.Dispose();
}
