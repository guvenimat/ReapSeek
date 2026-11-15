namespace ReapSeek.Marketing.Api;

internal sealed record AlertInsight(long Id, string Name);
internal sealed record ReapDeepAlertPayload(
    long AlertId, string AlertName, long ProjectId, string State, string Condition, double Threshold, string Window, double Value, double? PreviousValue,
    string Message, AlertInsight Insight, DateTime CheckedAt);
internal sealed record AlertSubscriptionRequest(long? AlertId, string? Secret);
internal sealed record AlertEventRow(
    long Id, long AlertId, string AlertName, string State, string Condition, double Threshold, string TimeWindow, double Value, double? PreviousValue,
    string Message, string InsightName, DateTime CheckedAt, DateTime ReceivedAt);
internal sealed record AlertsResponse(List<AlertEventRow> Events, int Subscriptions);
