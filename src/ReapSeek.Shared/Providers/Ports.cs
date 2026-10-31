namespace ReapSeek.Shared.Providers;

public sealed record NewPayment(decimal Amount, string Currency, string Reference, string Description, string ReturnUrl, string WebhookUrl);
public sealed record PaymentSession(string Id, string RedirectUrl, decimal Amount, string Currency);
public sealed record Shipment(string TrackingNumber, string Carrier);

public static class ShipmentKinds
{
    public const string Outbound = "outbound";
    public const string Return = "return";
}

public interface IPaymentGateway
{
    string Name { get; }
    Task<PaymentSession> CreatePaymentAsync(NewPayment payment, CancellationToken ct);
    Task<string> RefundAsync(string paymentId, decimal amount, string reference, CancellationToken ct);
}

public interface ICarrier
{
    Task<Shipment> CreateShipmentAsync(string reference, string kind, CancellationToken ct);
}

public interface IPayoutBank
{
    Task<string> TransferAsync(decimal amount, string currency, string reference, string recipient, CancellationToken ct);
}

public sealed class PaymentProviderException(string message) : Exception(message);

public sealed class ShippingProviderException(string message) : Exception(message);
