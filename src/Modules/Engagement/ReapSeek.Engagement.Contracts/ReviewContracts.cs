namespace ReapSeek.Engagement.Contracts;

public sealed record ReviewStat(long ProductId, double Rating, int Count);
public sealed record RecentReview(long Id, long CustomerId, long ProductId, int Rating);
public sealed record CustomerReview(long ProductId, int Rating, string Title, string Status, DateTime CreatedAt);
