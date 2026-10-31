using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Domain.Marketing;
using ReapSeek.Shared;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Outbox;

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
    public sealed record CampaignInsertRecord(
        string Name, long CohortId, string CohortName, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject, string Message);

    const string Select = """
        SELECT c.id, c.name, c.cohort_id, c.cohort_name, c.kind, c.value, c.min_subtotal, c.max_discount, c.valid_days, c.subject, c.message, c.status,
            c.recipients,
            (SELECT count(*)::int FROM coupons k JOIN orders o ON o.id = k.used_order_id WHERE k.campaign_id = c.id AND o.status = 'paid') AS redeemed,
            (SELECT coalesce(sum(o.total), 0) FROM coupons k JOIN orders o ON o.id = k.used_order_id WHERE k.campaign_id = c.id AND o.status = 'paid') AS revenue,
            c.error, c.created_at, c.sent_at
        FROM campaigns c
        """;

    public static void MapCampaigns(this RouteGroupBuilder api)
    {
        api.MapGet("/account/coupons", async (UserInfo user, NpgsqlDataSource db, TimeProvider clock, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var rows = await conn.QueryAsync<CustomerCoupon>("""
                SELECT k.code, k.kind, k.value, k.min_subtotal, k.max_discount, k.expires_at, c.name AS campaign
                FROM coupons k LEFT JOIN campaigns c ON c.id = k.campaign_id
                WHERE k.customer_id = @CustomerId AND k.used_order_id IS NULL AND k.expires_at > @Now ORDER BY k.expires_at
                """, new { CustomerId = user.Id, Now = clock.GetUtcNow().UtcDateTime });
            return ApiResults.Ok(new CustomerCouponsResponse(rows.AsList()), AppJsonContext.Default.CustomerCouponsResponse);
        }).RequireCustomer();

        var admin = api.MapGroup("/backoffice/marketing").RequireAdmin();
        admin.MapGet("/cohorts", async (ReapDeepAudience audience, CancellationToken ct) =>
            ApiResults.Ok(new CohortsListResponse(await audience.CohortsAsync(ct)), AppJsonContext.Default.CohortsListResponse));
        admin.MapPost("/audience", async (AudienceRequest req, ReapDeepAudience audience, NpgsqlDataSource db, CancellationToken ct) =>
        {
            Ensure.That(req.CohortId is > 0, "cohort_id is required");
            var ids = await audience.CustomersInCohortAsync(req.CohortId!.Value, ct);
            await using var conn = await db.OpenConnectionAsync(ct);
            var reachable = await conn.ExecuteScalarAsync<int>("""
                SELECT count(*)::int FROM users WHERE id = ANY(@Ids) AND role = 'customer' AND email_verified_at IS NOT NULL AND suspended_at IS NULL
                """, new ReachRecord([.. ids]));
            var known = await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM users WHERE id = ANY(@Ids) AND role = 'customer'", new ReachRecord([.. ids]));
            return ApiResults.Ok(new AudiencePreviewResponse(req.CohortId.Value, reachable, known - reachable), AppJsonContext.Default.AudiencePreviewResponse);
        });
        admin.MapGet("/campaigns", async (NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            return ApiResults.Ok(new CampaignsResponse((await conn.QueryAsync<CampaignRow>(Select + " ORDER BY c.created_at DESC LIMIT 100")).AsList()),
                AppJsonContext.Default.CampaignsResponse);
        });
        admin.MapPost("/campaigns", Create);
        admin.MapPost("/campaigns/{id:long}/send", async (long id, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var status = await conn.ExecuteScalarAsync<string?>("SELECT status FROM campaigns WHERE id = @Id FOR UPDATE", new { Id = id }, tx)
                ?? throw new NotFoundException("Campaign not found");
            if (status is not ("draft" or "failed")) throw new ConflictException("This campaign was already sent", "campaign_sent");
            await conn.ExecuteAsync("UPDATE campaigns SET status = 'sending', error = NULL WHERE id = @Id", new { Id = id }, tx);
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CampaignSend, new CampaignMessage(id, null), OutboxJsonContext.Default.CampaignMessage);
            await tx.CommitAsync(ct);
            return ApiResults.Ok((await conn.QuerySingleAsync<CampaignRow>(Select + " WHERE c.id = @Id", new { Id = id })), AppJsonContext.Default.CampaignRow);
        });
    }

    static async Task<IResult> Create(CampaignRequest req, NpgsqlDataSource db, ReapDeepAudience audience, CancellationToken ct)
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

        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO campaigns (name, cohort_id, cohort_name, kind, value, min_subtotal, max_discount, valid_days, subject, message)
            VALUES (@Name, @CohortId, @CohortName, @Kind, @Value, @MinSubtotal, @MaxDiscount, @ValidDays, @Subject, @Message) RETURNING id
            """, new CampaignInsertRecord(name, cohort.Id, cohort.Name, kind, value, min, cap, days, subject, message));
        return ApiResults.Created(await conn.QuerySingleAsync<CampaignRow>(Select + " WHERE c.id = @Id", new { Id = id }), AppJsonContext.Default.CampaignRow);
    }
}
