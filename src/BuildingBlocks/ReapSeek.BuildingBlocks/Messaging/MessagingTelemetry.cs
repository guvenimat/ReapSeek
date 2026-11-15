using System.Diagnostics.Metrics;
using ReapSeek.Shared.Observability;

namespace ReapSeek.BuildingBlocks.Messaging;

public static class MessagingTelemetry
{
    public static readonly Counter<long> Relayed = Telemetry.Meter.CreateCounter<long>("reapseek.messaging.relayed", "{message}",
        "Integration events copied from a module outbox to subscriber inboxes, by module and event type");
    public static readonly Counter<long> InboxProcessed = Telemetry.Meter.CreateCounter<long>("reapseek.inbox.processed", "{message}",
        "Inbox messages handled, by module, handler and outcome");
    public static readonly Histogram<double> InboxDelay = Telemetry.Meter.CreateHistogram("reapseek.inbox.delay", "s", "Time from publish to successful handling",
        tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600] });
}
