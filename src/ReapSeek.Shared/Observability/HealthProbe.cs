namespace ReapSeek.Shared.Observability;

public static class HealthProbe
{
    public static readonly string DefaultHeartbeatFile = Path.Combine(Path.GetTempPath(), "reapseek-worker.alive");
    static readonly TimeSpan BeatEvery = TimeSpan.FromSeconds(10);
    static DateTime _lastBeat;

    public static bool Handles(string[] args) => args.Length > 0 && args[0] == "health";

    public static async Task<int> RunAsync(string[] args)
    {
        var target = args.Length > 1 ? args[1] : "";
        if (target.StartsWith("http", StringComparison.Ordinal))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var res = await http.GetAsync(target);
                return res.IsSuccessStatusCode ? 0 : 1;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return 1;
            }
        }
        if (target == "heartbeat")
        {
            var maxAge = TimeSpan.FromSeconds(args.Length > 2 && int.TryParse(args[2], out var s) ? s : 60);
            var file = args.Length > 3 ? args[3] : DefaultHeartbeatFile;
            return File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < maxAge ? 0 : 1;
        }
        await Console.Error.WriteLineAsync("Usage: health <url> | health heartbeat [max-age-seconds] [file]");
        return 2;
    }

    public static void Beat(string? file = null)
    {
        var now = DateTime.UtcNow;
        if (now - _lastBeat < BeatEvery) return;
        _lastBeat = now;
        try
        {
            File.WriteAllText(file ?? DefaultHeartbeatFile, now.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lastBeat = default;
        }
    }
}
