namespace ReapSeek.Marketing.Contracts;

public sealed record CustomerCoupon(string Code, string Campaign, string Kind, decimal Value, DateTime ExpiresAt, Guid? UsedOrderId);
