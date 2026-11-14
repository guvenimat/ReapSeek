using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Sellers;

public sealed record SellerInfo(long Id, string Name, string Slug, string Status, decimal CommissionRate);
public sealed record SellerInfoIdRecord(long Id);
public sealed record SellerInfoIdsRecord(long[] Ids);

public static class SellerDirectory
{
    const string Columns = "SELECT id, name, slug, status, commission_rate FROM sellers.sellers";

    public static Task<SellerInfo?> FindAsync(NpgsqlConnection conn, long sellerId, NpgsqlTransaction? tx = null) =>
        conn.QuerySingleOrDefaultAsync<SellerInfo>(Columns + " WHERE id = @Id", new SellerInfoIdRecord(sellerId), tx);

    public static async Task<Dictionary<long, SellerInfo>> ManyAsync(NpgsqlConnection conn, IEnumerable<long> sellerIds, NpgsqlTransaction? tx = null)
    {
        var ids = sellerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<SellerInfo>(Columns + " WHERE id = ANY(@Ids)", new SellerInfoIdsRecord(ids), tx)).ToDictionary(s => s.Id);
    }
}
