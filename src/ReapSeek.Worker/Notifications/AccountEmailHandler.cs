using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Identity;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Notifications;

public sealed class AccountEmailHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg, TimeProvider clock, ILogger<AccountEmailHandler> log)
{
    public sealed record AccountRecord(long Id, string Email, string Name, string Role, bool Suspended, bool Verified);
    public sealed record UserIdRecord(long UserId);
    public sealed record TokenInsertRecord(string TokenHash, long UserId, string Purpose, DateTime CreatedAt, DateTime ExpiresAt);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        if (message.Type == OutboxTypes.UserRegistered)
        {
            var registered = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.UserRegistered)
                ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
            await WelcomeAsync(registered.UserId, ct);
            return;
        }
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.AccountEmailMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        var account = await AccountAsync(payload.UserId, ct);
        if (account is null) return;
        switch (payload.Kind)
        {
            case AccountEmailKinds.PasswordReset when !account.Suspended:
                await email.SendAsync(AccountEmails.PasswordReset(account.Email, account.Name, ResetUrl(account, await IssueAsync(account.Id, AccountTokens.PasswordReset, ct))), ct);
                break;
            case AccountEmailKinds.EmailVerification when !account.Verified && !account.Suspended:
                await email.SendAsync(AccountEmails.Verify(account.Email, account.Name, VerifyUrl(await IssueAsync(account.Id, AccountTokens.EmailVerification, ct))), ct);
                break;
            case AccountEmailKinds.PasswordChanged:
                await email.SendAsync(AccountEmails.PasswordChanged(account.Email, account.Name, ForgotUrl(account), clock.GetUtcNow().UtcDateTime), ct);
                break;
            default:
                log.LogInformation("Skipped {Kind} email for user {User}", payload.Kind, payload.UserId);
                break;
        }
    }

    async Task WelcomeAsync(long userId, CancellationToken ct)
    {
        var account = await AccountAsync(userId, ct);
        if (account is null) return;
        var verifyUrl = account.Verified ? null : VerifyUrl(await IssueAsync(account.Id, AccountTokens.EmailVerification, ct));
        await email.SendAsync(AccountEmails.Welcome(account.Email, account.Name, _storefrontUrl, verifyUrl), ct);
    }

    async Task<AccountRecord?> AccountAsync(long userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AccountRecord>("""
            SELECT id, email, name, role, suspended_at IS NOT NULL AS suspended, email_verified_at IS NOT NULL AS verified FROM users WHERE id = @UserId
            """, new UserIdRecord(userId));
    }

    async Task<string> IssueAsync(long userId, string purpose, CancellationToken ct)
    {
        var token = AccountTokens.New();
        var now = clock.GetUtcNow().UtcDateTime;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync($"UPDATE account_tokens SET used_at = now() WHERE user_id = @UserId AND purpose = '{purpose}' AND used_at IS NULL",
            new UserIdRecord(userId), tx);
        await conn.ExecuteAsync("""
            INSERT INTO account_tokens (token_hash, user_id, purpose, created_at, expires_at) VALUES (@TokenHash, @UserId, @Purpose, @CreatedAt, @ExpiresAt)
            """, new TokenInsertRecord(AccountTokens.Hash(token), userId, purpose, now, now + AccountTokens.Lifetime(purpose)), tx);
        await tx.CommitAsync(ct);
        return token;
    }

    string ResetUrl(AccountRecord a, string token) => a.Role == "customer"
        ? $"{_storefrontUrl}/reset-password?token={token}"
        : $"{_backofficeUrl}/#/reset-password?token={token}";

    string ForgotUrl(AccountRecord a) => a.Role == "customer" ? $"{_storefrontUrl}/forgot-password" : $"{_backofficeUrl}/#/forgot-password";

    string VerifyUrl(string token) => $"{_storefrontUrl}/verify-email?token={token}";
}

public static class AccountEmails
{
    const string Brand = "#5b2bd6";

    static string Button(string url, string label) =>
        $"""<p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:{Brand};color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">{label}</a></p>""";

    static string Html(string headline, string body) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
          <h1 style="color:{Brand};font-size:22px">{WebUtility.HtmlEncode(headline)}</h1>
          {body}
          <p style="color:#6b7080;font-size:12px">The ReapSeek team</p>
        </div>
        """;

    public static EmailMessage Welcome(string to, string name, string storefrontUrl, string? verifyUrl)
    {
        var verifyText = verifyUrl is null ? "" : $"\nPlease confirm your email address so we can reach you about your orders and offers:\n{verifyUrl}\n";
        var verifyHtml = verifyUrl is null ? "" : $"<p>Please confirm your email address so we can reach you about your orders and offers.</p>{Button(verifyUrl, "Confirm my email")}";
        return new EmailMessage(to, "Welcome to ReapSeek",
            $"Hi {name},\n\nYour ReapSeek account is ready. Discover products from hundreds of sellers in one place.\n{verifyText}\nStart shopping: {storefrontUrl}\n\nThe ReapSeek team\n",
            Html($"Welcome to ReapSeek, {name}", $"<p>Your account is ready. Discover products from hundreds of sellers in one place.</p>{verifyHtml}<p><a href=\"{WebUtility.HtmlEncode(storefrontUrl)}\">Start shopping</a></p>"));
    }

    public static EmailMessage Verify(string to, string name, string verifyUrl) =>
        new(to, "Confirm your email address",
            $"Hi {name},\n\nConfirm your email address for ReapSeek:\n{verifyUrl}\n\nThe link works for 3 days. If you did not ask for it, you can ignore this email.\n\nThe ReapSeek team\n",
            Html("Confirm your email address", $"<p>Hi {WebUtility.HtmlEncode(name)}, please confirm your email address for ReapSeek.</p>{Button(verifyUrl, "Confirm my email")}<p style=\"color:#6b7080;font-size:13px\">The link works for 3 days. If you did not ask for it, you can ignore this email.</p>"));

    public static EmailMessage PasswordReset(string to, string name, string resetUrl) =>
        new(to, "Reset your ReapSeek password",
            $"Hi {name},\n\nSomeone asked to reset the password of your ReapSeek account. To choose a new password, open:\n{resetUrl}\n\nThe link works once, for 30 minutes. If it was not you, ignore this email; your password stays the same.\n\nThe ReapSeek team\n",
            Html("Reset your password", $"<p>Hi {WebUtility.HtmlEncode(name)}, someone asked to reset the password of your ReapSeek account.</p>{Button(resetUrl, "Choose a new password")}<p style=\"color:#6b7080;font-size:13px\">The link works once, for 30 minutes. If it was not you, ignore this email; your password stays the same.</p>"));

    public static EmailMessage PasswordChanged(string to, string name, string forgotUrl, DateTime at) =>
        new(to, "Your ReapSeek password was changed",
            $"Hi {name},\n\nThe password of your ReapSeek account was changed on {at:yyyy-MM-dd HH:mm} UTC and you were signed out on your other devices.\n\nIf this was not you, reset your password now: {forgotUrl}\n\nThe ReapSeek team\n",
            Html("Your password was changed", $"<p>Hi {WebUtility.HtmlEncode(name)}, the password of your ReapSeek account was changed on {at:yyyy-MM-dd HH:mm} UTC and you were signed out on your other devices.</p><p>If this was not you, <a href=\"{WebUtility.HtmlEncode(forgotUrl)}\">reset your password now</a>.</p>"));
}
