using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Ordering;

public sealed record GuestCartCutoffRecord(DateTime Cutoff);

public static class CartMaintenance
{
    public static Task<int> PurgeGuestCartsAsync(NpgsqlConnection conn, DateTime cutoff) =>
        conn.ExecuteAsync("DELETE FROM ordering.carts WHERE customer_id IS NULL AND updated_at < @Cutoff", new GuestCartCutoffRecord(cutoff));
}
