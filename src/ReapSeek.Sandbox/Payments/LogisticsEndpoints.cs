using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ReapSeek.Sandbox.Payments;

public sealed record CreateShipmentRequest(string? Reference);
public sealed record ShipmentResponse(string TrackingNumber, string Carrier, string Reference, DateTime CreatedAt);
public sealed record CreateTransferRequest(decimal? Amount, string? Currency, string? Reference, string? Recipient);
public sealed record TransferResponse(string Id, string Status, decimal Amount, string Currency, string Reference, string Recipient, DateTime CreatedAt);

public static class LogisticsEndpoints
{
    const string Carrier = "Sandbox Cargo";
    static readonly ConcurrentDictionary<string, ShipmentResponse> Shipments = new();
    static readonly ConcurrentDictionary<string, TransferResponse> Transfers = new();

    public static string NewTrackingNumber() => "SC" + string.Concat(Enumerable.Range(0, 10).Select(_ => RandomNumberGenerator.GetInt32(10)));

    public static void MapLogistics(this WebApplication app)
    {
        var secretKey = app.Configuration["Sandbox:SecretKey"] ?? throw new InvalidOperationException("Sandbox:SecretKey is not set");
        bool Authorized(HttpContext ctx) => ctx.Request.Headers.Authorization.ToString() == $"Bearer {secretKey}";
        IResult Error(int status, string message) => Results.Json(new ErrorResponse(message), SandboxJsonContext.Default.ErrorResponse, statusCode: status);

        app.MapPost("/v1/shipments", (CreateShipmentRequest req, HttpContext ctx) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            var shipment = Shipments.GetOrAdd(req.Reference.Trim(), r => new ShipmentResponse(NewTrackingNumber(), Carrier, r, DateTime.UtcNow));
            return Results.Json(shipment, SandboxJsonContext.Default.ShipmentResponse, statusCode: 201);
        });

        app.MapPost("/v1/transfers", (CreateTransferRequest req, HttpContext ctx) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (req.Amount is not > 0 || decimal.Round(req.Amount.Value, 2) != req.Amount) return Error(400, "amount must be a positive value with at most two decimals");
            if (req.Currency is not "TRY") return Error(400, "Only TRY is supported");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            if (string.IsNullOrWhiteSpace(req.Recipient) || req.Recipient.Length > 100) return Error(400, "recipient is required");
            var transfer = Transfers.GetOrAdd(req.Reference.Trim(), r => new TransferResponse(PaymentStore.NewId("tr"), "paid", req.Amount.Value, req.Currency, r,
                req.Recipient.Trim(), DateTime.UtcNow));
            return Results.Json(transfer, SandboxJsonContext.Default.TransferResponse, statusCode: 201);
        });
    }
}
