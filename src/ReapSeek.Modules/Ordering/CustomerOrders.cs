using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Ordering;

public sealed record CustomerOrderStats(long CustomerId, int Orders, decimal Spent, decimal AverageOrder, DateTime? FirstOrderAt, DateTime? LastOrderAt);
public sealed record CustomerOrderSummary(string Number, string Status, decimal Total, int Packages, DateTime PlacedAt);
public sealed record CustomerReturnSummary(string Number, string OrderNumber, string Status, string Reason, decimal RefundAmount, DateTime RequestedAt);
public sealed record CustomerReturnTotals(int Returns, decimal Refunded);
public sealed record OrderNumberRecord(Guid Id, string Number);
public sealed record CustomerIdsRecord(long[] CustomerIds);
public sealed record OneCustomerRecord(long CustomerId);

public static class CustomerOrders
{
    public static async Task<Dictionary<long, CustomerOrderStats>> StatsAsync(NpgsqlConnection conn, IEnumerable<long> customerIds)
    {
        var ids = customerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<CustomerOrderStats>("""
            SELECT customer_id, count(*)::int AS orders, sum(total) AS spent, round(avg(total), 2) AS average_order,
                min(placed_at) AS first_order_at, max(placed_at) AS last_order_at
            FROM ordering.orders WHERE status = 'paid' AND customer_id = ANY(@CustomerIds) GROUP BY customer_id
            """, new CustomerIdsRecord(ids))).ToDictionary(s => s.CustomerId);
    }

    public static async Task<List<CustomerOrderSummary>> RecentAsync(NpgsqlConnection conn, long customerId, int limit) =>
        (await conn.QueryAsync<CustomerOrderSummary>($"""
            SELECT o.number, o.status, o.total, (SELECT count(*)::int FROM ordering.order_packages p WHERE p.order_id = o.id) AS packages, o.placed_at
            FROM ordering.orders o WHERE o.customer_id = @CustomerId ORDER BY o.placed_at DESC LIMIT {limit}
            """, new OneCustomerRecord(customerId))).AsList();

    public static Task<CustomerReturnTotals> ReturnTotalsAsync(NpgsqlConnection conn, long customerId) =>
        conn.QuerySingleAsync<CustomerReturnTotals>("""
            SELECT count(*)::int AS returns, coalesce(sum(refund_amount) FILTER (WHERE status = 'refunded'), 0) AS refunded
            FROM ordering.returns WHERE customer_id = @CustomerId
            """, new OneCustomerRecord(customerId));

    public static async Task<List<CustomerReturnSummary>> RecentReturnsAsync(NpgsqlConnection conn, long customerId, int limit) =>
        (await conn.QueryAsync<CustomerReturnSummary>($"""
            SELECT number, order_number, status, reason, refund_amount, requested_at FROM ordering.returns
            WHERE customer_id = @CustomerId ORDER BY requested_at DESC LIMIT {limit}
            """, new OneCustomerRecord(customerId))).AsList();

    public static async Task<Dictionary<Guid, string>> NumbersAsync(NpgsqlConnection conn, IEnumerable<Guid> orderIds)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<OrderNumberRecord>("SELECT id, number FROM ordering.orders WHERE id = ANY(@OrderIds)", new OrderIdsRecord(ids)))
            .ToDictionary(o => o.Id, o => o.Number);
    }
}
