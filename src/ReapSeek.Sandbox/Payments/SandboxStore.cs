using Dapper;
using Npgsql;

namespace ReapSeek.Sandbox.Payments;

public sealed record PaymentTransitionRecord(string Id, string FromStatus, string Status, string? CardBrand, string? CardLast4, string? FailureReason, bool CountsAttempt);
public sealed record ShipmentRecord(string TrackingNumber, string Reference, string Kind, string? WebhookUrl, DateTime CreatedAt, string Status, DateTime UpdatedAt);
public sealed record ShipmentEventRecord(string TrackingNumber, string Status, DateTime OccurredAt);
public sealed record ShipmentKeyRecord(string TrackingNumber, string Kind, string Reference, string? WebhookUrl, DateTime CreatedAt);
public sealed record ShipmentLookupRecord(string Kind, string Reference);
public sealed record ShipmentStepRecord(string? TrackingNumber, DateTime Now, DateTime Threshold);
public sealed record TrackingRecord(string TrackingNumber);
public sealed record IdRecord(string Id);
public sealed record TransferRecord(string Id, string Status, decimal Amount, string Currency, string Reference, string Recipient, DateTime CreatedAt);
public sealed record RefundRecord(string Id, string PaymentId, decimal Amount, string Reference, DateTime CreatedAt);
public sealed record ReferenceRecord(string Reference);

public static class SandboxDatabase
{
    const string Schema = """
        SELECT pg_advisory_xact_lock(5391);
        CREATE TABLE IF NOT EXISTS payments (
            id text PRIMARY KEY,
            amount numeric(12,2) NOT NULL,
            currency text NOT NULL,
            reference text NOT NULL,
            description text NOT NULL,
            return_url text NOT NULL,
            webhook_url text NOT NULL,
            created_at timestamptz NOT NULL,
            status text NOT NULL,
            card_brand text,
            card_last4 text,
            failure_reason text,
            attempts int NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS refunds (
            reference text PRIMARY KEY,
            id text NOT NULL UNIQUE,
            payment_id text NOT NULL REFERENCES payments(id),
            amount numeric(12,2) NOT NULL,
            created_at timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS refunds_payment ON refunds(payment_id);
        CREATE TABLE IF NOT EXISTS shipments (
            tracking_number text PRIMARY KEY,
            reference text NOT NULL,
            kind text NOT NULL,
            webhook_url text,
            created_at timestamptz NOT NULL,
            status text NOT NULL,
            updated_at timestamptz NOT NULL,
            UNIQUE (kind, reference)
        );
        CREATE INDEX IF NOT EXISTS shipments_moving ON shipments(updated_at) WHERE status <> 'delivered';
        CREATE TABLE IF NOT EXISTS shipment_events (
            id bigserial PRIMARY KEY,
            tracking_number text NOT NULL REFERENCES shipments(tracking_number),
            status text NOT NULL,
            occurred_at timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS shipment_events_tracking ON shipment_events(tracking_number);
        CREATE TABLE IF NOT EXISTS transfers (
            reference text PRIMARY KEY,
            id text NOT NULL UNIQUE,
            status text NOT NULL,
            amount numeric(12,2) NOT NULL,
            currency text NOT NULL,
            recipient text NOT NULL,
            created_at timestamptz NOT NULL
        );
        """;

    public static NpgsqlDataSource Create(string? connectionString) =>
        new NpgsqlSlimDataSourceBuilder(connectionString ?? throw new InvalidOperationException("ConnectionStrings:Sandbox is not set")).Build();

    public static async Task InitializeAsync(string connectionString, NpgsqlDataSource db, ILogger log)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await EnsureDatabaseAsync(connectionString);
                await using var conn = await db.OpenConnectionAsync();
                await using var tx = await conn.BeginTransactionAsync();
                await using (var cmd = new NpgsqlCommand(Schema, conn, tx)) await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
                return;
            }
            catch (NpgsqlException ex) when (attempt < 30)
            {
                log.LogWarning("The sandbox database is not ready ({Message}), retrying", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    static async Task EnsureDatabaseAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var name = target.Database ?? throw new InvalidOperationException("The sandbox connection string has no database");
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        await using var source = new NpgsqlSlimDataSourceBuilder(admin.ConnectionString).Build();
        await using var conn = await source.OpenConnectionAsync();
        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", conn) { Parameters = { new() { Value = name } } };
        if (await check.ExecuteScalarAsync() is not null) return;
        try
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name.Replace("\"", "\"\"")}\"", conn);
            await create.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
        }
    }
}

public sealed class PaymentStore(NpgsqlDataSource db)
{
    const string Columns = "id, amount, currency, reference, description, return_url, webhook_url, created_at, status, card_brand, card_last4, failure_reason, attempts";

