using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Operations;

public static class OutboxAdminEndpoints
{
    public static void MapOutboxAdmin(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/backoffice/outbox").RequireAdmin();

        admin.MapGet("/", async (string? state, string? type, OutboxAdminRepository outbox, CancellationToken ct) =>
        {
            var s = string.IsNullOrEmpty(state) ? OutboxStates.Failed : state;
            if (!OutboxStates.All.Contains(s)) return ApiResults.BadRequest("state must be failed, retrying, pending or discarded");
            return ApiResults.Ok(await outbox.ListAsync(s, string.IsNullOrWhiteSpace(type) ? null : type.Trim(), ct), AppJsonContext.Default.OutboxResponse);
        });
        admin.MapPost("/{id:long}/retry", async (long id, OutboxAdminRepository outbox, CancellationToken ct) => Result(await outbox.RetryAsync(id, ct)));
        admin.MapPost("/{id:long}/discard", async (long id, OutboxAdminRepository outbox, CancellationToken ct) => Result(await outbox.DiscardAsync(id, ct)));
        admin.MapPost("/retry", async (OutboxRetryRequest req, OutboxAdminRepository outbox, CancellationToken ct) =>
            ApiResults.Ok(new OutboxRetryResult(await outbox.RetryFailedAsync(string.IsNullOrWhiteSpace(req.Type) ? null : req.Type.Trim(), ct)),
                AppJsonContext.Default.OutboxRetryResult));
    }

    static IResult Result(OutboxChange change) => change switch
    {
        OutboxChange.Changed => Results.NoContent(),
        OutboxChange.NotFound => ApiResults.NotFound("Message not found"),
        _ => ApiResults.Conflict("The message is not in a state that allows this"),
    };
}
