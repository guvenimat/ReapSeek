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

    public static void MapCustomers(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/backoffice/customers").RequireAdmin();

        admin.MapGet("/", async (string? q, string? sort, string? status, int? page, CustomerRepository customers, CancellationToken ct) =>
        {
            var s = string.IsNullOrEmpty(sort) ? "recent" : Ensure.OneOf(sort, "sort", CustomerQuery.Sorts);
            var st = string.IsNullOrEmpty(status) ? null : Ensure.OneOf(status, "status", "active", "suspended");
            var p = Ensure.InRange(page ?? 1, 1, 10_000, "page");
            var (rows, total) = await customers.ListAsync(CustomerQuery.Pattern(q), s, st, p, PageSize, ct);
            return ApiResults.Ok(new CustomersResponse(rows, total, p, PageSize), AppJsonContext.Default.CustomersResponse);
        });

        admin.MapGet("/{id:long}", async (long id, CustomerRepository customers, CancellationToken ct) =>
            await customers.DetailAsync(id, ct) is { } detail
                ? ApiResults.Ok(detail, AppJsonContext.Default.CustomerDetail)
                : ApiResults.NotFound("Customer not found"));

        admin.MapGet("/{id:long}/reapdeep", async (long id, CustomerRepository customers, ReapDeepProfile reapDeep, CancellationToken ct) =>
            await customers.ExistsAsync(id, ct)
                ? ApiResults.Ok(await reapDeep.ForCustomerAsync(id, ct), AppJsonContext.Default.CustomerAnalytics)
                : ApiResults.NotFound("Customer not found"));

        admin.MapPost("/{id:long}/suspend", async (long id, SuspendRequest req, CustomerRepository customers, RedisCache cache, ILoggerFactory logs, CancellationToken ct) =>
        {
            var reason = Ensure.NotBlank(req.Reason, "reason", 300);
            var (change, revoked) = await customers.SuspendAsync(id, reason, ct);
            if (change == SuspensionChange.NotFound) return ApiResults.NotFound("Customer not found");
            if (change == SuspensionChange.Unchanged) return ApiResults.Conflict("The customer is already suspended");
            await cache.RemoveAsync(revoked.Select(CacheKeys.Session).ToArray());
            logs.CreateLogger("Customers").LogInformation("Suspended customer {Customer} and revoked {Sessions} sessions", id, revoked.Length);
            return Results.NoContent();
        });

        admin.MapPost("/{id:long}/reinstate", async (long id, CustomerRepository customers, CancellationToken ct) =>
            await customers.ReinstateAsync(id, ct) switch
            {
                SuspensionChange.Changed => Results.NoContent(),
                SuspensionChange.NotFound => ApiResults.NotFound("Customer not found"),
                _ => ApiResults.Conflict("The customer is not suspended"),
            });
    }
}
