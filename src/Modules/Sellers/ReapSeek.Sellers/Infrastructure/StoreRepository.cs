using Dapper;
using Npgsql;

namespace ReapSeek.Sellers.Infrastructure;

internal sealed class StoreRepository(NpgsqlDataSource db)
{
    public sealed record SlugRecord(string Slug);
    public sealed record SellerRecord(long Id, string Name, string Slug, string City, decimal Rating, DateTime CreatedAt);

    public async Task<SellerRecord?> SellerAsync(string slug, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerRecord>("""
            SELECT s.id, s.name, s.slug, s.city, s.rating, s.created_at
            FROM sellers.sellers s WHERE s.slug = @Slug AND s.status = 'active'
            """, new SlugRecord(slug));
    }
}
