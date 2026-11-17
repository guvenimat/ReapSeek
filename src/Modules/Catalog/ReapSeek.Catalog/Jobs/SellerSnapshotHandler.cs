using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Catalog.Application;
using ReapSeek.Catalog.Infrastructure;
using ReapSeek.Sellers.Contracts;

namespace ReapSeek.Catalog.Jobs;

internal sealed class SellerSnapshotHandler(SellerSnapshotRepository snapshots) : IInboxHandler<SellerChangedV1>
{
    public const string UpdateHandler = "seller-changed-update-snapshot";

    public Task HandleAsync(SellerChangedV1 message, InboxMessage envelope, CancellationToken ct) =>
        snapshots.ApplyAsync(SellerSnapshotStateRecord.From(message), ct);
}
