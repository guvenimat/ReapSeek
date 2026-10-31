using Dapper;
using Npgsql;
using ReapSeek.Domain.Common;
using ReapSeek.Domain.Marketing;

namespace ReapSeek.Shared.Marketing;

public static class CouponStore
{
    public sealed record CouponRecord(long Id, string Code, long? CustomerId, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt, Guid? UsedOrderId);
    public sealed record RedeemRecord(long Id, Guid OrderId, DateTime At);
    public sealed record CampaignRecord(
        long Id, string Name, long CohortId, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject, string Message, string Status);
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

    public static Task<int> IssueAsync(NpgsqlConnection conn, NpgsqlTransaction tx, IssueRecord issue) =>
        conn.ExecuteAsync("""
            INSERT INTO coupons (code, campaign_id, customer_id, kind, value, min_subtotal, max_discount, expires_at)
            SELECT c.code, @CampaignId, c.customer_id, @Kind, @Value, @MinSubtotal, @MaxDiscount, @ExpiresAt
            FROM unnest(@CustomerIds, @Codes) AS c(customer_id, code)
            WHERE EXISTS (SELECT 1 FROM users u WHERE u.id = c.customer_id AND u.role = 'customer')
            ON CONFLICT (campaign_id, customer_id) DO NOTHING
            """, issue, tx);
}
