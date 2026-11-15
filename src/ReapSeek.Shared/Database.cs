using Npgsql;

namespace ReapSeek.Shared;

public static class Database
{
    public const string SearchPath = "platform,identity,catalog,sellers,ordering,finance,engagement,marketing,public";

    public static NpgsqlDataSource Create(string? connectionString)
    {
        var builder = new NpgsqlSlimDataSourceBuilder(connectionString ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not set"));
        builder.ConnectionStringBuilder.SearchPath ??= SearchPath;
        builder.EnableArrays();
        return builder.Build();
    }
}
