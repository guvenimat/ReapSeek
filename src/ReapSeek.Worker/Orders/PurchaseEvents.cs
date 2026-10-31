using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ReapSeek.Domain.Ordering;
using ReapSeek.Shared.Analytics;

namespace ReapSeek.Worker.Orders;

public sealed record PurchaseProperties(
    string ItemId, string OrderId, decimal Price, int Quantity, decimal Amount, string Currency, long SellerId, string Sku, string Source);
public sealed record CaptureEvent(string Event, string DistinctId, string EventId, DateTime Timestamp, PurchaseProperties Properties);
public sealed record CaptureRequest(string ApiKey, DateTime SentAt, List<CaptureEvent> Batch);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CaptureRequest))]
public sealed partial class CaptureJsonContext : JsonSerializerContext;

public static class PurchaseEvents
{
    public static List<CaptureEvent> For(Order order) =>
        order.Packages.SelectMany(p => p.Lines.Select(l => new CaptureEvent(
            "purchase",
            ReapDeepIdentity.DistinctId(order.CustomerId),
            $"order-line-{l.Id}",
            order.PaidAt ?? order.PlacedAt,
            new PurchaseProperties(l.ProductId.ToString(), order.Number, l.UnitPrice.Amount, l.Quantity, l.Total.Amount, l.UnitPrice.Currency,
                p.SellerId, l.Sku, "server")))).ToList();
}

public sealed class ReapDeepIngestClient : IDisposable
{
    readonly HttpClient? _http;
    readonly string _publicKey = "";

    public ReapDeepIngestClient(IConfiguration cfg)
    {
        var url = cfg["ReapDeep:IngestUrl"];
        var key = cfg["ReapDeep:PublicKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;
        _publicKey = key;
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public bool IsConfigured => _http is not null;

    public async Task CaptureAsync(List<CaptureEvent> events, CancellationToken ct)
    {
        if (_http is null || events.Count == 0) return;
        using var res = await _http.PostAsJsonAsync("capture", new CaptureRequest(_publicKey, DateTime.UtcNow, events), CaptureJsonContext.Default.CaptureRequest, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"ReapDeep ingestion returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
    }

    public void Dispose() => _http?.Dispose();
}
