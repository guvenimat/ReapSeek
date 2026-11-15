using ReapSeek.Finance.Contracts;
using ReapSeek.Finance.Infrastructure;

namespace ReapSeek.Finance.Application;

internal sealed class SellerFinance(SellerFinanceRepository finance) : ISellerFinance
{
    public Task<SellerBalanceResponse> BalanceAsync(long sellerId, DateTime nowUtc, decimal inTransit, CancellationToken ct) =>
        finance.BalanceAsync(sellerId, nowUtc, inTransit, ct);

    public Task<List<PayoutRow>> PayoutsAsync(long? sellerId, CancellationToken ct) => finance.PayoutsAsync(sellerId, ct);
}
