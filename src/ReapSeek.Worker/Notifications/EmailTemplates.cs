using System.Net;
using System.Globalization;
using System.Text;
using ReapSeek.Domain.Ordering;
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

    public static EmailMessage OrderConfirmation(Order order, string email, string name, string storefrontUrl)
    {
        static string M(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
        var orderUrl = $"{storefrontUrl}/account/orders/{order.Number}";
        var text = new StringBuilder();
        text.AppendLine($"Hi {name},").AppendLine();
        text.AppendLine($"Thank you for your order {order.Number}. We received your payment of {M(order.Total.Amount)} {order.Total.Currency}.").AppendLine();
        foreach (var p in order.Packages)
        {
            text.AppendLine($"Sold by {p.SellerName}:");
            foreach (var l in p.Lines) text.AppendLine($"  {l.Quantity} x {l.ProductTitle} ({l.VariantLabel}) - {M(l.Total.Amount)}");
            text.AppendLine($"  Shipping: {(p.ShippingFee.IsZero ? "free" : M(p.ShippingFee.Amount))}");
        }
        text.AppendLine().AppendLine($"Total: {M(order.Total.Amount)} {order.Total.Currency}");
        text.AppendLine($"Delivery address: {order.Address.FullName}, {order.Address.Line}, {order.Address.District}/{order.Address.City} {order.Address.PostalCode}");
        text.AppendLine().AppendLine($"Track your order: {orderUrl}").AppendLine().AppendLine("The ReapSeek team");

        var rows = new StringBuilder();
        foreach (var p in order.Packages)
        {
            rows.Append($"<tr><td colspan=\"2\" style=\"padding:12px 0 4px;font-weight:700\">Sold by {WebUtility.HtmlEncode(p.SellerName)}</td></tr>");
            foreach (var l in p.Lines)
                rows.Append($"<tr><td style=\"padding:4px 0\">{l.Quantity} &times; {WebUtility.HtmlEncode(l.ProductTitle)}<br><span style=\"color:#6b7080;font-size:12px\">{WebUtility.HtmlEncode(l.VariantLabel)}</span></td><td style=\"text-align:right\">{M(l.Total.Amount)}</td></tr>");
            rows.Append($"<tr><td style=\"color:#6b7080;font-size:13px\">Shipping</td><td style=\"text-align:right;color:#6b7080;font-size:13px\">{(p.ShippingFee.IsZero ? "Free" : M(p.ShippingFee.Amount))}</td></tr>");
        }
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">Thank you for your order</h1>
              <p>Hi {WebUtility.HtmlEncode(name)}, we received your payment for order <strong>{WebUtility.HtmlEncode(order.Number)}</strong>.</p>
              <table style="width:100%;border-collapse:collapse;font-size:14px">{rows}
                <tr><td style="padding-top:12px;border-top:1px solid #e4e6ee;font-weight:700">Total</td><td style="padding-top:12px;border-top:1px solid #e4e6ee;text-align:right;font-weight:700">{M(order.Total.Amount)} {WebUtility.HtmlEncode(order.Total.Currency)}</td></tr>
              </table>
              <p style="font-size:13px;color:#6b7080">Delivery to {WebUtility.HtmlEncode(order.Address.FullName)}, {WebUtility.HtmlEncode(order.Address.Line)}, {WebUtility.HtmlEncode(order.Address.District)}/{WebUtility.HtmlEncode(order.Address.City)}</p>
              <p><a href="{WebUtility.HtmlEncode(orderUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Track your order</a></p>
              <p style="color:#6b7080;font-size:12px">The ReapSeek team</p>
            </div>
            """;
        return new EmailMessage(email, $"Your ReapSeek order {order.Number}", text.ToString(), html);
    }
}
