using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace ReapSeek.Simulator;

public sealed record SimOffer(long ProductId, long OfferId, long VariantId, long SellerId, string SellerName, string Sku, string VariantLabel, string? ImageUrl, decimal Price);

public sealed class Catalog
{
    public sealed record Row(
        long Id, string Title, string Brand, string CategoryPath, string CategoryName, string Department, long OfferId, long VariantId, long SellerId,
        string SellerName, string Sku, string VariantLabel, string? ImageUrl, decimal Price);

    public required List<SimProduct> Products { get; init; }
    public required Dictionary<long, SimOffer> Offers { get; init; }
    public required Dictionary<long, SimProduct> ById { get; init; }

    public static async Task<Catalog> LoadAsync(NpgsqlConnection conn)
    {
        var rows = (await conn.QueryAsync<Row>("""
            SELECT p.id, p.title, b.name AS brand, c.path AS category_path, c.name AS category_name, split_part(c.path, '/', 1) AS department,
                bb.offer_id, bb.variant_id, bb.seller_id, bb.seller_name, bb.sku,
                coalesce((SELECT string_agg(e.key || ': ' || e.value, ', ' ORDER BY e.key) FROM jsonb_each_text(bb.options) e), '') AS variant_label,
                (SELECT i.url FROM product_images i WHERE i.product_id = p.id ORDER BY i.position LIMIT 1) AS image_url, bb.price
            FROM products p JOIN brands b ON b.id = p.brand_id JOIN categories c ON c.id = p.category_id
            JOIN LATERAL (
                SELECT o.id AS offer_id, v.id AS variant_id, s.id AS seller_id, s.name AS seller_name, v.sku, v.options, o.price
                FROM product_variants v JOIN offers o ON o.variant_id = v.id JOIN sellers s ON s.id = o.seller_id
                WHERE v.product_id = p.id AND o.status = 'active' AND o.stock > 0 AND s.status = 'active'
                ORDER BY o.price, s.rating DESC, o.handling_days, o.id LIMIT 1
            ) bb ON true
            WHERE p.status = 'active' AND c.depth > 0
            ORDER BY p.id
            """)).AsList();
        var products = rows.Select(r => new SimProduct(r.Id, r.Title, r.Brand, r.CategoryPath, r.CategoryName, r.Department)).ToList();
        return new Catalog
        {
            Products = products,
            ById = products.ToDictionary(p => p.Id),
            Offers = rows.ToDictionary(r => r.Id, r => new SimOffer(r.Id, r.OfferId, r.VariantId, r.SellerId, r.SellerName, r.Sku, r.VariantLabel, r.ImageUrl, r.Price)),
        };
    }
}

public sealed class Ingest(string url, string publicKey) : IDisposable
{
    const int BatchSize = 500;
    readonly HttpClient _http = new() { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
    readonly List<string> _pending = [];

    public bool Enabled => publicKey.Length > 0;
    public int Sent { get; private set; }

    public async Task AddAsync(string evt, string distinctId, string eventId, DateTime at, IReadOnlyDictionary<string, object?> props)
    {
        if (!Enabled) return;
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("event", evt);
            w.WriteString("distinct_id", distinctId);
            w.WriteString("event_id", eventId);
            w.WriteString("timestamp", DateTime.SpecifyKind(at, DateTimeKind.Utc));
            w.WriteStartObject("properties");
            foreach (var (key, value) in props)
            {
                switch (value)
                {
                    case null: w.WriteNull(key); break;
                    case string s: w.WriteString(key, s); break;
                    case bool b: w.WriteBoolean(key, b); break;
                    case int i: w.WriteNumber(key, i); break;
                    case long l: w.WriteNumber(key, l); break;
                    case decimal d: w.WriteNumber(key, d); break;
                    case double d: w.WriteNumber(key, d); break;
                    default: w.WriteString(key, value.ToString()); break;
                }
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        _pending.Add(Encoding.UTF8.GetString(buffer.ToArray()));
        if (_pending.Count >= BatchSize) await FlushAsync();
    }

    public async Task FlushAsync()
    {
        if (_pending.Count == 0) return;
        var body = $"{{\"api_key\":{JsonSerializer.Serialize(publicKey, SimJsonContext.Default.String)},\"sent_at\":\"{DateTime.UtcNow:O}\",\"batch\":[{string.Join(',', _pending)}]}}";
        for (var attempt = 1; ; attempt++)
        {
            using var res = await _http.PostAsync("capture", new StringContent(body, Encoding.UTF8, "application/json"));
            if (res.IsSuccessStatusCode) break;
            if (attempt == 5) throw new HttpRequestException($"ReapDeep ingestion returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
        }
        Sent += _pending.Count;
        _pending.Clear();
    }

    public void Dispose() => _http.Dispose();
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
public sealed partial class SimJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
