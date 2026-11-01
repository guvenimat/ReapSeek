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
