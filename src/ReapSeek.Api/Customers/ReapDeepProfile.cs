using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Analytics;

namespace ReapSeek.Api.Customers;

public sealed record RfmProfile(string Segment, string SegmentName, int R, int F, long RecencyDays, long Orders, double Monetary, int WindowDays);
public sealed record ActivityRow(string Event, DateTime Timestamp, string? ItemId, string? ProductTitle, string? Detail);
public sealed record CustomerAnalytics(string Status, RfmProfile? Rfm, List<ActivityRow> Activity);

public sealed record ReapDeepRfm(int WindowDays, string? Segment, string? SegmentName, int? R, int? F, long? RecencyDays, long? Orders, double? Monetary);
public sealed record ReapDeepEvent(string Event, DateTime Timestamp, string DistinctId, JsonElement Columns, JsonElement Properties);
public sealed record ReapDeepEvents(List<ReapDeepEvent> Events);
public sealed record ReapDeepPerson(string Id, string[] DistinctIds);
public sealed record ReapDeepPersons(List<ReapDeepPerson> Persons);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ReapDeepRfm))]
[JsonSerializable(typeof(ReapDeepEvents))]
[JsonSerializable(typeof(ReapDeepPersons))]
public sealed partial class ProfileJsonContext : JsonSerializerContext;

public static class ActivityRules
{
    public static string? ItemId(ReapDeepEvent e) =>
        Text(e.Columns, "item_id") ?? Text(e.Properties, "item_id");

    public static string? Detail(ReapDeepEvent e) => e.Event switch
    {
        "search_performed" => Text(e.Properties, "query") is { } q ? $"\"{q}\"" : null,
        "purchase" or "refund" => Text(e.Properties, "order_id") is { } o ? $"{o}, {Text(e.Properties, "amount")} {Text(e.Properties, "currency")}".TrimEnd(',', ' ') : null,
        "add_to_cart" => Text(e.Properties, "quantity") is { } n ? $"x{n}" : null,
        "review_submitted" => Text(e.Properties, "rating") is { } r ? $"{r} stars" : null,
        "coupon_applied" => Text(e.Properties, "code"),
        "$pageview" => Text(e.Properties, "$pathname") ?? Text(e.Properties, "path"),
        _ => null,
    };

    static string? Text(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()
            : null;
}

public sealed class ReapDeepProfile : IDisposable
{
    const int ActivityLimit = 25;
    readonly HttpClient? _http;
    readonly NpgsqlDataSource _db;
    readonly ILogger<ReapDeepProfile> _log;

    public sealed record ProductIdsRecord(long[] Ids);
    public sealed record ProductTitleRecord(long Id, string Title);

    public ReapDeepProfile(IConfiguration cfg, NpgsqlDataSource db, ILogger<ReapDeepProfile> log)
    {
        _db = db;
        _log = log;
        var url = cfg["ReapDeep:ApiUrl"];
        var key = cfg["ReapDeep:ReadKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public async Task<CustomerAnalytics> ForCustomerAsync(long customerId, CancellationToken ct)
    {
        if (_http is null) return new CustomerAnalytics("not_connected", null, []);
        var distinctId = ReapDeepIdentity.DistinctId(customerId);
        try
        {
            var rfm = await RfmAsync(distinctId, ct);
            var events = await EventsAsync($"distinct_id={Uri.EscapeDataString(distinctId)}", ct);
            if (rfm?.Segment is null && await PersonIdAsync(distinctId, ct) is { } personId)
            {
                rfm = await RfmAsync(personId, ct);
                events = await EventsAsync($"person_id={Uri.EscapeDataString(personId)}", ct);
            }
            return new CustomerAnalytics("ok", Profile(rfm), await ActivityAsync(events, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning("ReapDeep profile for {Customer} failed: {Message}", distinctId, ex.Message);
            return new CustomerAnalytics("unavailable", null, []);
        }
    }

    static RfmProfile? Profile(ReapDeepRfm? r) =>
        r is { Segment: { } s, SegmentName: { } n, R: { } rr, F: { } f, RecencyDays: { } d, Orders: { } o, Monetary: { } m }
            ? new RfmProfile(s, n, rr, f, d, o, m, r.WindowDays) : null;

    Task<ReapDeepRfm?> RfmAsync(string person, CancellationToken ct) =>
        _http!.GetFromJsonAsync($"api/v1/persons/{Uri.EscapeDataString(person)}/rfm", ProfileJsonContext.Default.ReapDeepRfm, ct);

    async Task<List<ReapDeepEvent>> EventsAsync(string filter, CancellationToken ct) =>
        (await _http!.GetFromJsonAsync($"api/v1/events?{filter}&limit={ActivityLimit}", ProfileJsonContext.Default.ReapDeepEvents, ct))?.Events ?? [];

    async Task<string?> PersonIdAsync(string distinctId, CancellationToken ct)
    {
        var page = await _http!.GetFromJsonAsync($"api/v1/persons?search={Uri.EscapeDataString(distinctId)}&limit=20", ProfileJsonContext.Default.ReapDeepPersons, ct);
        return page?.Persons.FirstOrDefault(p => p.DistinctIds.Contains(distinctId, StringComparer.Ordinal))?.Id;
    }

    async Task<List<ActivityRow>> ActivityAsync(List<ReapDeepEvent> events, CancellationToken ct)
    {
        var ids = events.Select(ActivityRules.ItemId).Select(i => long.TryParse(i, out var id) ? id : 0).Where(id => id > 0).Distinct().ToArray();
        var titles = new Dictionary<long, string>();
        if (ids.Length > 0)
        {
            await using var conn = await _db.OpenConnectionAsync(ct);
            foreach (var p in await conn.QueryAsync<ProductTitleRecord>("SELECT id, title FROM products WHERE id = ANY(@Ids)", new ProductIdsRecord(ids)))
                titles[p.Id] = p.Title;
        }
        return events.OrderByDescending(e => e.Timestamp).Select(e =>
        {
            var item = ActivityRules.ItemId(e);
            return new ActivityRow(e.Event, e.Timestamp, item, long.TryParse(item, out var id) ? titles.GetValueOrDefault(id) : null, ActivityRules.Detail(e));
        }).ToList();
    }

    public void Dispose() => _http?.Dispose();
}
