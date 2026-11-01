using Npgsql;
using ReapSeek.Sandbox;
using ReapSeek.Sandbox.Payments;

if (args is ["health", var url])
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var res = await http.GetAsync(url);
        Environment.ExitCode = res.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddTelemetry();
builder.AddSandbox();
builder.AddLogistics();

var app = builder.Build();
await SandboxDatabase.InitializeAsync(app.Configuration.GetConnectionString("Sandbox")!, app.Services.GetRequiredService<NpgsqlDataSource>(),
    app.Services.GetRequiredService<ILogger<Program>>());
app.MapSandbox();
app.MapLogistics();
app.Run();
