using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Api.Customers;

public sealed record CustomerRow(
    long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, int Orders, decimal Spent, DateTime? LastOrderAt);
public sealed record CustomersResponse(List<CustomerRow> Customers, int Total, int Page, int PageSize);
public sealed record CustomerOrderRow(string Number, string Status, decimal Total, int Packages, DateTime PlacedAt);
public sealed record CustomerReturnRow(string Number, string OrderNumber, string Status, string Reason, decimal RefundAmount, DateTime RequestedAt);
public sealed record CustomerReviewRow(long ProductId, string ProductTitle, int Rating, string Title, string Status, DateTime CreatedAt);
public sealed record CustomerCouponRow(string Code, string Campaign, string Kind, decimal Value, DateTime ExpiresAt, string? UsedOrderNumber);
public sealed record CustomerStats(int Orders, decimal Spent, decimal Refunded, decimal AverageOrder, int Returns, int Reviews, DateTime? FirstOrderAt, DateTime? LastOrderAt);
public sealed record CustomerDetail(
    long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, string? SuspendedReason, DateTime? EmailVerifiedAt, string DistinctId, CustomerStats Stats,
    List<CustomerOrderRow> Orders, List<CustomerReturnRow> Returns, List<CustomerReviewRow> Reviews, List<CustomerCouponRow> Coupons);
public sealed record SuspendRequest(string? Reason);

public static class CustomerQuery
{
    public static readonly string[] Sorts = ["recent", "spent", "orders", "last_order"];

    public static string OrderBy(string sort) => sort switch
    {
        "spent" => "spent DESC, u.id DESC",
        "orders" => "orders DESC, u.id DESC",
        "last_order" => "last_order_at DESC NULLS LAST, u.id DESC",
        _ => "u.created_at DESC, u.id DESC",
    };

    public static string? Pattern(string? q) =>
        string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}

public static class CustomerEndpoints
{
    const int PageSize = 25;

    public sealed record ListRecord(string? Pattern, string? Status, int Limit, int Offset);
    public sealed record ListRow(long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, int Orders, decimal Spent, DateTime? LastOrderAt, int Total);
    public sealed record IdRecord(long Id);
    public sealed record ProfileRecord(long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, string? SuspendedReason, DateTime? EmailVerifiedAt);
    public sealed record SuspendRecord(long Id, string? Reason);

    public static void MapCustomers(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/backoffice/customers").RequireAdmin();

        admin.MapGet("/", async (string? q, string? sort, string? status, int? page, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var s = string.IsNullOrEmpty(sort) ? "recent" : Ensure.OneOf(sort, "sort", CustomerQuery.Sorts);
            var st = string.IsNullOrEmpty(status) ? null : Ensure.OneOf(status, "status", "active", "suspended");
            var p = Ensure.InRange(page ?? 1, 1, 10_000, "page");
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
                ORDER BY {CustomerQuery.OrderBy(s)}
                LIMIT @Limit OFFSET @Offset
                """, new ListRecord(CustomerQuery.Pattern(q), st, PageSize, (p - 1) * PageSize))).AsList();
            var customers = rows.Select(r => new CustomerRow(r.Id, r.Name, r.Email, r.CreatedAt, r.SuspendedAt, r.Orders, r.Spent, r.LastOrderAt)).ToList();
            return ApiResults.Ok(new CustomersResponse(customers, rows.Count > 0 ? rows[0].Total : 0, p, PageSize), AppJsonContext.Default.CustomersResponse);
        });

        admin.MapGet("/{id:long}", async (long id, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var profile = await conn.QuerySingleOrDefaultAsync<ProfileRecord>(
                "SELECT id, name, email, created_at, suspended_at, suspended_reason, email_verified_at FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id));
            if (profile is null) return ApiResults.NotFound("Customer not found");
            var args = new IdRecord(id);
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
            return ApiResults.Ok(new CustomerDetail(profile.Id, profile.Name, profile.Email, profile.CreatedAt, profile.SuspendedAt, profile.SuspendedReason, profile.EmailVerifiedAt,
                ReapSeek.Shared.Analytics.ReapDeepIdentity.DistinctId(id), stats, orders, returns, reviews, coupons), AppJsonContext.Default.CustomerDetail);
        });

        admin.MapGet("/{id:long}/reapdeep", async (long id, NpgsqlDataSource db, ReapDeepProfile reapDeep, CancellationToken ct) =>
        {
            await using (var conn = await db.OpenConnectionAsync(ct))
                if (await conn.ExecuteScalarAsync<long?>("SELECT id FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id)) is null)
                    return ApiResults.NotFound("Customer not found");
            return ApiResults.Ok(await reapDeep.ForCustomerAsync(id, ct), AppJsonContext.Default.CustomerAnalytics);
        });

        admin.MapPost("/{id:long}/suspend", async (long id, SuspendRequest req, NpgsqlDataSource db, RedisCache cache, ILoggerFactory logs, CancellationToken ct) =>
        {
            var reason = Ensure.NotBlank(req.Reason, "reason", 300);
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var changed = await conn.ExecuteAsync("""
                UPDATE users SET suspended_at = now(), suspended_reason = @Reason WHERE id = @Id AND role = 'customer' AND suspended_at IS NULL
                """, new SuspendRecord(id, reason), tx);
            if (changed == 0)
                return await conn.ExecuteScalarAsync<long?>("SELECT id FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id), tx) is null
                    ? ApiResults.NotFound("Customer not found")
                    : ApiResults.Conflict("The customer is already suspended");
            var revoked = (await conn.QueryAsync<string>("DELETE FROM sessions WHERE user_id = @Id RETURNING token_hash", new IdRecord(id), tx)).ToArray();
            await tx.CommitAsync(ct);
            await cache.RemoveAsync(revoked.Select(CacheKeys.Session).ToArray());
            logs.CreateLogger("Customers").LogInformation("Suspended customer {Customer} and revoked {Sessions} sessions", id, revoked.Length);
            return Results.NoContent();
        });

        admin.MapPost("/{id:long}/reinstate", async (long id, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var changed = await conn.ExecuteAsync("""
                UPDATE users SET suspended_at = NULL, suspended_reason = NULL WHERE id = @Id AND role = 'customer' AND suspended_at IS NOT NULL
                """, new IdRecord(id));
            if (changed == 1) return Results.NoContent();
            return await conn.ExecuteScalarAsync<long?>("SELECT id FROM users WHERE id = @Id AND role = 'customer'", new IdRecord(id)) is null
                ? ApiResults.NotFound("Customer not found")
                : ApiResults.Conflict("The customer is not suspended");
        });
    }
}
