using ReapSeek.Worker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddReapSeekWorker();
builder.Build().Run();
