using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ReapSeek.Sandbox;

public static class Telemetry
{
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"))) return builder;
        builder.Logging.AddOpenTelemetry(o => o.IncludeFormattedMessage = true);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("reapseek-sandbox", serviceNamespace: "reapseek"))
            .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/healthz"))
                .AddHttpClientInstrumentation().AddNpgsql())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .UseOtlpExporter();
        return builder;
    }
}
