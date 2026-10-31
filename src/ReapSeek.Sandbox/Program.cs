using ReapSeek.Sandbox.Payments;

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddSandbox();

var app = builder.Build();
app.MapSandbox();
app.MapLogistics();
app.Run();
