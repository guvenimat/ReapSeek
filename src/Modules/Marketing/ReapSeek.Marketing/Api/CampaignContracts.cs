using ReapSeek.Shared.Analytics;

namespace ReapSeek.Marketing.Api;

internal sealed record CampaignRequest(
    string? Name, long? CohortId, string? Kind, decimal? Value, decimal? MinSubtotal, decimal? MaxDiscount, int? ValidDays, string? Subject, string? Message);
internal sealed record CampaignRow(
    long Id, string Name, long CohortId, string CohortName, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject,
    string Message, string Status, int Recipients, int Redeemed, decimal Revenue, string? Error, DateTime CreatedAt, DateTime? SentAt);
internal sealed record CampaignsResponse(List<CampaignRow> Campaigns);
internal sealed record CohortsListResponse(List<AudienceCohort> Cohorts);
internal sealed record AudienceRequest(long? CohortId);
internal sealed record AudiencePreviewResponse(long CohortId, int Customers, int Unreachable);
internal sealed record ReachRecord(long[] Ids);
internal sealed record CustomerCoupon(string Code, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt, string? Campaign);
internal sealed record CustomerCouponsResponse(List<CustomerCoupon> Coupons);
