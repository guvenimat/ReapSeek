using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ReapSeek.Worker.CatalogSync;

public sealed record ReapDeepItem(
    string ItemId, string Title, string Category, decimal? Price, string Currency, bool InStock, string Url, string ImageUrl,
    Dictionary<string, string> Attributes);
public sealed record ReapDeepItemsRequest(List<ReapDeepItem> Items);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ReapDeepItemsRequest))]
public sealed partial class ReapDeepJsonContext : JsonSerializerContext;

public sealed class ReapDeepClient : IDisposable
{
    readonly HttpClient? _http;

    public ReapDeepClient(IConfiguration cfg)
    {
        var url = cfg["ReapDeep:ApiUrl"];
        var key = cfg["ReapDeep:CatalogKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public bool IsConfigured => _http is not null;

    public async Task UpsertAsync(List<ReapDeepItem> items, CancellationToken ct)
    {
        using var res = await _http!.PutAsJsonAsync("api/v1/items", new ReapDeepItemsRequest(items), ReapDeepJsonContext.Default.ReapDeepItemsRequest, ct);
        await EnsureSuccessAsync(res, "upsert", ct);
    }

    public async Task DeleteAsync(string itemId, CancellationToken ct)
    {
        using var res = await _http!.DeleteAsync($"api/v1/items/{Uri.EscapeDataString(itemId)}", ct);
        if (res.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(res, "delete", ct);
    }

    static async Task EnsureSuccessAsync(HttpResponseMessage res, string action, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"ReapDeep catalog {action} failed with {(int)res.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
    }

    public void Dispose() => _http?.Dispose();
}
