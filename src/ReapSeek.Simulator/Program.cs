using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ReapSeek.Shared;
using ReapSeek.Shared.Payouts;
using ReapSeek.Simulator;

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
var cfg = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args.Skip(1).ToArray()).Build();
var command = args.FirstOrDefault() ?? "help";
int Int(string key, int fallback) => int.TryParse(cfg[key], out var v) ? v : fallback;

var db = Database.Create(cfg.GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not set"));
using var ingest = new Ingest(cfg["ReapDeep:IngestUrl"] ?? "http://localhost:8000", cfg["ReapDeep:PublicKey"] ?? "");
if (!ingest.Enabled) Console.WriteLine("ReapDeep is not connected (ReapDeep:PublicKey is empty); only ReapSeek data is written.");

var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cancel.Cancel();

switch (command)
{
    case "backfill":
        using (var payouts = new PayoutRunner(db, cfg, NullLogger<PayoutRunner>.Instance))
            await new Backfill(db, ingest, payouts).RunAsync(new WorldOptions(Int("customers", 2000), Int("days", 120), Int("seed", 42), DateTime.UtcNow));
        await SandboxHistory.SyncAsync(db, cfg.GetConnectionString("Sandbox"));
        return 0;
    case "sync-sandbox":
        await SandboxHistory.SyncAsync(db, cfg.GetConnectionString("Sandbox"));
        return 0;
    case "live":
        await new Live(db, ingest, new LiveOptions(cfg["Simulator:ApiUrl"] ?? "http://localhost:8100", cfg["Simulator:SandboxUrl"] ?? "http://localhost:8101",
            Int("shoppers", 3), TimeSpan.FromSeconds(Int("pause", 4)), Int("seed", 7))).RunAsync(cancel.Token);
        await ingest.FlushAsync();
        return 0;
    default:
        Console.WriteLine("Usage: simulator backfill [--customers 2000] [--days 120] [--seed 42]");
        Console.WriteLine("       simulator live [--shoppers 3] [--pause 4]");
        Console.WriteLine("       simulator sync-sandbox");
        return command == "help" ? 0 : 1;
}
