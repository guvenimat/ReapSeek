using ReapSeek.Api.Infrastructure;

if (args.Contains("--migrate"))
{
    Environment.ExitCode = await MigrateCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddReapSeekApi();

var app = builder.Build();
app.UseReapSeekApi();
app.Run();
