using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.Domain.Ledger;
using ReapSeek.Shared.Providers;

namespace ReapSeek.Shared.Payouts;

public sealed record PayoutRunResult(DateTime PeriodEnd, int Created, int Transferred, int TransferFailures);

public sealed class PayoutRunner
{
    const int LockKey = 0x504159;

    public sealed record PendingTransferRecord(long Id, decimal Amount, string SellerName);
    public sealed record TransferDoneRecord(long Id, string Reference);
    public sealed record PeriodRecord(DateTime PeriodEnd, DateTime Now);

    readonly NpgsqlDataSource _db;
    readonly ILogger<PayoutRunner> _log;
    readonly IPayoutBank _bank;

    public PayoutRunner(NpgsqlDataSource db, IPayoutBank bank, ILogger<PayoutRunner> log)
    {
        _db = db;
        _bank = bank;
        _log = log;
    }

    public async Task<PayoutRunResult> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var periodEnd = PayoutPeriod.CurrentPeriodEnd(nowUtc, PayoutPeriod.Turkey);
        await using var conn = await _db.OpenConnectionAsync(ct);
        int created;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(@Key)", new { Key = LockKey }, tx);
            created = await conn.ExecuteScalarAsync<int>("""
                WITH due AS (
                    SELECT seller_id, sum(amount) AS amount, count(*) AS entries FROM seller_ledger
                    WHERE payout_id IS NULL AND (available_at < @PeriodEnd OR (return_id IS NOT NULL AND available_at <= @Now))
                    GROUP BY seller_id HAVING sum(amount) > 0
                ), created AS (
                    INSERT INTO payouts (seller_id, period_end, amount, entry_count)
                    SELECT seller_id, @PeriodEnd, amount, entries FROM due
                    ON CONFLICT (seller_id, period_end) DO NOTHING
                    RETURNING id, seller_id
                ), linked AS (
                    UPDATE seller_ledger l SET payout_id = c.id FROM created c
                    WHERE l.seller_id = c.seller_id AND l.payout_id IS NULL AND (l.available_at < @PeriodEnd OR (l.return_id IS NOT NULL AND l.available_at <= @Now))
                    RETURNING l.id
                )
                SELECT count(*) FROM created
                """, new PeriodRecord(periodEnd, nowUtc), tx);
            await tx.CommitAsync(ct);
        }

        var pending = (await conn.QueryAsync<PendingTransferRecord>("""
            SELECT p.id, p.amount, s.name AS seller_name FROM payouts p JOIN sellers s ON s.id = p.seller_id
            WHERE p.transfer_reference IS NULL ORDER BY p.id
            """)).AsList();
        int transferred = 0, failures = 0;
        foreach (var p in pending)
        {
            try
            {
                var transferId = await _bank.TransferAsync(p.Amount, "TRY", $"payout-{p.Id}", p.SellerName, ct);
                await conn.ExecuteAsync("UPDATE payouts SET transfer_reference = @Reference WHERE id = @Id", new TransferDoneRecord(p.Id, transferId));
                transferred++;
            }
            catch (PaymentProviderException ex)
            {
                failures++;
                _log.LogWarning("Transfer for payout {Payout} failed, retrying on the next run: {Message}", p.Id, ex.Message);
            }
        }
        if (created > 0 || transferred > 0)
            _log.LogInformation("Payouts for the period ending {PeriodEnd:u}: {Created} created, {Transferred} transferred", periodEnd, created, transferred);
        return new PayoutRunResult(periodEnd, created, transferred, failures);
    }

}
