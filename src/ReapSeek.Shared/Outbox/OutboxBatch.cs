namespace ReapSeek.Shared.Outbox;

public sealed record OutboxMessageRecord(long Id, string Type, string Payload, int Attempts, DateTime CreatedAt, string? TraceParent);

public interface ICatalogChangeHandler
{
    Task HandleAsync(IReadOnlyList<OutboxMessageRecord> batch, CancellationToken ct);
}
