using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace ReapSeek.Sandbox.Payments;

public static class ShipmentStatus
{
    public const string LabelCreated = "label_created";
    public const string InTransit = "in_transit";
    public const string OutForDelivery = "out_for_delivery";
    public const string Delivered = "delivered";
}

public sealed record ShipmentEvent(string Status, DateTime OccurredAt);
public sealed record CreateShipmentRequest(string? Reference, string? WebhookUrl, string? Kind);
public sealed record ShipmentResponse(string TrackingNumber, string Carrier, string Reference, string Kind, string Status, DateTime CreatedAt, List<ShipmentEvent> Events);
public sealed record ShipmentWebhookData(string TrackingNumber, string Reference, string Kind, string Status, DateTime OccurredAt);
public sealed record ShipmentWebhookEvent(string Id, string Type, DateTime Created, ShipmentWebhookData Data);
public sealed record CreateTransferRequest(decimal? Amount, string? Currency, string? Reference, string? Recipient);
public sealed record TransferResponse(string Id, string Status, decimal Amount, string Currency, string Reference, string Recipient, DateTime CreatedAt);
public sealed record CreateRefundRequest(decimal? Amount, string? Reference);
public sealed record RefundResponse(string Id, string PaymentId, string Status, decimal Amount, string Reference, DateTime CreatedAt);

public sealed class ShipmentProgress(LogisticsStore store, WebhookDispatcher webhooks, IConfiguration cfg, ILogger<ShipmentProgress> log) : BackgroundService
{
    readonly TimeSpan _step = TimeSpan.FromSeconds(cfg.GetValue("Sandbox:ShipmentStepSeconds", 3600));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var s in await store.AdvanceAsync(null, now, now - _step)) Notify(s, webhooks);
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                log.LogWarning("Moving shipments failed: {Message}", ex.Message);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    public static void Notify(ShipmentRecord s, WebhookDispatcher webhooks)
    {
        if (s.WebhookUrl is null) return;
        var id = PaymentStore.NewId("evt");
        var body = JsonSerializer.Serialize(new ShipmentWebhookEvent(id, $"shipment.{s.Status}", DateTime.UtcNow,
            new ShipmentWebhookData(s.TrackingNumber, s.Reference, s.Kind, s.Status, s.UpdatedAt)), SandboxJsonContext.Default.ShipmentWebhookEvent);
        webhooks.SendRaw(s.WebhookUrl, id, s.TrackingNumber, body);
    }
}

public static class LogisticsEndpoints
{
    const string Carrier = "Sandbox Cargo";
    static readonly string[] Kinds = ["outbound", "return"];

    public static string NewTrackingNumber() => "SC" + string.Concat(Enumerable.Range(0, 10).Select(_ => RandomNumberGenerator.GetInt32(10)));

    public static WebApplicationBuilder AddLogistics(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<LogisticsStore>();
        builder.Services.AddHostedService<ShipmentProgress>();
        return builder;
    }

    static ShipmentResponse Describe(ShipmentRecord s, List<ShipmentEventRecord> events) =>
        new(s.TrackingNumber, Carrier, s.Reference, s.Kind, s.Status, s.CreatedAt, [.. events.Select(e => new ShipmentEvent(e.Status, e.OccurredAt))]);

    public static void MapLogistics(this WebApplication app)
    {
        var secretKey = app.Configuration["Sandbox:SecretKey"] ?? throw new InvalidOperationException("Sandbox:SecretKey is not set");
        bool Authorized(HttpContext ctx) => ctx.Request.Headers.Authorization.ToString() == $"Bearer {secretKey}";
        IResult Error(int status, string message) => Results.Json(new ErrorResponse(message), SandboxJsonContext.Default.ErrorResponse, statusCode: status);

        app.MapPost("/v1/shipments", async (CreateShipmentRequest req, HttpContext ctx, LogisticsStore store) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            var kind = req.Kind ?? "outbound";
            if (!Kinds.Contains(kind)) return Error(400, "kind must be outbound or return");
            if (req.WebhookUrl is not null && !Uri.TryCreate(req.WebhookUrl, UriKind.Absolute, out _)) return Error(400, "webhook_url must be a URL");
            var (shipment, events) = await store.CreateShipmentAsync(kind, req.Reference.Trim(), req.WebhookUrl, NewTrackingNumber());
            return Results.Json(Describe(shipment, events), SandboxJsonContext.Default.ShipmentResponse, statusCode: 201);
        });

        app.MapGet("/v1/shipments/{tracking}", async (string tracking, HttpContext ctx, LogisticsStore store) =>
            !Authorized(ctx) ? Error(401, "Invalid API key")
            : await store.FindShipmentAsync(tracking) is { } s ? Results.Json(Describe(s.Shipment, s.Events), SandboxJsonContext.Default.ShipmentResponse)
            : Error(404, "Shipment not found"));

        app.MapPost("/v1/shipments/{tracking}/advance", async (string tracking, HttpContext ctx, LogisticsStore store, WebhookDispatcher webhooks) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            var moved = await store.AdvanceAsync(tracking, DateTime.UtcNow, DateTime.MaxValue);
            if (moved.Count == 0)
                return await store.FindShipmentAsync(tracking) is null ? Error(404, "Shipment not found") : Error(409, "The shipment is already delivered");
            ShipmentProgress.Notify(moved[0], webhooks);
            var current = (await store.FindShipmentAsync(tracking))!.Value;
            return Results.Json(Describe(current.Shipment, current.Events), SandboxJsonContext.Default.ShipmentResponse);
        });

        app.MapPost("/v1/transfers", async (CreateTransferRequest req, HttpContext ctx, LogisticsStore store) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (req.Amount is not > 0 || decimal.Round(req.Amount.Value, 2) != req.Amount) return Error(400, "amount must be a positive value with at most two decimals");
            if (req.Currency is not "TRY") return Error(400, "Only TRY is supported");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            if (string.IsNullOrWhiteSpace(req.Recipient) || req.Recipient.Length > 100) return Error(400, "recipient is required");
            var t = await store.TransferAsync(new TransferRecord(PaymentStore.NewId("tr"), "paid", req.Amount.Value, req.Currency, req.Reference.Trim(),
                req.Recipient.Trim(), DateTime.UtcNow));
            return Results.Json(new TransferResponse(t.Id, t.Status, t.Amount, t.Currency, t.Reference, t.Recipient, t.CreatedAt),
                SandboxJsonContext.Default.TransferResponse, statusCode: 201);
        });

        app.MapPost("/v1/payments/{id}/refunds", async (string id, CreateRefundRequest req, HttpContext ctx, PaymentStore payments) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (req.Amount is not > 0 || decimal.Round(req.Amount.Value, 2) != req.Amount) return Error(400, "amount must be a positive value with at most two decimals");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            var (refund, created, error) = await payments.RefundAsync(id, req.Amount.Value, req.Reference.Trim());
            return error switch
            {
                "not_found" => Error(404, "Payment not found"),
                "not_succeeded" => Error(409, "Only a succeeded payment can be refunded"),
                "too_large" => Error(400, "The refund is larger than what is left on the payment"),
                _ => Results.Json(new RefundResponse(refund!.Id, refund.PaymentId, "succeeded", refund.Amount, refund.Reference, refund.CreatedAt),
                    SandboxJsonContext.Default.RefundResponse, statusCode: created ? 201 : 200),
            };
        });
    }
}
