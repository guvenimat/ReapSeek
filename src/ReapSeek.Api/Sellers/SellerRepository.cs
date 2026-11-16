using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Sellers;

public sealed class SellerRepository(NpgsqlDataSource db)
{
    public sealed record SellerRecord(long Id, string Name, string Slug, string City, string Status, decimal CommissionRate, decimal Rating, DateTime CreatedAt);
    public sealed record SellerChangeRecord(long Id, string Name, string Slug, string City, string Status, decimal Commission);

    const string UniqueViolation = "23505";
    const string Select = "SELECT s.id, s.name, s.slug, s.city, s.status, s.commission_rate, s.rating, s.created_at FROM sellers.sellers s";

    public async Task<List<SellerRow>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await WithCountsAsync(conn, (await conn.QueryAsync<SellerRecord>(Select + " ORDER BY lower(s.name)")).AsList());
    }

    public async Task<SellerRow?> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<SellerRecord>(Select + " WHERE s.id = @Id", new SellerChangeRecord(id, "", "", "", "", 0));
        return row is null ? null : (await WithCountsAsync(conn, [row]))[0];
    }

    static async Task<List<SellerRow>> WithCountsAsync(NpgsqlConnection conn, List<SellerRecord> sellers)
    {
        var counts = await SellerSnapshots.OfferCountsAsync(conn, sellers.Select(s => s.Id).ToArray());
        return sellers.Select(s => new SellerRow(s.Id, s.Name, s.Slug, s.City, s.Status, s.CommissionRate, s.Rating,
            counts.GetValueOrDefault(s.Id)?.ProductCount ?? 0, counts.GetValueOrDefault(s.Id)?.OfferCount ?? 0, s.CreatedAt)).ToList();
    }

    static Task SnapshotAsync(NpgsqlConnection conn, NpgsqlTransaction tx, SellerRecord s) =>
        SellerSnapshots.UpsertAsync(conn, tx, [new SellerSnapshotRecord(s.Id, s.Name, s.Slug, s.City, s.Status, s.Rating)]);

    public async Task<long?> CreateAsync(string name, string slug, string city, decimal commission, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var seller = await conn.QuerySingleAsync<SellerRecord>("""
                INSERT INTO sellers.sellers (name, slug, city, commission_rate) VALUES (@Name, @Slug, @City, @Commission)
                RETURNING id, name, slug, city, status, commission_rate, rating, created_at
                """, new SellerChangeRecord(0, name, slug, city, "", commission), tx);
            await SnapshotAsync(conn, tx, seller);
            await tx.CommitAsync(ct);
            return seller.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public async Task<bool> UpdateAsync(long id, string name, string city, string status, decimal commission, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var change = new SellerChangeRecord(id, name, "", city, status, commission);
        var previous = await conn.ExecuteScalarAsync<string?>("SELECT status FROM sellers.sellers WHERE id = @Id FOR UPDATE", change, tx);
        if (previous is null) return false;
        SellerRecord seller;
        try
        {
            seller = await conn.QuerySingleAsync<SellerRecord>("""
                UPDATE sellers.sellers SET name = @Name, city = @City, status = @Status, commission_rate = @Commission WHERE id = @Id
                RETURNING id, name, slug, city, status, commission_rate, rating, created_at
                """, change, tx);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            throw new ConflictException($"Seller '{name}' already exists");
        }
        await SnapshotAsync(conn, tx, seller);
        if (previous != status)
        {
            var productIds = await SellerSnapshots.ProductIdsOfSellerAsync(conn, tx, id);
            if (productIds.Count > 0)
                await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged(productIds), OutboxJsonContext.Default.ProductsChanged);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
