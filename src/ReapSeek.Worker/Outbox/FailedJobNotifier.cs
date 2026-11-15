using ReapSeek.BuildingBlocks.Messaging;
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
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Outbox;

public sealed class FailedJobNotifier(NpgsqlDataSource db, EmailSender email, IConfiguration cfg)
{
    public sealed record FailedJobRecord(string Type, int Attempts, string? LastError);
    public sealed record FailedJobIdRecord(long Id);

    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct) => FailedJobAsync(message, ct);

    async Task FailedJobAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.OutboxFailedMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var table = JobTables.Of(payload.MessageId);
        var job = await conn.QuerySingleOrDefaultAsync<FailedJobRecord>(
            $"SELECT {table.TypeColumn} AS type, attempts, last_error FROM {table.Table} WHERE id = @Id AND failed_at IS NOT NULL", new FailedJobIdRecord(payload.MessageId));
        if (job is null) return;
        var url = $"{_backofficeUrl}/#/jobs";
        var headline = $"Background job {payload.MessageId} ({job.Type}) gave up";
        var error = (job.LastError ?? "").Split('\n')[0];
        foreach (var admin in await People.AdminsAsync(conn))
            await email.SendAsync(new EmailMessage(admin.Email, headline,
                $"{headline} after {job.Attempts} attempts.\n\n{error}\n\nRetry or discard it: {url}\n",
                $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#c8324b;font-size:20px">{WebUtility.HtmlEncode(headline)}</h1><p>It failed {job.Attempts} times. Last error:</p><pre style="white-space:pre-wrap;font-size:12px;background:#f4f5f8;padding:8px">{WebUtility.HtmlEncode(error)}</pre><p><a href="{WebUtility.HtmlEncode(url)}">Retry or discard it in the back office</a></p></div>"""), ct);
    }
}
