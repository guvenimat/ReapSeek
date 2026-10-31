using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ReapSeek.Shared.Observability;

public static class Telemetry
{
    public const string Name = "ReapSeek";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> OrdersPlaced = Meter.CreateCounter<long>("reapseek.orders.placed", "{order}", "Orders placed at checkout");
    public static readonly Counter<long> OrdersPaid = Meter.CreateCounter<long>("reapseek.orders.paid", "{order}", "Orders whose payment succeeded");
    public static readonly Counter<double> Revenue = Meter.CreateCounter<double>("reapseek.orders.revenue", "TRY", "Paid order totals");
    public static readonly Counter<long> PaymentsFailed = Meter.CreateCounter<long>("reapseek.payments.failed", "{payment}", "Payments that failed, by reason");
    public static readonly Counter<long> OutboxProcessed = Meter.CreateCounter<long>("reapseek.outbox.processed", "{message}", "Outbox messages handled, by type and outcome");
    public static readonly Histogram<double> OutboxDelay = Meter.CreateHistogram("reapseek.outbox.delay", "s", "Time from enqueue to successful handling",
        tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300, 900, 3600] });

    public static bool IsEnabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    public static OpenTelemetryBuilder? AddReapSeekTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        if (!IsEnabled) return null;
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: "reapseek"))
            .WithTracing(t => t.AddSource(Name).AddHttpClientInstrumentation().AddNpgsql())
            .WithMetrics(m => m.AddMeter(Name, "Npgsql").AddHttpClientInstrumentation().AddRuntimeInstrumentation());
        otel.UseOtlpExporter();
        return otel;
    }

    public static string? CurrentTraceParent() => Activity.Current?.Id;

    public static Activity? StartConsumer(string name, string? traceParent) =>
        ActivityContext.TryParse(traceParent, null, out var parent)
            ? Source.StartActivity(name, ActivityKind.Consumer, parent)
            : Source.StartActivity(name, ActivityKind.Consumer);
}
