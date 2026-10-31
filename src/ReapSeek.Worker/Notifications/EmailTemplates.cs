using System.Net;
using System.Globalization;
using System.Text;
using ReapSeek.Domain.Ordering;
using ReapSeek.Domain.Returns;
using ReapSeek.Worker.Orders;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Worker.Notifications;

public static class EmailTemplates
{
    public static EmailMessage ReturnUpdate(ReturnRequest r, ReturnHandler.PersonRecord customer, string storefrontUrl)
    {
        var orderUrl = $"{storefrontUrl}/account/orders/{r.OrderNumber}";
        var items = string.Join(", ", r.Lines.Select(l => $"{l.Quantity} x {l.ProductTitle}"));
        var amount = $"{r.RefundAmount.Amount.ToString("N2", CultureInfo.InvariantCulture)} {r.RefundAmount.Currency}";
        var (subject, headline, body) = r.Status switch
        {
            ReturnStatus.Approved => ($"Your return {r.Number} is approved", "Your return is approved",
                $"Drop the package at any Sandbox Cargo point with the return code {r.ReturnTracking}. We refund {amount} to your card when it reaches the seller."),
            ReturnStatus.Rejected => ($"Your return {r.Number} was not accepted", "Your return was not accepted",
                $"The seller did not accept the return: {r.RejectionNote}"),
            ReturnStatus.Refunded => ($"We refunded {amount} for return {r.Number}", "Your refund is on its way",
                $"The seller received your return. We refunded {amount} to the card you paid with; it can take a few days to appear."),
            _ => ($"Your return {r.Number}", "Your return", $"Your return is {ReturnStatusRules.Name(r.Status)}."),
        };
        var text = $"""
            Hi {customer.Name},

            {body}
            Items: {items}

            Order: {orderUrl}

            The ReapSeek team
            """;
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">{WebUtility.HtmlEncode(headline)}</h1>
              <p>Hi {WebUtility.HtmlEncode(customer.Name)}, {WebUtility.HtmlEncode(body)}</p>
              <p style="font-size:13px;color:#6b7080">Return {WebUtility.HtmlEncode(r.Number)} for order {WebUtility.HtmlEncode(r.OrderNumber)}: {WebUtility.HtmlEncode(items)}</p>
              <p><a href="{WebUtility.HtmlEncode(orderUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">View your order</a></p>
              <p style="color:#6b7080;font-size:12px">The ReapSeek team</p>
            </div>
            """;
        return new EmailMessage(customer.Email, subject, text, html);
    }

    public static EmailMessage ReturnRequestedForSeller(ReturnRequest r, string email, string name, string backofficeUrl)
    {
        var url = $"{backofficeUrl}/#/returns";
        var items = string.Join(", ", r.Lines.Select(l => $"{l.Quantity} x {l.ProductTitle}"));
        var text = $"""
            Hi {name},

            A customer asked to return items from order {r.OrderNumber} (return {r.Number}, reason: {r.Reason.Replace('_', ' ')}).
            Items: {items}

            Approve or reject it in the seller center: {url}
            """;
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">New return request</h1>
              <p>A customer asked to return items from order <strong>{WebUtility.HtmlEncode(r.OrderNumber)}</strong> ({WebUtility.HtmlEncode(r.Reason.Replace('_', ' '))}).</p>
              <p style="font-size:13px;color:#6b7080">{WebUtility.HtmlEncode(items)}</p>
              <p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Review the return</a></p>
            </div>
            """;
        return new EmailMessage(email, $"Return request {r.Number} for order {r.OrderNumber}", text, html);
    }

    public static EmailMessage PackageShipped(Order order, SellerPackage package, string email, string name, string storefrontUrl)
    {
        var orderUrl = $"{storefrontUrl}/account/orders/{order.Number}";
        var items = string.Join(", ", package.Lines.Select(l => $"{l.Quantity} x {l.ProductTitle}"));
        var text = $"""
            Hi {name},

            Your package from {package.SellerName} for order {order.Number} is on its way.
            Tracking number: {package.TrackingNumber}
            Items: {items}

            Track your order: {orderUrl}

            The ReapSeek team
            """;
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">Your package is on its way</h1>
              <p>Hi {WebUtility.HtmlEncode(name)}, {WebUtility.HtmlEncode(package.SellerName)} shipped your package for order <strong>{WebUtility.HtmlEncode(order.Number)}</strong>.</p>
              <p>Tracking number: <strong>{WebUtility.HtmlEncode(package.TrackingNumber ?? "")}</strong></p>
              <p style="font-size:13px;color:#6b7080">{WebUtility.HtmlEncode(items)}</p>
              <p><a href="{WebUtility.HtmlEncode(orderUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Track your order</a></p>
              <p style="color:#6b7080;font-size:12px">The ReapSeek team</p>
            </div>
            """;
        return new EmailMessage(email, $"Your package for order {order.Number} has shipped", text, html);
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
