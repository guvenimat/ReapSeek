namespace ReapSeek.Ordering.Contracts;

public interface IReturnRefunds
{
    Task RefundAsync(Guid returnId, CancellationToken ct);
}
