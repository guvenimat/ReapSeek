using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ReapSeek.Sandbox.Payments;

public static class PaymentStatus
{
    public const string RequiresPaymentMethod = "requires_payment_method";
    public const string RequiresAction = "requires_action";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public sealed class PaymentRecord
{
    public string Id { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Description { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = PaymentStatus.RequiresPaymentMethod;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? FailureReason { get; set; }
    public int Attempts { get; set; }
}

public sealed record CreatePaymentRequest(decimal? Amount, string? Currency, string? Reference, string? Description, string? ReturnUrl, string? WebhookUrl);
public sealed record PaymentResponse(
    string Id, string Status, decimal Amount, string Currency, string Reference, string RedirectUrl, string? CardBrand, string? CardLast4,
    string? FailureReason, DateTime CreatedAt);
public sealed record ErrorResponse(string Error);
public sealed record WebhookData(string PaymentId, string Reference, decimal Amount, string Currency, string Status, string? FailureReason, string? CardLast4);
public sealed record WebhookEvent(string Id, string Type, DateTime Created, WebhookData Data);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CreatePaymentRequest))]
[JsonSerializable(typeof(PaymentResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(WebhookEvent))]
[JsonSerializable(typeof(CreateShipmentRequest))]
[JsonSerializable(typeof(ShipmentResponse))]
[JsonSerializable(typeof(ShipmentWebhookEvent))]
[JsonSerializable(typeof(CreateRefundRequest))]
[JsonSerializable(typeof(RefundResponse))]
[JsonSerializable(typeof(CreateTransferRequest))]
[JsonSerializable(typeof(TransferResponse))]
public sealed partial class SandboxJsonContext : JsonSerializerContext;

public enum CardOutcome { Invalid, Declined, InsufficientFunds, RequiresAuthentication }

public static class CardRules
{
    public const string AuthenticationCode = "123456";
    public const string DeclinedCard = "4000000000000002";
    public const string InsufficientFundsCard = "4000000000009995";

    public static string Digits(string? value) => new((value ?? "").Where(char.IsDigit).ToArray());

    public static bool PassesLuhn(string digits)
    {
        if (digits.Length is < 12 or > 19) return false;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return sum % 10 == 0;
    }

    public static bool IsValidExpiry(string? value, DateTime now)
    {
        var parts = (value ?? "").Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var month) || !int.TryParse(parts[1], out var year) || month is < 1 or > 12) return false;
        if (year < 100) year += 2000;
        return new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1) > now;
    }

    public static (CardOutcome Outcome, string? Error) Check(string cardNumber, string? expiry, string? cvc, DateTime now)
    {
        if (!PassesLuhn(cardNumber)) return (CardOutcome.Invalid, "The card number is not valid.");
        if (!IsValidExpiry(expiry, now)) return (CardOutcome.Invalid, "The expiry date is not valid.");
        if (Digits(cvc).Length is not (3 or 4)) return (CardOutcome.Invalid, "The security code is not valid.");
        return cardNumber switch
        {
            DeclinedCard => (CardOutcome.Declined, null),
            InsufficientFundsCard => (CardOutcome.InsufficientFunds, null),
            _ => (CardOutcome.RequiresAuthentication, null),
        };
    }

    public static string Brand(string digits) => digits switch
    {
        ['4', ..] => "Visa",
        ['5', >= '1' and <= '5', ..] => "Mastercard",
        ['3', '4' or '7', ..] => "American Express",
        ['9', '7', '9', '2', ..] => "Troy",
        _ => "Card",
    };
}

public static class WebhookSignature
{
    public const string Header = "Sandbox-Signature";

    public static string Create(string secret, long timestamp, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{body}"));
        return $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexStringLower(mac)}";
    }
}
