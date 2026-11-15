using ReapSeek.Worker.Identity;
using ReapSeek.Api.Identity;
using ReapSeek.Modules.Identity;
using ReapSeek.Shared.Migrations;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.UnitTests;

public class InfrastructureTests
{
    [Fact]
    public void Migrations_are_embedded_in_version_order()
    {
        var migrations = MigrationRunner.Embedded();
        Assert.NotEmpty(migrations);
        Assert.Equal(("platform", "0001_baseline"), (migrations[0].Module, migrations[0].Version));
        Assert.Equal(MigrationRunner.Modules, migrations.Select(m => m.Module).Distinct());
        foreach (var group in migrations.GroupBy(m => m.Module))
        {
            Assert.Equal("0001_baseline", group.First().Version);
            Assert.Equal(group.Select(m => m.Version).Order(StringComparer.Ordinal), group.Select(m => m.Version));
            Assert.Equal(group.Count(), group.Select(m => m.Version[..4]).Distinct().Count());
        }
        Assert.All(migrations, m => Assert.False(string.IsNullOrWhiteSpace(m.Sql)));
    }

    [Fact]
    public void The_database_search_path_matches_the_platform_baseline()
    {
        var migrations = MigrationRunner.Embedded();
        var platform = migrations.Single(m => m.Module == "platform" && m.Version == "0001_baseline").Sql;
        var expected = ReapSeek.Shared.Database.SearchPath.Replace(",", ", ");
        Assert.Contains($"SET search_path = {expected}'", platform);
        var schemas = ReapSeek.Shared.Database.SearchPath.Split(',').Where(s => s != "public").ToList();
        Assert.Equal(MigrationRunner.Modules.Order(), schemas.Order());
        foreach (var schema in schemas)
            Assert.Contains($"CREATE SCHEMA IF NOT EXISTS {schema};", migrations.Single(m => m.Module == schema && m.Version == "0001_baseline").Sql);
    }

    [Fact]
    public void Outbox_retries_back_off_exponentially_and_give_up()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddSeconds(5), OutboxPolicy.RetryAt(1, now));
        Assert.Equal(now.AddSeconds(10), OutboxPolicy.RetryAt(2, now));
        Assert.Equal(now.AddSeconds(40), OutboxPolicy.RetryAt(4, now));
        Assert.Equal(now.AddSeconds(1280), OutboxPolicy.RetryAt(OutboxPolicy.MaxAttempts - 1, now));
        Assert.Null(OutboxPolicy.RetryAt(OutboxPolicy.MaxAttempts, now));
    }

    [Fact]
    public void Long_errors_are_truncated() =>
        Assert.Equal(OutboxPolicy.MaxErrorLength, OutboxPolicy.Truncate(new string('x', 10_000)).Length);

    [Fact]
    public void Welcome_email_escapes_the_name_in_html()
    {
        var email = AccountEmails.Welcome("jane@example.com", "<b>Jane</b>", "http://localhost:3100", "http://localhost:3100/verify-email?token=abc");
        Assert.Equal("jane@example.com", email.To);
        Assert.Contains("&lt;b&gt;Jane&lt;/b&gt;", email.Html);
        Assert.DoesNotContain("<b>Jane</b>", email.Html);
        Assert.Contains("Hi <b>Jane</b>,", email.Text);
        Assert.Contains("http://localhost:3100", email.Text);
        Assert.Contains("http://localhost:3100/verify-email?token=abc", email.Text);
        Assert.DoesNotContain("Confirm", AccountEmails.Welcome("jane@example.com", "Jane", "http://localhost:3100", null).Html);
    }

    [Fact]
    public void Dapper_row_types_are_not_private()
    {
        var rows = new[] { typeof(PasswordHasher).Assembly, typeof(OutboxRepository).Assembly, typeof(MigrationRunner).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsNested && t.Name.EndsWith("Record", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, t => Assert.False(t.IsNestedPrivate, $"{t.FullName} is private; Dapper.AOT silently skips private row types"));
    }
}
