using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Domain.Marketing;
using ReapSeek.Shared;
using ReapSeek.Shared.Analytics;

namespace ReapSeek.Api.Marketing;

public sealed record CampaignRequest(
    string? Name, long? CohortId, string? Kind, decimal? Value, decimal? MinSubtotal, decimal? MaxDiscount, int? ValidDays, string? Subject, string? Message);
public sealed record CampaignRow(
    long Id, string Name, long CohortId, string CohortName, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject,
    string Message, string Status, int Recipients, int Redeemed, decimal Revenue, string? Error, DateTime CreatedAt, DateTime? SentAt);
public sealed record CampaignsResponse(List<CampaignRow> Campaigns);
public sealed record CohortsListResponse(List<AudienceCohort> Cohorts);
public sealed record AudienceRequest(long? CohortId);
public sealed record AudiencePreviewResponse(long CohortId, int Customers, int Unreachable);
public sealed record ReachRecord(long[] Ids);
public sealed record CustomerCoupon(string Code, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt, string? Campaign);
public sealed record CustomerCouponsResponse(List<CustomerCoupon> Coupons);

public static class CampaignEndpoints
{
    public static void MapCampaigns(this RouteGroupBuilder api)
    {
        api.MapGet("/account/coupons", async (UserInfo user, CampaignRepository campaigns, TimeProvider clock, CancellationToken ct) =>
            ApiResults.Ok(new CustomerCouponsResponse(await campaigns.CustomerCouponsAsync(user.Id, clock.GetUtcNow().UtcDateTime, ct)),
                AppJsonContext.Default.CustomerCouponsResponse)).RequireCustomer();

        var admin = api.MapGroup("/backoffice/marketing").RequireAdmin();
        admin.MapGet("/cohorts", async (ReapDeepAudience audience, CancellationToken ct) =>
            ApiResults.Ok(new CohortsListResponse(await audience.CohortsAsync(ct)), AppJsonContext.Default.CohortsListResponse));
        admin.MapPost("/audience", async (AudienceRequest req, ReapDeepAudience audience, CampaignRepository campaigns, CancellationToken ct) =>
        {
            Ensure.That(req.CohortId is > 0, "cohort_id is required");
            var ids = await audience.CustomersInCohortAsync(req.CohortId!.Value, ct);
            var (reachable, known) = await campaigns.ReachAsync([.. ids], ct);
            return ApiResults.Ok(new AudiencePreviewResponse(req.CohortId.Value, reachable, known - reachable), AppJsonContext.Default.AudiencePreviewResponse);
        });
        admin.MapGet("/campaigns", async (CampaignRepository campaigns, CancellationToken ct) =>
            ApiResults.Ok(new CampaignsResponse(await campaigns.ListAsync(ct)), AppJsonContext.Default.CampaignsResponse));
        admin.MapPost("/campaigns", Create);
        admin.MapPost("/campaigns/{id:long}/send", async (long id, CampaignRepository campaigns, CancellationToken ct) =>
            ApiResults.Ok(await campaigns.StartSendingAsync(id, ct), AppJsonContext.Default.CampaignRow));
    }

    static async Task<IResult> Create(CampaignRequest req, CampaignRepository campaigns, ReapDeepAudience audience, CancellationToken ct)
    {
        var name = Ensure.NotBlank(req.Name, "name", 100);
        Ensure.That(req.CohortId is > 0, "cohort_id is required");
        var kind = Ensure.OneOf(req.Kind ?? "", "kind", ["percent", "fixed"]);
        Ensure.That(req.Value is > 0, "value is required");
        var value = Math.Round(req.Value!.Value, 2, MidpointRounding.AwayFromZero);
        var min = Math.Round(req.MinSubtotal ?? 0, 2, MidpointRounding.AwayFromZero);
        var cap = req.MaxDiscount is { } m ? Math.Round(m, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
        Coupon.ValidateTerms(kind == "percent" ? DiscountKind.Percent : DiscountKind.Fixed, value, min, cap);
        var days = Ensure.InRange(req.ValidDays ?? 14, 1, 90, "valid_days");
        var subject = Ensure.NotBlank(req.Subject, "subject", 120);
        var message = Ensure.NotBlank(req.Message, "message", 2000);
        var cohort = (await audience.CohortsAsync(ct)).FirstOrDefault(c => c.Id == req.CohortId) ?? throw new ValidationException("cohort_id is not a ReapDeep cohort");
        var created = await campaigns.CreateAsync(new CampaignRepository.CampaignInsertRecord(name, cohort.Id, cohort.Name, kind, value, min, cap, days, subject, message), ct);
        return ApiResults.Created(created, AppJsonContext.Default.CampaignRow);
    }
}
