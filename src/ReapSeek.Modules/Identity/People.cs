using Dapper;
using Npgsql;

namespace ReapSeek.Modules.Identity;

public sealed record Person(long Id, string Email, string Name);
public sealed record PersonIdRecord(long Id);
public sealed record PersonIdsRecord(long[] Ids);
public sealed record PersonSellerRecord(long SellerId);
public sealed record PersonSearchRecord(string Pattern);

public static class People
{
    const string Columns = "SELECT id, email, name FROM identity.users";

    public static Task<Person?> FindAsync(NpgsqlConnection conn, long userId, NpgsqlTransaction? tx = null) =>
        conn.QuerySingleOrDefaultAsync<Person>(Columns + " WHERE id = @Id", new PersonIdRecord(userId), tx);

    public static async Task<Person> GetAsync(NpgsqlConnection conn, long userId, NpgsqlTransaction? tx = null) =>
        await FindAsync(conn, userId, tx) ?? throw new InvalidOperationException($"User {userId} was not found");

    public static Task<Person?> FindActiveAsync(NpgsqlConnection conn, long userId) =>
        conn.QuerySingleOrDefaultAsync<Person>(Columns + " WHERE id = @Id AND suspended_at IS NULL", new PersonIdRecord(userId));

    public static async Task<List<Person>> SellerStaffAsync(NpgsqlConnection conn, long sellerId, NpgsqlTransaction? tx = null) =>
        (await conn.QueryAsync<Person>(Columns + " WHERE seller_id = @SellerId ORDER BY id", new PersonSellerRecord(sellerId), tx)).AsList();

    public static async Task<List<Person>> AdminsAsync(NpgsqlConnection conn) =>
        (await conn.QueryAsync<Person>(Columns + " WHERE role = 'admin' ORDER BY id")).AsList();

    public static async Task<Dictionary<long, Person>> ManyAsync(NpgsqlConnection conn, IEnumerable<long> userIds, NpgsqlTransaction? tx = null)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<Person>(Columns + " WHERE id = ANY(@Ids)", new PersonIdsRecord(ids), tx)).ToDictionary(p => p.Id);
    }

    public static async Task<HashSet<long>> ReachableCustomersAsync(NpgsqlConnection conn, IEnumerable<long> userIds, NpgsqlTransaction? tx = null)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<long>("""
            SELECT id FROM identity.users
            WHERE id = ANY(@Ids) AND role = 'customer' AND email_verified_at IS NOT NULL AND suspended_at IS NULL
            """, new PersonIdsRecord(ids), tx)).ToHashSet();
    }

    public static async Task<HashSet<long>> CustomersAsync(NpgsqlConnection conn, IEnumerable<long> userIds)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await conn.QueryAsync<long>("SELECT id FROM identity.users WHERE id = ANY(@Ids) AND role = 'customer'", new PersonIdsRecord(ids))).ToHashSet();
    }

    public static async Task<long[]> MatchingAsync(NpgsqlConnection conn, string pattern) =>
        (await conn.QueryAsync<long>("SELECT id FROM identity.users WHERE email ILIKE @Pattern", new PersonSearchRecord(pattern))).ToArray();

    public static Task LockAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long userId) =>
        conn.ExecuteAsync("SELECT id FROM identity.users WHERE id = @Id FOR UPDATE", new PersonIdRecord(userId), tx);
}
