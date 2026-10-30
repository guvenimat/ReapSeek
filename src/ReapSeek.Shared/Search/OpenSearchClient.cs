using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace ReapSeek.Shared.Search;

public sealed class OpenSearchException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class OpenSearchClient : IDisposable
{
    readonly HttpClient? _http;

    public OpenSearchClient(IConfiguration cfg)
    {
        var url = cfg["OpenSearch:Url"];
        if (string.IsNullOrWhiteSpace(url)) return;
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    public bool IsConfigured => _http is not null;

    public Task<JsonNode?> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

    public Task<JsonNode?> PostAsync(string path, JsonNode body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);

    public Task<JsonNode?> PutAsync(string path, string json, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, path, new StringContent(json, Encoding.UTF8, "application/json"), ct);

    public Task<JsonNode?> DeleteAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Delete, path, null, ct);

    public Task<JsonNode?> BulkAsync(string path, string ndjson, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson"), ct);

    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        using var res = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return false;
        if (!res.IsSuccessStatusCode) throw new OpenSearchException((int)res.StatusCode, $"HEAD {path} failed with {(int)res.StatusCode}");
        return true;
    }

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path) { Content = content };
        using var res = await Http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new OpenSearchException((int)res.StatusCode, $"{method} {path} failed with {(int)res.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
        return body.Length == 0 ? null : JsonNode.Parse(body);
    }

    HttpClient Http => _http ?? throw new InvalidOperationException("OpenSearch:Url is not configured");

    public void Dispose() => _http?.Dispose();
}
