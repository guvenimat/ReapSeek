using System.Net.Mail;
using System.Net.Mime;

namespace ReapSeek.Worker.Notifications;

public sealed record EmailMessage(string To, string Subject, string Text, string Html);

public sealed class EmailSender(IConfiguration cfg)
{
    readonly string _host = cfg["Smtp:Host"] ?? "localhost";
    readonly int _port = cfg.GetValue("Smtp:Port", 1025);
    readonly MailAddress _from = new(cfg["Smtp:From"] ?? "no-reply@reapseek.local", "ReapSeek");

    public async Task SendAsync(EmailMessage email, CancellationToken ct)
    {
        using var message = new MailMessage { From = _from, Subject = email.Subject, Body = email.Text, IsBodyHtml = false };
        message.To.Add(email.To);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.Html, null, MediaTypeNames.Text.Html));
        using var client = new SmtpClient(_host, _port);
        await client.SendMailAsync(message, ct);
    }
}
