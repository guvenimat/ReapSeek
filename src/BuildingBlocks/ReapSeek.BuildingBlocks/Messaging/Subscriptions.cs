using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace ReapSeek.BuildingBlocks.Messaging;

public sealed record InboxMessage(long Id, string SourceModule, long SourceMessageId, string EventType, string HandlerName, string Payload,
    int Attempts, DateTime OccurredAt, string? TraceParent);

public interface IInboxHandler<in T>
{
    Task HandleAsync(T message, InboxMessage envelope, CancellationToken ct);
}

public sealed class Subscription(MessagingModule source, string eventType, MessagingModule subscriber, string handler, int maxAttempts,
    Func<IServiceProvider, InboxMessage, CancellationToken, Task> invoke)
{
    public MessagingModule Source => source;
    public string EventType => eventType;
    public MessagingModule Subscriber => subscriber;
    public string Handler => handler;
    public int MaxAttempts => maxAttempts;

    public Task InvokeAsync(IServiceProvider services, InboxMessage message, CancellationToken ct) => invoke(services, message, ct);
}

public sealed partial class Subscriptions
{
    readonly List<Subscription> _all = [];

    public IReadOnlyList<Subscription> All => _all;

    public IReadOnlyList<MessagingModule> Subscribers => _all.Select(s => s.Subscriber).Distinct().ToList();

    public Subscriptions Add<T, THandler>(MessagingModule source, string eventType, JsonTypeInfo<T> typeInfo, MessagingModule subscriber, string handler,
        int maxAttempts = RetryPolicy.MaxAttempts)
        where THandler : class, IInboxHandler<T>
    {
        if (!HandlerName().IsMatch(handler)) throw new ArgumentException($"Invalid handler name: {handler}", nameof(handler));
        if (!EventName().IsMatch(eventType)) throw new ArgumentException($"Invalid event type: {eventType}", nameof(eventType));
        if (!eventType.StartsWith($"{source.Name}.", StringComparison.Ordinal))
            throw new ArgumentException($"Event type {eventType} does not belong to module {source.Name}", nameof(eventType));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (_all.Any(s => s.Subscriber == subscriber && s.Handler == handler))
            throw new InvalidOperationException($"Handler {handler} is already registered in module {subscriber.Name}");
        _all.Add(new Subscription(source, eventType, subscriber, handler, maxAttempts, (services, message, ct) =>
        {
            var payload = JsonSerializer.Deserialize(message.Payload, typeInfo)
                ?? throw new InvalidOperationException($"Inbox message {message.Id} has an empty payload");
            return services.GetRequiredService<THandler>().HandleAsync(payload, message, ct);
        }));
        return this;
    }

    public IReadOnlyList<Subscription> Of(MessagingModule source, string eventType) =>
        _all.Where(s => s.Source == source && s.EventType == eventType).ToList();

    public Subscription? Find(MessagingModule subscriber, string handler) =>
        _all.SingleOrDefault(s => s.Subscriber == subscriber && s.Handler == handler);

    [GeneratedRegex("^[a-z0-9]+(?:[-_][a-z0-9]+)*$")]
    private static partial Regex HandlerName();

    [GeneratedRegex(@"^[a-z]+\.[a-z0-9_]+\.v[0-9]+$")]
    private static partial Regex EventName();
}
