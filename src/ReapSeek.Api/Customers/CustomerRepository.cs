using Dapper;
using Npgsql;
using ReapSeek.Shared.Analytics;

namespace ReapSeek.Api.Customers;

public enum SuspensionChange { Changed, NotFound, Unchanged }

public sealed class CustomerRepository(NpgsqlDataSource db)
{
    public sealed record ListRecord(string? Pattern, string? Status, int Limit, int Offset);
    public sealed record ListRow(long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, int Orders, decimal Spent, DateTime? LastOrderAt, int Total);
    public sealed record IdRecord(long Id);
    public sealed record ProfileRecord(long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, string? SuspendedReason, DateTime? EmailVerifiedAt);
    public sealed record SuspendRecord(long Id, string? Reason);

    public async Task<(List<CustomerRow> Rows, int Total)> ListAsync(string? pattern, string sort, string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<ListRow>($"""
            WITH stats AS (
                SELECT customer_id, count(*)::int AS orders, sum(total) AS spent, max(placed_at) AS last_order_at
                FROM orders WHERE status = 'paid' GROUP BY customer_id)
            SELECT u.id, u.name, u.email, u.created_at, u.suspended_at, coalesce(s.orders, 0) AS orders, coalesce(s.spent, 0) AS spent,
                s.last_order_at, (count(*) OVER ())::int AS total
            FROM users u LEFT JOIN stats s ON s.customer_id = u.id
            WHERE u.role = 'customer'
              AND (@Pattern::text IS NULL OR u.email ILIKE @Pattern OR u.name ILIKE @Pattern)
              AND (@Status::text IS NULL OR (@Status = 'suspended') = (u.suspended_at IS NOT NULL))
            ORDER BY {CustomerQuery.OrderBy(sort)}
            LIMIT @Limit OFFSET @Offset
            """, new ListRecord(pattern, status, pageSize, (page - 1) * pageSize))).AsList();
        return (rows.Select(r => new CustomerRow(r.Id, r.Name, r.Email, r.CreatedAt, r.SuspendedAt, r.Orders, r.Spent, r.LastOrderAt)).ToList(),
            rows.Count > 0 ? rows[0].Total : 0);
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
        var stats = await conn.QuerySingleAsync<CustomerStats>("""
            SELECT (SELECT count(*)::int FROM orders WHERE customer_id = @Id AND status = 'paid') AS orders,
                (SELECT coalesce(sum(total), 0) FROM orders WHERE customer_id = @Id AND status = 'paid') AS spent,
                (SELECT coalesce(sum(refund_amount), 0) FROM returns WHERE customer_id = @Id AND status = 'refunded') AS refunded,
                (SELECT coalesce(round(avg(total), 2), 0) FROM orders WHERE customer_id = @Id AND status = 'paid') AS average_order,
                (SELECT count(*)::int FROM returns WHERE customer_id = @Id) AS returns,
                (SELECT count(*)::int FROM reviews WHERE customer_id = @Id) AS reviews,
                (SELECT min(placed_at) FROM orders WHERE customer_id = @Id AND status = 'paid') AS first_order_at,
                (SELECT max(placed_at) FROM orders WHERE customer_id = @Id AND status = 'paid') AS last_order_at
            """, args);
        var orders = (await conn.QueryAsync<CustomerOrderRow>("""
            SELECT o.number, o.status, o.total, (SELECT count(*)::int FROM order_packages p WHERE p.order_id = o.id) AS packages, o.placed_at
            FROM orders o WHERE o.customer_id = @Id ORDER BY o.placed_at DESC LIMIT 20
            """, args)).AsList();
        var returns = (await conn.QueryAsync<CustomerReturnRow>("""
            SELECT number, order_number, status, reason, refund_amount, requested_at FROM returns WHERE customer_id = @Id ORDER BY requested_at DESC LIMIT 10
            """, args)).AsList();
        var reviews = (await conn.QueryAsync<CustomerReviewRow>("""
            SELECT r.product_id, p.title AS product_title, r.rating, r.title, r.status, r.created_at
            FROM reviews r JOIN products p ON p.id = r.product_id WHERE r.customer_id = @Id ORDER BY r.created_at DESC LIMIT 10
            """, args)).AsList();
        var coupons = (await conn.QueryAsync<CustomerCouponRow>("""
            SELECT c.code, m.name AS campaign, c.kind, c.value, c.expires_at, o.number AS used_order_number
            FROM coupons c JOIN campaigns m ON m.id = c.campaign_id LEFT JOIN orders o ON o.id = c.used_order_id
            WHERE c.customer_id = @Id ORDER BY c.created_at DESC LIMIT 10
            """, args)).AsList();
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
