using Dapper;
using Npgsql;
using ReapSeek.Modules.Ordering;
using ReapSeek.Modules.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Marketing;

public sealed class CampaignRepository(NpgsqlDataSource db)
{
    public sealed record CampaignInsertRecord(
        string Name, long CohortId, string CohortName, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject, string Message);
    public sealed record CustomerCouponsRecord(long CustomerId, DateTime Now);
    public sealed record CampaignIdRecord(long Id);
    public sealed record CampaignRecord(
        long Id, string Name, long CohortId, string CohortName, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject,
        string Message, string Status, int Recipients, Guid[] UsedOrders, string? Error, DateTime CreatedAt, DateTime? SentAt);

    static async Task<CampaignRow> ToRowAsync(NpgsqlConnection conn, CampaignRecord c)
    {
        var paid = await OrderLookups.PaidTotalAsync(conn, c.UsedOrders);
        return new CampaignRow(c.Id, c.Name, c.CohortId, c.CohortName, c.Kind, c.Value, c.MinSubtotal, c.MaxDiscount, c.ValidDays, c.Subject,
            c.Message, c.Status, c.Recipients, paid.Count, paid.Total, c.Error, c.CreatedAt, c.SentAt);
    }

    const string Select = """
        SELECT c.id, c.name, c.cohort_id, c.cohort_name, c.kind, c.value, c.min_subtotal, c.max_discount, c.valid_days, c.subject, c.message, c.status,
            c.recipients,
            coalesce((SELECT array_agg(k.used_order_id) FROM coupons k WHERE k.campaign_id = c.id AND k.used_order_id IS NOT NULL), '{}') AS used_orders,
            c.error, c.created_at, c.sent_at
        FROM campaigns c
        """;

    public async Task<List<CustomerCoupon>> CustomerCouponsAsync(long customerId, DateTime now, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<CustomerCoupon>("""
            SELECT k.code, k.kind, k.value, k.min_subtotal, k.max_discount, k.expires_at, c.name AS campaign
            FROM coupons k LEFT JOIN campaigns c ON c.id = k.campaign_id
            WHERE k.customer_id = @CustomerId AND k.used_order_id IS NULL AND k.expires_at > @Now ORDER BY k.expires_at
            """, new CustomerCouponsRecord(customerId, now))).AsList();
    }

    public async Task<(int Reachable, int Known)> ReachAsync(long[] customerIds, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var reachable = (await People.ReachableCustomersAsync(conn, customerIds)).Count;
        var known = (await People.CustomersAsync(conn, customerIds)).Count;
        return (reachable, known);
    }

    public async Task<List<CampaignRow>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = new List<CampaignRow>();
        foreach (var c in await conn.QueryAsync<CampaignRecord>(Select + " ORDER BY c.created_at DESC LIMIT 100")) rows.Add(await ToRowAsync(conn, c));
        return rows;
    }

    public async Task<CampaignRow> CreateAsync(CampaignInsertRecord campaign, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO campaigns (name, cohort_id, cohort_name, kind, value, min_subtotal, max_discount, valid_days, subject, message)
            VALUES (@Name, @CohortId, @CohortName, @Kind, @Value, @MinSubtotal, @MaxDiscount, @ValidDays, @Subject, @Message) RETURNING id
            """, campaign);
        return await ToRowAsync(conn, await conn.QuerySingleAsync<CampaignRecord>(Select + " WHERE c.id = @Id", new CampaignIdRecord(id)));
    }

    public async Task<CampaignRow> StartSendingAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var status = await conn.ExecuteScalarAsync<string?>("SELECT status FROM campaigns WHERE id = @Id FOR UPDATE", new CampaignIdRecord(id), tx)
            ?? throw new NotFoundException("Campaign not found");
        if (status is not ("draft" or "failed")) throw new ConflictException("This campaign was already sent", "campaign_sent");
        await conn.ExecuteAsync("UPDATE campaigns SET status = 'sending', error = NULL WHERE id = @Id", new CampaignIdRecord(id), tx);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CampaignSend, new CampaignMessage(id, null), OutboxJsonContext.Default.CampaignMessage);
        await tx.CommitAsync(ct);
        return await ToRowAsync(conn, await conn.QuerySingleAsync<CampaignRecord>(Select + " WHERE c.id = @Id", new CampaignIdRecord(id)));
    }
}
