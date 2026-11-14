using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;
using ReapSeek.Shared.Observability;
using ReapSeek.Worker.Infrastructure;

if (HealthProbe.Handles(args))
{
    Environment.ExitCode = await HealthProbe.RunAsync(args);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddReapSeekWorker();
builder.Build().Run();
