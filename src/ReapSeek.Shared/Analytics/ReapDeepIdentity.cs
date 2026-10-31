namespace ReapSeek.Shared.Analytics;

public static class ReapDeepIdentity
{
    public static string DistinctId(long customerId) => $"customer-{customerId}";
}
