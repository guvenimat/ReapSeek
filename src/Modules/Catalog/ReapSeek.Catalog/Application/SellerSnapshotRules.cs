using ReapSeek.Sellers.Contracts;

namespace ReapSeek.Catalog.Application;

internal sealed record SellerSnapshotStateRecord(long Id, string Name, string Slug, string City, string Status, decimal Rating, long Version)
{
    public static SellerSnapshotStateRecord From(SellerChangedV1 change) =>
        new(change.SellerId, change.Name, change.Slug, change.City, change.Status, change.Rating, change.Version);
}

internal static class SellerSnapshotRules
{
    public static bool Supersedes(SellerSnapshotStateRecord? current, SellerSnapshotStateRecord next) =>
        current is null || next.Version > current.Version;

    public static bool ChangesProducts(SellerSnapshotStateRecord? current, SellerSnapshotStateRecord next) =>
        current is null || current with { Version = next.Version } != next;
}
