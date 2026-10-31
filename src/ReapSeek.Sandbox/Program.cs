using ReapSeek.Sandbox.Payments;

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddSandbox();
builder.AddLogistics();

var app = builder.Build();
app.MapSandbox();
app.MapLogistics();
app.Run();
