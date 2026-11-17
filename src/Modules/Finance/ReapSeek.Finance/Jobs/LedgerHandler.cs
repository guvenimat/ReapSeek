using Microsoft.Extensions.Configuration;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Domain.Ledger;
using ReapSeek.Finance.Application;
using ReapSeek.Finance.Infrastructure;
using ReapSeek.Ordering.Contracts;

namespace ReapSeek.Finance.Jobs;

internal sealed class LedgerHandler(LedgerRepository ledger, IConfiguration cfg) : IInboxHandler<PackageDeliveredV1>, IInboxHandler<ReturnRefundedV1>
{
    public const string DeliveredHandler = "package-delivered-settle";
    public const string RefundedHandler = "return-refunded-settle";

    readonly TimeSpan _hold = TimeSpan.FromDays(cfg.GetValue("Ledger:HoldDays", Settlement.DefaultHold.TotalDays));

    public Task HandleAsync(PackageDeliveredV1 message, InboxMessage envelope, CancellationToken ct) =>
        ledger.InsertAsync(LedgerEntries.ForDelivery(message, _hold), ct);

    public Task HandleAsync(ReturnRefundedV1 message, InboxMessage envelope, CancellationToken ct) =>
        ledger.InsertAsync(LedgerEntries.ForRefund(message), ct);
}