    public async Task<PaymentRecord> AddAsync(PaymentRecord payment)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync($"""
            INSERT INTO payments ({Columns})
            VALUES (@Id, @Amount, @Currency, @Reference, @Description, @ReturnUrl, @WebhookUrl, @CreatedAt, @Status, @CardBrand, @CardLast4, @FailureReason, @Attempts)
            """, payment);
        return payment;
    }

    public async Task<PaymentRecord?> FindAsync(string id)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PaymentRecord>($"SELECT {Columns} FROM payments WHERE id = @Id", new IdRecord(id));
    }

    public async Task<PaymentRecord?> TransitionAsync(PaymentTransitionRecord change)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PaymentRecord>($"""
            UPDATE payments SET status = @Status, card_brand = coalesce(@CardBrand, card_brand), card_last4 = coalesce(@CardLast4, card_last4),
                failure_reason = @FailureReason, attempts = attempts + CASE WHEN @CountsAttempt THEN 1 ELSE 0 END
            WHERE id = @Id AND status = @FromStatus
            RETURNING {Columns}
            """, change);
    }

    public async Task<(RefundRecord? Refund, bool Created, string? Error)> RefundAsync(string paymentId, decimal amount, string reference)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var payment = await conn.QuerySingleOrDefaultAsync<PaymentRecord>($"SELECT {Columns} FROM payments WHERE id = @Id FOR UPDATE", new IdRecord(paymentId), tx);
        if (payment is null) return (null, false, "not_found");
        if (payment.Status != PaymentStatus.Succeeded) return (null, false, "not_succeeded");
        var existing = await conn.QuerySingleOrDefaultAsync<RefundRecord>(
            "SELECT id, payment_id, amount, reference, created_at FROM refunds WHERE reference = @Reference", new ReferenceRecord(reference), tx);
        if (existing is not null) return (existing, false, null);
        var refunded = await conn.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(amount), 0) FROM refunds WHERE payment_id = @Id", new IdRecord(paymentId), tx);
        if (refunded + amount > payment.Amount) return (null, false, "too_large");
        var refund = new RefundRecord(NewId("re"), paymentId, amount, reference, DateTime.UtcNow);
        await conn.ExecuteAsync("INSERT INTO refunds (reference, id, payment_id, amount, created_at) VALUES (@Reference, @Id, @PaymentId, @Amount, @CreatedAt)", refund, tx);
        await tx.CommitAsync();
        return (refund, true, null);
    }

    public static string NewId(string prefix) => $"{prefix}_{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12))}";
}

public sealed class LogisticsStore(NpgsqlDataSource db)
{
    const string ShipmentColumns = "tracking_number, reference, kind, webhook_url, created_at, status, updated_at";
    const string NextStatus = """
        CASE status WHEN 'label_created' THEN 'in_transit' WHEN 'in_transit' THEN 'out_for_delivery' ELSE 'delivered' END
        """;

    public async Task<(ShipmentRecord Shipment, List<ShipmentEventRecord> Events)> CreateShipmentAsync(string kind, string reference, string? webhookUrl, string trackingNumber)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync($"""
            WITH created AS (
                INSERT INTO shipments ({ShipmentColumns})
                VALUES (@TrackingNumber, @Reference, @Kind, @WebhookUrl, @CreatedAt, 'label_created', @CreatedAt)
                ON CONFLICT (kind, reference) DO NOTHING
                RETURNING tracking_number, created_at)
            INSERT INTO shipment_events (tracking_number, status, occurred_at) SELECT tracking_number, 'label_created', created_at FROM created
            """, new ShipmentKeyRecord(trackingNumber, kind, reference, webhookUrl, DateTime.UtcNow));
        var shipment = await conn.QuerySingleAsync<ShipmentRecord>($"SELECT {ShipmentColumns} FROM shipments WHERE kind = @Kind AND reference = @Reference",
            new ShipmentLookupRecord(kind, reference));
        return (shipment, await EventsAsync(conn, shipment.TrackingNumber));
    }

    public async Task<(ShipmentRecord Shipment, List<ShipmentEventRecord> Events)?> FindShipmentAsync(string trackingNumber)
    {
        await using var conn = await db.OpenConnectionAsync();
        var shipment = await conn.QuerySingleOrDefaultAsync<ShipmentRecord>($"SELECT {ShipmentColumns} FROM shipments WHERE tracking_number = @TrackingNumber",
            new TrackingRecord(trackingNumber));
        return shipment is null ? null : (shipment, await EventsAsync(conn, trackingNumber));
    }

    public async Task<List<ShipmentRecord>> AdvanceAsync(string? trackingNumber, DateTime now, DateTime threshold)
    {
        await using var conn = await db.OpenConnectionAsync();
        return (await conn.QueryAsync<ShipmentRecord>($"""
            WITH moved AS (
                UPDATE shipments SET status = {NextStatus}, updated_at = @Now
                WHERE status <> 'delivered' AND updated_at <= @Threshold AND (@TrackingNumber::text IS NULL OR tracking_number = @TrackingNumber)
                RETURNING {ShipmentColumns}),
            logged AS (INSERT INTO shipment_events (tracking_number, status, occurred_at) SELECT tracking_number, status, updated_at FROM moved)
            SELECT {ShipmentColumns} FROM moved ORDER BY created_at
            """, new ShipmentStepRecord(trackingNumber, now, threshold))).AsList();
    }

    public async Task<TransferRecord> TransferAsync(TransferRecord transfer)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            INSERT INTO transfers (reference, id, status, amount, currency, recipient, created_at)
            VALUES (@Reference, @Id, @Status, @Amount, @Currency, @Recipient, @CreatedAt)
            ON CONFLICT (reference) DO NOTHING
            """, transfer);
        return await conn.QuerySingleAsync<TransferRecord>(
            "SELECT id, status, amount, currency, reference, recipient, created_at FROM transfers WHERE reference = @Reference", new ReferenceRecord(transfer.Reference));
    }

    static async Task<List<ShipmentEventRecord>> EventsAsync(NpgsqlConnection conn, string trackingNumber) =>
        (await conn.QueryAsync<ShipmentEventRecord>("SELECT tracking_number, status, occurred_at FROM shipment_events WHERE tracking_number = @TrackingNumber ORDER BY id",
            new TrackingRecord(trackingNumber))).AsList();
}
