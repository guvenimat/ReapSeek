using Npgsql;

namespace ReapSeek.Api.Identity;

public sealed class BootstrapAdmin(AccountRepository accounts, IConfiguration cfg, ILogger<BootstrapAdmin> log) : IHostedService
{
    const int MaxAttempts = 30;
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task StartAsync(CancellationToken ct)
    {
        var email = cfg["Auth:BootstrapEmail"];
        var password = cfg["Auth:BootstrapPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return;
        email = IdentityRules.NormalizeEmail(email);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (await accounts.CreateFirstAdminAsync(email, "Administrator", PasswordHasher.Hash(password), ct))
                    log.LogInformation("Created bootstrap admin {Email}", email);
                else if (!await accounts.AdminExistsAsync(ct))
                    log.LogWarning("Bootstrap admin {Email} was not created because the email belongs to a customer account", email);
                return;
            }
            catch (NpgsqlException ex) when (attempt < MaxAttempts)
            {
                log.LogWarning("Bootstrap admin check failed ({Message}), retrying", ex.Message);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
