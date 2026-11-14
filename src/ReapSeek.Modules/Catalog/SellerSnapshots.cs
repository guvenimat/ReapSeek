using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Catalog;

public sealed record SellerSnapshotRecord(long Id, string Name, string Slug, string City, string Status, decimal Rating);
public sealed record SellerSnapshotsRecord(long[] Ids, string[] Names, string[] Slugs, string[] Cities, string[] Statuses, decimal[] Ratings);
public sealed record SellerIdRecord(long SellerId);
public sealed record SellerIdsRecord(long[] SellerIds);
public sealed record SellerOfferCountRecord(long SellerId, long ProductCount, long OfferCount);

public static class SellerSnapshots
{
    public static Task UpsertAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, IReadOnlyCollection<SellerSnapshotRecord> sellers) =>
        sellers.Count == 0 ? Task.CompletedTask : conn.ExecuteAsync("""
            INSERT INTO catalog.seller_snapshots (id, name, slug, city, status, rating)
            SELECT * FROM unnest(@Ids, @Names, @Slugs, @Cities, @Statuses, @Ratings)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, slug = excluded.slug, city = excluded.city, status = excluded.status, rating = excluded.rating
            """, new SellerSnapshotsRecord(sellers.Select(s => s.Id).ToArray(), sellers.Select(s => s.Name).ToArray(), sellers.Select(s => s.Slug).ToArray(), sellers.Select(s => s.City).ToArray(),
            sellers.Select(s => s.Status).ToArray(), sellers.Select(s => s.Rating).ToArray()), tx);

    public static async Task<List<long>> ProductIdsOfSellerAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long sellerId) =>
        (await conn.QueryAsync<long>("""
            SELECT DISTINCT v.product_id FROM catalog.offers o JOIN catalog.product_variants v ON v.id = o.variant_id WHERE o.seller_id = @SellerId
            """, new SellerIdRecord(sellerId), tx)).AsList();

    public static async Task<Dictionary<long, SellerOfferCountRecord>> OfferCountsAsync(NpgsqlConnection conn, long[] sellerIds) =>
        (await conn.QueryAsync<SellerOfferCountRecord>("""
            SELECT o.seller_id, count(DISTINCT v.product_id) AS product_count, count(*) AS offer_count
            FROM catalog.offers o JOIN catalog.product_variants v ON v.id = o.variant_id
            WHERE o.seller_id = ANY(@SellerIds)
            GROUP BY o.seller_id
            """, new SellerIdsRecord(sellerIds))).ToDictionary(r => r.SellerId);
}
