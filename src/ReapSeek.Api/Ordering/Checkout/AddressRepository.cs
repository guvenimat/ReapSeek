using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;

namespace ReapSeek.Api.Ordering.Checkout;

public static class AddressRepository
{
    public sealed record CustomerIdRecord(long CustomerId);
    public sealed record AddressRecord(long CustomerId, string FullName, string Phone, string City, string District, string Line, string PostalCode);

    public static async Task<List<SavedAddress>> ListAsync(NpgsqlConnection conn, long customerId) =>
        (await conn.QueryAsync<SavedAddress>("""
            SELECT id, full_name, phone, city, district, line, postal_code, last_used_at
            FROM addresses WHERE customer_id = @CustomerId ORDER BY last_used_at DESC LIMIT 10
            """, new CustomerIdRecord(customerId))).AsList();

    public static Task SaveAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long customerId, ShippingAddress a) =>
        conn.ExecuteAsync("""
            INSERT INTO addresses (customer_id, full_name, phone, city, district, line, postal_code)
            VALUES (@CustomerId, @FullName, @Phone, @City, @District, @Line, @PostalCode)
            ON CONFLICT (customer_id, full_name, phone, city, district, line, postal_code) DO UPDATE SET last_used_at = now()
            """, new AddressRecord(customerId, a.FullName, a.Phone, a.City, a.District, a.Line, a.PostalCode), tx);
}
