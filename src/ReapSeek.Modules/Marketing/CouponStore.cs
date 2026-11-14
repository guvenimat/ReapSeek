using Dapper;
using Npgsql;
using ReapSeek.Modules.Identity;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Marketing;

namespace ReapSeek.Modules.Marketing;

public static class CouponStore
{
    public sealed record CouponRecord(long Id, string Code, long? CustomerId, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt, Guid? UsedOrderId);
    public sealed record RedeemRecord(long Id, Guid OrderId, DateTime At);
    public sealed record CampaignRecord(
        long Id, string Name, long CohortId, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject, string Message, string Status);
    public sealed record CouponOrderRecord(Guid OrderId);
    public sealed record CustomerCoupon(string Code, string Campaign, string Kind, decimal Value, DateTime ExpiresAt, Guid? UsedOrderId);
    public sealed record CouponCustomerRecord(long CustomerId);
    public sealed record IssueRecord(long CampaignId, long[] CustomerIds, string[] Codes, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt);

    public static string KindName(DiscountKind kind) => kind == DiscountKind.Percent ? "percent" : "fixed";
    public static DiscountKind ParseKind(string name) => name == "percent" ? DiscountKind.Percent : DiscountKind.Fixed;

    public static async Task<(long Id, Coupon Coupon)?> FindAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string code, bool forUpdate)
    {
        var r = await conn.QuerySingleOrDefaultAsync<CouponRecord>(
            "SELECT id, code, customer_id, kind, value, min_subtotal, max_discount, expires_at, used_order_id FROM coupons WHERE code = @Code" + (forUpdate ? " FOR UPDATE" : ""),
            new { Code = code.Trim().ToUpperInvariant() }, tx);
        return r is null ? null : (r.Id, new Coupon(r.Code, r.CustomerId, ParseKind(r.Kind), r.Value, Money.Of(r.MinSubtotal), r.MaxDiscount is { } m ? Money.Of(m) : null,
            r.ExpiresAt, r.UsedOrderId));
    }

    public static async Task RedeemAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long couponId, Guid orderId, DateTime now)
    {
        var updated = await conn.ExecuteAsync("UPDATE coupons SET used_order_id = @OrderId, used_at = @At WHERE id = @Id AND used_order_id IS NULL",
            new RedeemRecord(couponId, orderId, now), tx);
        if (updated != 1) throw new DomainException("coupon.used", "This code has already been used");
    }

    public static async Task<CampaignRecord?> CampaignAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long id) =>
        await conn.QuerySingleOrDefaultAsync<CampaignRecord>("""
            SELECT id, name, cohort_id, kind, value, min_subtotal, max_discount, valid_days, subject, message, status FROM campaigns WHERE id = @Id
            """ + (tx is null ? "" : " FOR UPDATE"), new { Id = id }, tx);

    public static async Task<List<CustomerCoupon>> RecentForCustomerAsync(NpgsqlConnection conn, long customerId, int limit) =>
        (await conn.QueryAsync<CustomerCoupon>($"""
            SELECT c.code, m.name AS campaign, c.kind, c.value, c.expires_at, c.used_order_id
            FROM marketing.coupons c JOIN marketing.campaigns m ON m.id = c.campaign_id
            WHERE c.customer_id = @CustomerId ORDER BY c.created_at DESC LIMIT {limit}
            """, new CouponCustomerRecord(customerId))).AsList();

    public static Task ReleaseAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid orderId) =>
        conn.ExecuteAsync("UPDATE marketing.coupons SET used_order_id = NULL, used_at = NULL WHERE used_order_id = @OrderId", new CouponOrderRecord(orderId), tx);

    public static async Task<int> IssueAsync(NpgsqlConnection conn, NpgsqlTransaction tx, IssueRecord issue)
    {
        var reachable = await People.ReachableCustomersAsync(conn, issue.CustomerIds, tx);
        var keep = issue.CustomerIds.Select((id, i) => (id, i)).Where(x => reachable.Contains(x.id)).Select(x => x.i).ToArray();
        issue = issue with { CustomerIds = keep.Select(i => issue.CustomerIds[i]).ToArray(), Codes = keep.Select(i => issue.Codes[i]).ToArray() };
        return await conn.ExecuteAsync("""
            INSERT INTO coupons (code, campaign_id, customer_id, kind, value, min_subtotal, max_discount, expires_at)
            SELECT c.code, @CampaignId, c.customer_id, @Kind, @Value, @MinSubtotal, @MaxDiscount, @ExpiresAt
            FROM unnest(@CustomerIds, @Codes) AS c(customer_id, code)
            ON CONFLICT (campaign_id, customer_id) DO NOTHING
            """, issue, tx);
    }
}
