using Npgsql;
using ReapSeek.Modules.Finance;

namespace ReapSeek.Finance.Infrastructure;

internal sealed class LedgerRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(LedgerInsertRecord entries, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LedgerStore.InsertAsync(conn, tx, entries);
        await tx.CommitAsync(ct);
    }
}
