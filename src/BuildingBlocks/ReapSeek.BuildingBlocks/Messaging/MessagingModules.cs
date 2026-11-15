namespace ReapSeek.BuildingBlocks.Messaging;

public sealed record MessagingModule(string Name, int Ordinal)
{
    public string Outbox => $"{Name}.outbox";
    public string Inbox => $"{Name}.inbox";
    public long FirstInboxId => Ordinal * MessagingModules.InboxIdRange + 1;
    public string JobType(string handler) => $"{Name}/{handler}";
}

public static class MessagingModules
{
    public const long InboxIdRange = 1_000_000_000_000_000;

    public static readonly MessagingModule Identity = new("identity", 1);
    public static readonly MessagingModule Catalog = new("catalog", 2);
    public static readonly MessagingModule Sellers = new("sellers", 3);
    public static readonly MessagingModule Ordering = new("ordering", 4);
    public static readonly MessagingModule Finance = new("finance", 5);
    public static readonly MessagingModule Engagement = new("engagement", 6);
    public static readonly MessagingModule Marketing = new("marketing", 7);

    public static readonly IReadOnlyList<MessagingModule> All = [Identity, Catalog, Sellers, Ordering, Finance, Engagement, Marketing];

    public static MessagingModule Get(string name) =>
        All.SingleOrDefault(m => m.Name == name) ?? throw new ArgumentException($"Unknown messaging module: {name}", nameof(name));

    public static MessagingModule? OfInboxId(long id) => id < InboxIdRange ? null : All.SingleOrDefault(m => m.Ordinal == id / InboxIdRange);
}
