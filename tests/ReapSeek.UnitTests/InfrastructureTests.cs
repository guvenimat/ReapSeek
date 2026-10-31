using ReapSeek.Api.Identity;
using ReapSeek.Shared.Identity;
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
        Assert.Equal("0001_identity_and_outbox", migrations[0].Version);
        Assert.Equal(migrations.Select(m => m.Version).Order(StringComparer.Ordinal), migrations.Select(m => m.Version));
        Assert.All(migrations, m => Assert.False(string.IsNullOrWhiteSpace(m.Sql)));
        Assert.Equal(migrations.Count, migrations.Select(m => m.Version[..4]).Distinct().Count());
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
        var email = EmailTemplates.Welcome(new UserRegistered(1, "jane@example.com", "<b>Jane</b>"), "http://localhost:3100");
        Assert.Equal("jane@example.com", email.To);
        Assert.Contains("&lt;b&gt;Jane&lt;/b&gt;", email.Html);
        Assert.DoesNotContain("<b>Jane</b>", email.Html);
        Assert.Contains("Hi <b>Jane</b>,", email.Text);
        Assert.Contains("http://localhost:3100", email.Text);
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
