using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Caching;

namespace ReapSeek.Identity.Api;

internal sealed record CustomerRow(
    long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, int Orders, decimal Spent, DateTime? LastOrderAt);
internal sealed record CustomersResponse(List<CustomerRow> Customers, int Total, int Page, int PageSize);
internal sealed record CustomerOrderRow(string Number, string Status, decimal Total, int Packages, DateTime PlacedAt);
internal sealed record CustomerReturnRow(string Number, string OrderNumber, string Status, string Reason, decimal RefundAmount, DateTime RequestedAt);
internal sealed record CustomerReviewRow(long ProductId, string ProductTitle, int Rating, string Title, string Status, DateTime CreatedAt);
internal sealed record CustomerCouponRow(string Code, string Campaign, string Kind, decimal Value, DateTime ExpiresAt, string? UsedOrderNumber);
internal sealed record CustomerStats(int Orders, decimal Spent, decimal Refunded, decimal AverageOrder, int Returns, int Reviews, DateTime? FirstOrderAt, DateTime? LastOrderAt);
internal sealed record CustomerDetail(
    long Id, string Name, string Email, DateTime CreatedAt, DateTime? SuspendedAt, string? SuspendedReason, DateTime? EmailVerifiedAt, string DistinctId, CustomerStats Stats,
    List<CustomerOrderRow> Orders, List<CustomerReturnRow> Returns, List<CustomerReviewRow> Reviews, List<CustomerCouponRow> Coupons);
internal sealed record SuspendRequest(string? Reason);

internal static class CustomerQuery
{
    public static readonly string[] Sorts = ["recent", "spent", "orders", "last_order"];

    public static IEnumerable<CustomerRow> Sort(IEnumerable<CustomerRow> rows, string sort) => sort switch
    {
        "spent" => rows.OrderByDescending(r => r.Spent).ThenByDescending(r => r.Id),
        "orders" => rows.OrderByDescending(r => r.Orders).ThenByDescending(r => r.Id),
        "last_order" => rows.OrderBy(r => r.LastOrderAt is null).ThenByDescending(r => r.LastOrderAt).ThenByDescending(r => r.Id),
        _ => rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
    };

    public static string? Pattern(string? q) =>
        string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}

internal static class CustomerEndpoints
{
    const int PageSize = 25;

    public static void MapCustomers(this IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/backoffice/customers").RequireAdmin();

        admin.MapGet("/", async (string? q, string? sort, string? status, int? page, CustomerRepository customers, CancellationToken ct) =>
        {
            var s = string.IsNullOrEmpty(sort) ? "recent" : Ensure.OneOf(sort, "sort", CustomerQuery.Sorts);
            var st = string.IsNullOrEmpty(status) ? null : Ensure.OneOf(status, "status", "active", "suspended");
            var p = Ensure.InRange(page ?? 1, 1, 10_000, "page");
            var (rows, total) = await customers.ListAsync(CustomerQuery.Pattern(q), s, st, p, PageSize, ct);
            return ApiResults.Ok(new CustomersResponse(rows, total, p, PageSize), IdentityJsonContext.Default.CustomersResponse);
        });

        admin.MapGet("/{id:long}", async (long id, CustomerRepository customers, CancellationToken ct) =>
            await customers.DetailAsync(id, ct) is { } detail
                ? ApiResults.Ok(detail, IdentityJsonContext.Default.CustomerDetail)
                : ApiResults.NotFound("Customer not found"));

        admin.MapGet("/{id:long}/reapdeep", async (long id, CustomerRepository customers, ReapDeepProfile reapDeep, CancellationToken ct) =>
            await customers.ExistsAsync(id, ct)
                ? ApiResults.Ok(await reapDeep.ForCustomerAsync(id, ct), IdentityJsonContext.Default.CustomerAnalytics)
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
