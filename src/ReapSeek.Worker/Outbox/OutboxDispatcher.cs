using System.Text.Json;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;

namespace ReapSeek.Worker.Outbox;

public sealed class OutboxDispatcher(EmailSender email, IConfiguration cfg)
{
    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct) => message.Type switch
    {
        OutboxTypes.UserRegistered => email.SendAsync(EmailTemplates.Welcome(
            Deserialize(message, OutboxJsonContext.Default.UserRegistered), _storefrontUrl), ct),
        _ => throw new NotSupportedException($"Unknown outbox message type: {message.Type}"),
    };

    static T Deserialize<T>(OutboxRepository.MessageRecord message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(message.Payload, type) ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
}
