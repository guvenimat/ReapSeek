using Npgsql;

namespace ReapSeek.Shared;

public static class Database
{
    public const string SearchPath = "identity,catalog,sellers,ordering,finance,engagement,marketing,platform,public";

    public static NpgsqlDataSource Create(string? connectionString)
    {
        var builder = new NpgsqlSlimDataSourceBuilder(connectionString ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not set"));
        builder.ConnectionStringBuilder.SearchPath ??= SearchPath;
        builder.EnableArrays();
        return builder.Build();
    }
}
