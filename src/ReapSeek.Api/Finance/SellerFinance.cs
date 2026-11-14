using Dapper;
using Npgsql;
using ReapSeek.Domain.Ledger;
using ReapSeek.Modules.Sellers;

namespace ReapSeek.Api.Finance;

public sealed record BalanceSummary(decimal InTransit, decimal Pending, decimal Available, decimal PaidOut, DateTime NextPayoutAt);
public sealed record LedgerRow(long Id, string OrderNumber, string Kind, decimal Amount, DateTime AvailableAt, long? PayoutId, DateTime CreatedAt);
public sealed record PayoutRow(long Id, long SellerId, string SellerName, DateTime PeriodEnd, decimal Amount, int EntryCount, string? TransferReference, DateTime CreatedAt);
public sealed record SellerBalanceResponse(BalanceSummary Summary, List<LedgerRow> Entries, List<PayoutRow> Payouts);
public sealed record PayoutsResponse(List<PayoutRow> Payouts);

public sealed class SellerFinanceRepository(NpgsqlDataSource db)
{
    public sealed record BalanceRecord(decimal Pending, decimal Available, decimal PaidOut);
    public sealed record PayoutRecord(long Id, long SellerId, DateTime PeriodEnd, decimal Amount, int EntryCount, string? TransferReference, DateTime CreatedAt);

    public async Task<SellerBalanceResponse> BalanceAsync(long sellerId, DateTime nowUtc, decimal inTransit, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new { SellerId = sellerId, Now = nowUtc };
        var balance = await conn.QuerySingleAsync<BalanceRecord>("""
            SELECT coalesce(sum(amount) FILTER (WHERE payout_id IS NULL AND available_at > @Now), 0) AS pending,
                coalesce(sum(amount) FILTER (WHERE payout_id IS NULL AND available_at <= @Now), 0) AS available,
                coalesce(sum(amount) FILTER (WHERE payout_id IS NOT NULL), 0) AS paid_out
            FROM finance.seller_ledger WHERE seller_id = @SellerId
            """, args);
        var entries = (await conn.QueryAsync<LedgerRow>("""
            SELECT id, order_number, kind, amount, available_at, payout_id, created_at FROM finance.seller_ledger
            WHERE seller_id = @SellerId ORDER BY created_at DESC, id DESC LIMIT 100
            """, args)).AsList();
        var payouts = await PayoutsAsync(conn, sellerId);
        var next = PayoutPeriod.CurrentPeriodEnd(nowUtc, PayoutPeriod.Turkey).AddDays(7);
        return new SellerBalanceResponse(new BalanceSummary(inTransit, balance.Pending, balance.Available, balance.PaidOut, next), entries, payouts);
    }

    public async Task<List<PayoutRow>> PayoutsAsync(long? sellerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await PayoutsAsync(conn, sellerId);
    }

    static async Task<List<PayoutRow>> PayoutsAsync(NpgsqlConnection conn, long? sellerId)
    {
        var rows = (await conn.QueryAsync<PayoutRecord>("""
            SELECT p.id, p.seller_id, p.period_end, p.amount, p.entry_count, p.transfer_reference, p.created_at
            FROM finance.payouts p
            WHERE @SellerId::bigint IS NULL OR p.seller_id = @SellerId
            ORDER BY p.period_end DESC, p.id DESC LIMIT 100
            """, new { SellerId = sellerId })).AsList();
        var sellers = await SellerDirectory.ManyAsync(conn, rows.Select(r => r.SellerId));
        return rows.Select(r => new PayoutRow(r.Id, r.SellerId, sellers.GetValueOrDefault(r.SellerId)?.Name ?? "", r.PeriodEnd, r.Amount, r.EntryCount,
            r.TransferReference, r.CreatedAt)).ToList();
    }
}

public sealed class SellerFinance(SellerFinanceRepository finance)
{
    public Task<SellerBalanceResponse> BalanceAsync(long sellerId, DateTime nowUtc, decimal inTransit, CancellationToken ct) =>
        finance.BalanceAsync(sellerId, nowUtc, inTransit, ct);

    public Task<List<PayoutRow>> PayoutsAsync(long? sellerId, CancellationToken ct) => finance.PayoutsAsync(sellerId, ct);
}
