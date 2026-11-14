using Dapper;
using Npgsql;

namespace ReapSeek.Api.Sellers.SellerPanel;

public sealed class SellerPanelRepository(NpgsqlDataSource db)
{
    public async Task<SellerAccount?> AccountAsync(long sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerAccount>(
            "SELECT id, name, slug, city, status, commission_rate, rating FROM sellers.sellers WHERE id = @Id", new { Id = sellerId });
    }
}
