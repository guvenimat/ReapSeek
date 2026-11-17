using ReapSeek.Domain.Ledger;
using ReapSeek.Modules.Finance;
using ReapSeek.Ordering.Contracts;

namespace ReapSeek.Finance.Application;

internal static class LedgerEntries
{
    public static LedgerInsertRecord ForDelivery(PackageDeliveredV1 e, TimeSpan hold)
    {
        var entries = Settlement.ForPackage(e.Subtotal, e.ShippingFee, e.CommissionRate);
        return new LedgerInsertRecord(e.SellerId, e.PackageId, null, e.OrderNumber, entries.Select(x => x.Kind).ToArray(), entries.Select(x => x.Amount).ToArray(),
            Settlement.AvailableAt(e.DeliveredAt, hold));
    }

    public static LedgerInsertRecord ForRefund(ReturnRefundedV1 e)
    {
        var entries = Settlement.ForRefund(e.GrossAmount, e.CommissionRate);
        return new LedgerInsertRecord(e.SellerId, e.PackageId, e.ReturnId, e.OrderNumber, entries.Select(x => x.Kind).ToArray(), entries.Select(x => x.Amount).ToArray(),
            e.RefundedAt);
    }
}
