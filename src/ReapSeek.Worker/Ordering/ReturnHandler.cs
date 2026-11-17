using ReapSeek.Modules.Identity;
using System.Text.Json;
using Npgsql;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared.Outbox;
using ReapSeek.Modules.Ordering;
using ReapSeek.Ordering.Contracts;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Ordering;

public sealed class ReturnHandler
{
    public sealed record PersonRecord(string Email, string Name);

    readonly NpgsqlDataSource _db;
    readonly EmailSender _email;
    readonly ReapDeepIngestClient _reapDeep;
    readonly IReturnRefunds _refunds;
    readonly string _storefrontUrl;
    readonly string _backofficeUrl;

    public ReturnHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IReturnRefunds refunds, IConfiguration cfg)
    {
        _db = db;
        _email = email;
        _reapDeep = reapDeep;
        _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
        _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');
        _refunds = refunds;
    }

    public static bool Handles(string type) => type is OutboxTypes.ReturnRequested or OutboxTypes.ReturnApproved or OutboxTypes.ReturnRejected
        or OutboxTypes.ReturnReceived or OutboxTypes.ReturnRefunded;

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.ReturnEventMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await _db.OpenConnectionAsync(ct);
        var request = await ReturnStore.LoadAsync(conn, null, payload.ReturnId)
            ?? throw new InvalidOperationException($"Return {payload.Number} was not found");

        switch (message.Type)
        {
            case OutboxTypes.ReturnReceived:
                await _refunds.RefundAsync(request.Id, ct);
                break;
            case OutboxTypes.ReturnRefunded:
                await _reapDeep.EnqueueAsync(conn, RefundEvents.For(request));
                await _email.SendAsync(EmailTemplates.ReturnUpdate(request, await CustomerAsync(conn, request), _storefrontUrl), ct);
                break;
            case OutboxTypes.ReturnRequested:
                foreach (var seller in await People.SellerStaffAsync(conn, request.SellerId))
                    await _email.SendAsync(EmailTemplates.ReturnRequestedForSeller(request, seller.Email, seller.Name, _backofficeUrl), ct);
                break;
            default:
                await _email.SendAsync(EmailTemplates.ReturnUpdate(request, await CustomerAsync(conn, request), _storefrontUrl), ct);
                break;
        }
    }

    static Task<Person> CustomerAsync(NpgsqlConnection conn, ReturnRequest request) => People.GetAsync(conn, request.CustomerId);

}
