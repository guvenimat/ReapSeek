using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace ReapSeek.Sandbox.Payments;

public static class ShipmentStatus
{
    public const string LabelCreated = "label_created";
    public const string InTransit = "in_transit";
    public const string OutForDelivery = "out_for_delivery";
    public const string Delivered = "delivered";
    public static readonly string[] Steps = [LabelCreated, InTransit, OutForDelivery, Delivered];

    public static string? Next(string status) => Array.IndexOf(Steps, status) is var i && i >= 0 && i < Steps.Length - 1 ? Steps[i + 1] : null;
}

public sealed class Shipment
{
    public required string TrackingNumber { get; init; }
    public required string Reference { get; init; }
    public required string Kind { get; init; }
    public string? WebhookUrl { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string Status { get; set; } = ShipmentStatus.LabelCreated;
    public DateTime UpdatedAt { get; set; }
    public List<ShipmentEvent> Events { get; } = [];
}

public sealed class Refund
{
    public required string Id { get; init; }
    public required string PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Reference { get; init; }
    public required DateTime CreatedAt { get; init; }
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

public sealed class LogisticsStore
{
    public ConcurrentDictionary<string, Shipment> Shipments { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, Shipment> ByReference { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, TransferResponse> Transfers { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, Refund> Refunds { get; } = new(StringComparer.Ordinal);
    public object RefundLock { get; } = new();
}

public sealed class ShipmentProgress(LogisticsStore store, WebhookDispatcher webhooks, IConfiguration cfg) : BackgroundService
{
    readonly TimeSpan _step = TimeSpan.FromSeconds(cfg.GetValue("Sandbox:ShipmentStepSeconds", 3600));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            foreach (var s in store.Shipments.Values)
            {
                lock (s)
                {
                    if (now - s.UpdatedAt < _step || ShipmentStatus.Next(s.Status) is not { } next) continue;
                    s.Status = next;
                    s.UpdatedAt = now;
                    s.Events.Add(new ShipmentEvent(next, now));
                }
                Notify(s, webhooks);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    public static void Notify(Shipment s, WebhookDispatcher webhooks)
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

    static ShipmentResponse Describe(Shipment s)
    {
        lock (s) return new(s.TrackingNumber, Carrier, s.Reference, s.Kind, s.Status, s.CreatedAt, [.. s.Events]);
    }

    public static void MapLogistics(this WebApplication app)
    {
        var secretKey = app.Configuration["Sandbox:SecretKey"] ?? throw new InvalidOperationException("Sandbox:SecretKey is not set");
        bool Authorized(HttpContext ctx) => ctx.Request.Headers.Authorization.ToString() == $"Bearer {secretKey}";
        IResult Error(int status, string message) => Results.Json(new ErrorResponse(message), SandboxJsonContext.Default.ErrorResponse, statusCode: status);

        app.MapPost("/v1/shipments", (CreateShipmentRequest req, HttpContext ctx, LogisticsStore store) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            var kind = req.Kind ?? "outbound";
            if (!Kinds.Contains(kind)) return Error(400, "kind must be outbound or return");
            if (req.WebhookUrl is not null && !Uri.TryCreate(req.WebhookUrl, UriKind.Absolute, out _)) return Error(400, "webhook_url must be a URL");
            var now = DateTime.UtcNow;
            var shipment = store.ByReference.GetOrAdd($"{kind}:{req.Reference.Trim()}", _ => new Shipment
            {
                TrackingNumber = NewTrackingNumber(), Reference = req.Reference.Trim(), Kind = kind, WebhookUrl = req.WebhookUrl, CreatedAt = now, UpdatedAt = now,
                Events = { new ShipmentEvent(ShipmentStatus.LabelCreated, now) },
            });
            store.Shipments.TryAdd(shipment.TrackingNumber, shipment);
            return Results.Json(Describe(shipment), SandboxJsonContext.Default.ShipmentResponse, statusCode: 201);
        });

        app.MapGet("/v1/shipments/{tracking}", (string tracking, HttpContext ctx, LogisticsStore store) =>
            !Authorized(ctx) ? Error(401, "Invalid API key")
            : store.Shipments.TryGetValue(tracking, out var s) ? Results.Json(Describe(s), SandboxJsonContext.Default.ShipmentResponse)
            : Error(404, "Shipment not found"));

        app.MapPost("/v1/shipments/{tracking}/advance", (string tracking, HttpContext ctx, LogisticsStore store, WebhookDispatcher webhooks) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (!store.Shipments.TryGetValue(tracking, out var s)) return Error(404, "Shipment not found");
            lock (s)
            {
                if (ShipmentStatus.Next(s.Status) is not { } next) return Error(409, "The shipment is already delivered");
                s.Status = next;
                s.UpdatedAt = DateTime.UtcNow;
                s.Events.Add(new ShipmentEvent(next, s.UpdatedAt));
            }
            ShipmentProgress.Notify(s, webhooks);
            return Results.Json(Describe(s), SandboxJsonContext.Default.ShipmentResponse);
        });

        app.MapPost("/v1/transfers", (CreateTransferRequest req, HttpContext ctx, LogisticsStore store) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (req.Amount is not > 0 || decimal.Round(req.Amount.Value, 2) != req.Amount) return Error(400, "amount must be a positive value with at most two decimals");
            if (req.Currency is not "TRY") return Error(400, "Only TRY is supported");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            if (string.IsNullOrWhiteSpace(req.Recipient) || req.Recipient.Length > 100) return Error(400, "recipient is required");
            var transfer = store.Transfers.GetOrAdd(req.Reference.Trim(), r => new TransferResponse(PaymentStore.NewId("tr"), "paid", req.Amount.Value, req.Currency, r,
                req.Recipient.Trim(), DateTime.UtcNow));
            return Results.Json(transfer, SandboxJsonContext.Default.TransferResponse, statusCode: 201);
        });

        app.MapPost("/v1/payments/{id}/refunds", (string id, CreateRefundRequest req, HttpContext ctx, PaymentStore payments, LogisticsStore store) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (payments.Find(id) is not { } payment) return Error(404, "Payment not found");
            if (payment.Status != PaymentStatus.Succeeded) return Error(409, "Only a succeeded payment can be refunded");
            if (req.Amount is not > 0 || decimal.Round(req.Amount.Value, 2) != req.Amount) return Error(400, "amount must be a positive value with at most two decimals");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            lock (store.RefundLock)
            {
                if (store.Refunds.TryGetValue(req.Reference.Trim(), out var existing))
                    return Results.Json(new RefundResponse(existing.Id, existing.PaymentId, "succeeded", existing.Amount, existing.Reference, existing.CreatedAt),
                        SandboxJsonContext.Default.RefundResponse);
                var refunded = store.Refunds.Values.Where(r => r.PaymentId == id).Sum(r => r.Amount);
                if (refunded + req.Amount.Value > payment.Amount) return Error(400, "The refund is larger than what is left on the payment");
                var refund = new Refund { Id = PaymentStore.NewId("re"), PaymentId = id, Amount = req.Amount.Value, Reference = req.Reference.Trim(), CreatedAt = DateTime.UtcNow };
                store.Refunds[refund.Reference] = refund;
                return Results.Json(new RefundResponse(refund.Id, id, "succeeded", refund.Amount, refund.Reference, refund.CreatedAt),
                    SandboxJsonContext.Default.RefundResponse, statusCode: 201);
            }
        });
    }
}
