using Dapper;
using Npgsql;
using ReapSeek.Sellers.Api;

namespace ReapSeek.Sellers.Infrastructure;

internal sealed class SellerPanelRepository(NpgsqlDataSource db)
{
    public async Task<SellerAccount?> AccountAsync(long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerAccount>(
            "SELECT id, name, slug, city, status, commission_rate, rating FROM sellers.sellers WHERE id = @Id", new { Id = sellerId });
    }
}
