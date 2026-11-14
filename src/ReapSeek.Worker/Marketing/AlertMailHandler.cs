using ReapSeek.Modules.Identity;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Marketing;

public sealed class AlertMailHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg)
{
    public sealed record AlertRecord(string AlertName, string State, string Message, string InsightName, double Value, DateTime CheckedAt);

    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct) => AlertAsync(message, ct);

    async Task AlertAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.AlertMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var a = await conn.QuerySingleOrDefaultAsync<AlertRecord>(
            "SELECT alert_name, state, message, insight_name, value, checked_at FROM analytics_alerts WHERE id = @Id", new { Id = payload.EventId });
        if (a is null) return;
        var url = $"{_backofficeUrl}/#/alerts";
        var headline = a.State == "firing" ? $"Alert: {a.AlertName}" : $"{a.AlertName} is back to normal";
        foreach (var admin in await People.AdminsAsync(conn))
            await email.SendAsync(new EmailMessage(admin.Email, headline,
                $"{headline}\n\n{a.Message}\nSource: {a.InsightName} (ReapDeep)\n\nAll alerts: {url}\n",
                $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:{(a.State == "firing" ? "#c8324b" : "#12805c")};font-size:20px">{WebUtility.HtmlEncode(headline)}</h1><p>{WebUtility.HtmlEncode(a.Message)}</p><p style="color:#6b7080;font-size:13px">Source: {WebUtility.HtmlEncode(a.InsightName)} in ReapDeep</p><p><a href="{WebUtility.HtmlEncode(url)}">All alerts in the back office</a></p></div>"""), ct);
    }
}
