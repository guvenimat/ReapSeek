using Npgsql;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Sellers.Contracts;

namespace ReapSeek.Sellers.Infrastructure;

internal sealed record SellerVersionRecord(long Id, string Name, string Slug, string City, string Status, decimal Rating, long Version);

internal static class SellerChanges
{
    static readonly ModuleOutbox Outbox = new(MessagingModules.Sellers);

    public static Task PublishAsync(NpgsqlConnection conn, NpgsqlTransaction tx, SellerVersionRecord s) =>
        Outbox.PublishAsync(conn, tx, SellerChangedV1.EventName, new SellerChangedV1(s.Id, s.Name, s.Slug, s.City, s.Status, s.Rating, s.Version),
            SellersEventsJsonContext.Default.SellerChangedV1);
}
