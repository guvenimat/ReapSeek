namespace ReapSeek.Finance.Contracts;

public sealed record BalanceSummary(decimal InTransit, decimal Pending, decimal Available, decimal PaidOut, DateTime NextPayoutAt);
public sealed record LedgerRow(long Id, string OrderNumber, string Kind, decimal Amount, DateTime AvailableAt, long? PayoutId, DateTime CreatedAt);
public sealed record PayoutRow(long Id, long SellerId, string SellerName, DateTime PeriodEnd, decimal Amount, int EntryCount, string? TransferReference, DateTime CreatedAt);
public sealed record SellerBalanceResponse(BalanceSummary Summary, List<LedgerRow> Entries, List<PayoutRow> Payouts);
public sealed record PayoutsResponse(List<PayoutRow> Payouts);
public sealed record PayoutRunResult(DateTime PeriodEnd, int Created, int Transferred, int TransferFailures);

public interface ISellerFinance
{
    Task<SellerBalanceResponse> BalanceAsync(long sellerId, DateTime nowUtc, decimal inTransit, CancellationToken ct);
    Task<List<PayoutRow>> PayoutsAsync(long? sellerId, CancellationToken ct);
}
