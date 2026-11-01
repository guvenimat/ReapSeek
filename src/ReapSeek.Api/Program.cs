using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Observability;

if (HealthProbe.Handles(args))
{
    Environment.ExitCode = await HealthProbe.RunAsync(args);
    return;
}

if (MigrateCommand.Handles(args))
{
    Environment.ExitCode = await MigrateCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddReapSeekApi();

var app = builder.Build();
app.UseReapSeekApi();
app.Run();
