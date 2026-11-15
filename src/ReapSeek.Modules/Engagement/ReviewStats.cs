using Dapper;
using Npgsql;
using ReapSeek.Engagement.Contracts;

namespace ReapSeek.Modules.Engagement;

public sealed record ReviewProductIdsRecord(long[] ProductIds);
public sealed record ReviewWindowRecord(int Days);
public sealed record ReviewCustomerRecord(long CustomerId);

public static class ReviewStats
{
    public static Task<int> CountForCustomerAsync(NpgsqlConnection conn, long customerId) =>
        conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM engagement.reviews WHERE customer_id = @CustomerId", new ReviewCustomerRecord(customerId));

    public static async Task<List<CustomerReview>> RecentForCustomerAsync(NpgsqlConnection conn, long customerId, int limit) =>
        (await conn.QueryAsync<CustomerReview>($"""
            SELECT product_id, rating, title, status, created_at FROM engagement.reviews
            WHERE customer_id = @CustomerId ORDER BY created_at DESC LIMIT {limit}
            """, new ReviewCustomerRecord(customerId))).AsList();

    public static async Task<List<RecentReview>> RecentVisibleAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, int days) =>
        (await conn.QueryAsync<RecentReview>("""
            SELECT id, customer_id, product_id, rating FROM engagement.reviews
            WHERE status = 'visible' AND updated_at >= now() - make_interval(days => @Days)
            """, new ReviewWindowRecord(days), tx)).AsList();

    public static async Task<Dictionary<long, ReviewStat>> ForProductsAsync(NpgsqlConnection conn, IEnumerable<long> productIds)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<ReviewStat>("""
            SELECT product_id, round(avg(rating), 1)::float8 AS rating, count(*)::int AS count
            FROM engagement.reviews WHERE product_id = ANY(@ProductIds) AND status = 'visible' GROUP BY product_id
            """, new ReviewProductIdsRecord(ids))).ToDictionary(r => r.ProductId);
    }
}
