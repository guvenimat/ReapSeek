using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Sellers;

public sealed class SellerRepository(NpgsqlDataSource db)
{
    const string UniqueViolation = "23505";
    const string Select = """
        SELECT s.id, s.name, s.slug, s.city, s.status, s.commission_rate, s.rating,
            (SELECT count(DISTINCT v.product_id) FROM offers o JOIN product_variants v ON v.id = o.variant_id WHERE o.seller_id = s.id) AS product_count,
            (SELECT count(*) FROM offers o WHERE o.seller_id = s.id) AS offer_count,
            s.created_at
        FROM sellers s
        """;

    public async Task<List<SellerRow>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<SellerRow>(Select + " ORDER BY lower(s.name)")).AsList();
    }

    public async Task<SellerRow?> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SellerRow>(Select + " WHERE s.id = @Id", new { Id = id });
    }

    public async Task<long?> CreateAsync(string name, string slug, string city, decimal commission, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        try
        {
            return await conn.ExecuteScalarAsync<long>("""
                INSERT INTO sellers (name, slug, city, commission_rate) VALUES (@Name, @Slug, @City, @Commission) RETURNING id
                """, new { Name = name, Slug = slug, City = city, Commission = commission });
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
        var previous = await conn.ExecuteScalarAsync<string?>("SELECT status FROM sellers WHERE id = @Id FOR UPDATE", new { Id = id }, tx);
        if (previous is null) return false;
        try
        {
            await conn.ExecuteAsync("""
                UPDATE sellers SET name = @Name, city = @City, status = @Status, commission_rate = @Commission WHERE id = @Id
                """, new { Id = id, Name = name, City = city, Status = status, Commission = commission }, tx);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            throw new ConflictException($"Seller '{name}' already exists");
        }
        if (previous != status)
        {
            var productIds = (await conn.QueryAsync<long>("""
                SELECT DISTINCT v.product_id FROM offers o JOIN product_variants v ON v.id = o.variant_id WHERE o.seller_id = @Id
                """, new { Id = id }, tx)).AsList();
            if (productIds.Count > 0)
                await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged(productIds), OutboxJsonContext.Default.ProductsChanged);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
