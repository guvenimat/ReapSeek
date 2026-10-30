using ReapSeek.Api.Infrastructure;

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
