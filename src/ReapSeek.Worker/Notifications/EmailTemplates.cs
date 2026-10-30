using System.Net;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Worker.Notifications;

public static class EmailTemplates
{
    public static EmailMessage Welcome(UserRegistered user, string storefrontUrl)
    {
        var name = WebUtility.HtmlEncode(user.Name);
        var url = WebUtility.HtmlEncode(storefrontUrl);
        return new EmailMessage(
            user.Email,
            "Welcome to ReapSeek",
            $"""
            Hi {user.Name},

            Your ReapSeek account is ready. Discover products from hundreds of sellers in one place.

            Start shopping: {storefrontUrl}

            The ReapSeek team
            """,
            $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">Welcome to ReapSeek, {name}</h1>
              <p>Your account is ready. Discover products from hundreds of sellers in one place.</p>
              <p><a href="{url}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Start shopping</a></p>
              <p style="color:#6b7080;font-size:12px">The ReapSeek team</p>
            </div>
            """);
    }
}
