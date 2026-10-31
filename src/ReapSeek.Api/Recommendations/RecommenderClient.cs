using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Recommendations;

public sealed class RecommenderClient : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan SignatureLifetime = TimeSpan.FromMinutes(5);

    readonly HttpClient? _http;
    readonly string _publicKey;
    readonly string _secret;
    readonly TimeProvider _clock;

    public RecommenderClient(IConfiguration cfg, TimeProvider clock)
    {
        _clock = clock;
        _publicKey = cfg["ReapDeep:PublicKey"] ?? "";
        _secret = cfg["ReapDeep:RecommendationSecret"] ?? "";
        if (cfg["ReapDeep:RecommendUrl"] is { Length: > 0 } url && _publicKey.Length > 0)
            _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = Timeout };
    }

    public bool IsConfigured => _http is not null;

    public bool CanPersonalize => _http is not null && _secret.Length > 0;

    public static string Sign(string secret, string distinctId, long expires) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{distinctId}.{expires}")));

    public async Task<List<RecommenderItem>> GetAsync(string model, string? itemId, int limit, CancellationToken ct, string? distinctId = null)
    {
        var http = _http ?? throw new InvalidOperationException("ReapDeep recommender is not configured");
        var query = $"recommend?api_key={Uri.EscapeDataString(_publicKey)}&model={model}&limit={limit.ToString(CultureInfo.InvariantCulture)}"
            + (itemId is null ? "" : $"&item_id={Uri.EscapeDataString(itemId)}");
        if (distinctId is not null)
        {
            if (_secret.Length == 0) throw new InvalidOperationException("ReapDeep recommendation secret is not configured");
            var expires = (_clock.GetUtcNow() + SignatureLifetime).ToUnixTimeSeconds();
            query += $"&distinct_id={Uri.EscapeDataString(distinctId)}&expires={expires.ToString(CultureInfo.InvariantCulture)}&signature={Sign(_secret, distinctId, expires)}";
        }
        var response = await http.GetFromJsonAsync(query, AppJsonContext.Default.RecommenderResponse, ct)
            ?? throw new HttpRequestException("Empty recommender response");
        return response.Items;
    }

    public void Dispose() => _http?.Dispose();
}
