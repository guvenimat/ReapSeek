using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;
using ReapSeek.Modules.Engagement;
using ReapSeek.Modules.Marketing;
using ReapSeek.Modules.Ordering;
using ReapSeek.Identity.Api;
using ReapSeek.Shared.Analytics;

namespace ReapSeek.Identity.Infrastructure;

internal enum SuspensionChange { Changed, NotFound, Unchanged }

internal sealed class CustomerRepository(NpgsqlDataSource db)
{
    internal sealed record ListRecord(string? Pattern, string? Status);
    internal sealed record ListRow(long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt);
    internal sealed record IdRecord(long Id);
    internal sealed record ProfileRecord(long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, string? SuspendedReason, DateTime? EmailVerifiedAt);
    internal sealed record SuspendRecord(long Id, string? Reason);

    public static async Task<Dictionary<long, string>> ProductTitlesAsync(NpgsqlConnection conn, long[] ids) =>
        (await CatalogLookups.ProductsAsync(conn, ids)).ToDictionary(p => p.Key, p => p.Value.Title);

    public async Task<(List<CustomerRow> Rows, int Total)> ListAsync(string? pattern, string sort, string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var people = (await conn.QueryAsync<ListRow>("""
            SELECT u.id, u.name, u.email, u.created_at, u.suspended_at FROM identity.users u
            WHERE u.role = 'customer'
              AND (@Pattern::text IS NULL OR u.email ILIKE @Pattern OR u.name ILIKE @Pattern)
              AND (@Status::text IS NULL OR (@Status = 'suspended') = (u.suspended_at IS NOT NULL))
            """, new ListRecord(pattern, status))).AsList();
        var stats = await CustomerOrders.StatsAsync(conn, people.Select(p => p.Id));
        var rows = people.Select(u => stats.TryGetValue(u.Id, out var o)
            ? new CustomerRow(u.Id, u.Name, u.Email, u.CreatedAt, u.SuspendedAt, o.Orders, o.Spent, o.LastOrderAt)
            : new CustomerRow(u.Id, u.Name, u.Email, u.CreatedAt, u.SuspendedAt, 0, 0, null));
        var sorted = CustomerQuery.Sort(rows, sort).ToList();
        return (sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList(), sorted.Count);
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>("SELECT id FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id)) is not null;
    }

    public async Task<CustomerDetail?> DetailAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new IdRecord(id);
        var profile = await conn.QuerySingleOrDefaultAsync<ProfileRecord>(
            "SELECT id, name, email, created_at, suspended_at, suspended_reason, email_verified_at FROM users WHERE id = @Id AND role = 'customer'", args);
        if (profile is null) return null;
        var order = (await CustomerOrders.StatsAsync(conn, [id])).GetValueOrDefault(id);
        var returnTotals = await CustomerOrders.ReturnTotalsAsync(conn, id);
        var stats = new CustomerStats(order?.Orders ?? 0, order?.Spent ?? 0, returnTotals.Refunded, order?.AverageOrder ?? 0, returnTotals.Returns,
            await ReviewStats.CountForCustomerAsync(conn, id), order?.FirstOrderAt, order?.LastOrderAt);
        var orders = (await CustomerOrders.RecentAsync(conn, id, 20)).Select(o => new CustomerOrderRow(o.Number, o.Status, o.Total, o.Packages, o.PlacedAt)).ToList();
        var returns = (await CustomerOrders.RecentReturnsAsync(conn, id, 10))
            .Select(r => new CustomerReturnRow(r.Number, r.OrderNumber, r.Status, r.Reason, r.RefundAmount, r.RequestedAt)).ToList();
        var recentReviews = await ReviewStats.RecentForCustomerAsync(conn, id, 10);
        var titles = await ProductTitlesAsync(conn, recentReviews.Select(r => r.ProductId).ToArray());
        var reviews = recentReviews.Select(r => new CustomerReviewRow(r.ProductId, titles.GetValueOrDefault(r.ProductId) ?? "", r.Rating, r.Title, r.Status, r.CreatedAt)).ToList();
        var recentCoupons = await CouponStore.RecentForCustomerAsync(conn, id, 10);
        var numbers = await CustomerOrders.NumbersAsync(conn, recentCoupons.Where(c => c.UsedOrderId is not null).Select(c => c.UsedOrderId!.Value));
        var coupons = recentCoupons.Select(c => new CustomerCouponRow(c.Code, c.Campaign, c.Kind, c.Value, c.ExpiresAt,
            c.UsedOrderId is { } used ? numbers.GetValueOrDefault(used) : null)).ToList();
        return new CustomerDetail(profile.Id, profile.Name, profile.Email, profile.CreatedAt, profile.SuspendedAt, profile.SuspendedReason, profile.EmailVerifiedAt,
            ReapDeepIdentity.DistinctId(id), stats, orders, returns, reviews, coupons);
    }

    public async Task<(SuspensionChange Change, string[] RevokedSessions)> SuspendAsync(long id, string reason, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var changed = await conn.ExecuteAsync("""
            UPDATE users SET suspended_at = now(), suspended_reason = @Reason WHERE id = @Id AND role = 'customer' AND suspended_at IS NULL
            """, new SuspendRecord(id, reason), tx);
        if (changed == 0)
            return (await conn.ExecuteScalarAsync<long?>("SELECT id FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id), tx) is null
                ? SuspensionChange.NotFound : SuspensionChange.Unchanged, []);
        var revoked = (await conn.QueryAsync<string>("DELETE FROM sessions WHERE user_id = @Id RETURNING token_hash", new IdRecord(id), tx)).ToArray();
        await tx.CommitAsync(ct);
        return (SuspensionChange.Changed, revoked);
    }

    public async Task<SuspensionChange> ReinstateAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var changed = await conn.ExecuteAsync("""
            UPDATE users SET suspended_at = NULL, suspended_reason = NULL WHERE id = @Id AND role = 'customer' AND suspended_at IS NOT NULL
            """, new IdRecord(id));
        if (changed == 1) return SuspensionChange.Changed;
        return await conn.ExecuteScalarAsync<long?>("SELECT id FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id)) is null
            ? SuspensionChange.NotFound : SuspensionChange.Unchanged;
    }
}
