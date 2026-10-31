using Npgsql;
using ReapSeek.Sandbox.Payments;

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddSandbox();
builder.AddLogistics();

var app = builder.Build();
await SandboxDatabase.InitializeAsync(app.Configuration.GetConnectionString("Sandbox")!, app.Services.GetRequiredService<NpgsqlDataSource>(),
    app.Services.GetRequiredService<ILogger<Program>>());
app.MapSandbox();
app.MapLogistics();
app.Run();
