using System.Diagnostics;
using ReapSeek.Shared.Observability;

namespace ReapSeek.UnitTests;

public class TelemetryTests
{
    static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name is "ReapSeek" or "TelemetryTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void An_outbox_job_continues_the_trace_of_the_request_that_queued_it()
    {
        using var listener = Listen();
        using var source = new ActivitySource("TelemetryTests");
        string? traceParent;
        ActivityTraceId requestTrace;
        using (var request = source.StartActivity("POST /checkout"))
        {
            requestTrace = request!.TraceId;
            traceParent = Telemetry.CurrentTraceParent();
        }

        Assert.NotNull(traceParent);
        Assert.Null(Activity.Current);
        using var job = Telemetry.StartConsumer("outbox order_paid", traceParent);
        Assert.Equal(requestTrace, job!.TraceId);
        Assert.Equal(ActivityKind.Consumer, job.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-trace-parent")]
    public void A_job_without_a_valid_trace_parent_starts_a_new_trace(string? traceParent)
    {
        using var listener = Listen();
        using var job = Telemetry.StartConsumer("outbox user_registered", traceParent);
        Assert.NotNull(job);
        Assert.Equal(default, job!.ParentSpanId);
    }
}
