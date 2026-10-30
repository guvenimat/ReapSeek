using Npgsql;

namespace ReapSeek.Shared;

public static class Database
{
    public static NpgsqlDataSource Create(string? connectionString)
    {
        var builder = new NpgsqlSlimDataSourceBuilder(connectionString ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not set"));
        builder.EnableArrays();
        return builder.Build();
    }
}
