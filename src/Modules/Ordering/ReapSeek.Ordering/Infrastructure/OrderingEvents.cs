using Npgsql;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;
using ReapSeek.Ordering.Contracts;

namespace ReapSeek.Ordering.Infrastructure;

internal static class OrderingEvents
{
    static readonly ModuleOutbox Outbox = new(MessagingModules.Ordering);

    public static Task PackageDeliveredAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order order, SellerPackage package, decimal commissionRate) =>
        Outbox.PublishAsync(conn, tx, PackageDeliveredV1.EventName,
            new PackageDeliveredV1(package.Id, order.Number, package.SellerId, package.Subtotal.Amount, package.ShippingFee.Amount, commissionRate,
                package.DeliveredAt ?? throw new InvalidOperationException($"Package {package.Id} has no delivery time")),
            OrderingEventsJsonContext.Default.PackageDeliveredV1);

    public static Task ReturnRefundedAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ReturnRequest r) =>
        Outbox.PublishAsync(conn, tx, ReturnRefundedV1.EventName,
            new ReturnRefundedV1(r.Id, r.PackageId, r.SellerId, r.OrderNumber, r.Total.Amount, r.CommissionRate,
                r.RefundedAt ?? throw new InvalidOperationException($"Return {r.Number} has no refund time")),
            OrderingEventsJsonContext.Default.ReturnRefundedV1);
}
