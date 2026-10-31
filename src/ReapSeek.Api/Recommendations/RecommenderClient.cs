using System.Globalization;
using System.Net.Http.Json;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Recommendations;

public sealed class RecommenderClient : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    readonly HttpClient? _http;
    readonly string _publicKey;

    public RecommenderClient(IConfiguration cfg)
    {
        _publicKey = cfg["ReapDeep:PublicKey"] ?? "";
        if (cfg["ReapDeep:RecommendUrl"] is { Length: > 0 } url && _publicKey.Length > 0)
            _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = Timeout };
    }

    public bool IsConfigured => _http is not null;

    public async Task<List<RecommenderItem>> GetAsync(string model, string? itemId, int limit, CancellationToken ct)
    {
        var http = _http ?? throw new InvalidOperationException("ReapDeep recommender is not configured");
        var query = $"recommend?api_key={Uri.EscapeDataString(_publicKey)}&model={model}&limit={limit.ToString(CultureInfo.InvariantCulture)}"
            + (itemId is null ? "" : $"&item_id={Uri.EscapeDataString(itemId)}");
        var response = await http.GetFromJsonAsync(query, AppJsonContext.Default.RecommenderResponse, ct)
            ?? throw new HttpRequestException("Empty recommender response");
        return response.Items;
    }

    public void Dispose() => _http?.Dispose();
}
