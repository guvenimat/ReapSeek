using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ReapSeek.Sandbox.Payments;

public static class SandboxEndpoints
{
    const decimal MaxAmount = 1_000_000m;

    public static WebApplicationBuilder AddSandbox(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, SandboxJsonContext.Default);
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        });
        builder.Services.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(10));
        var connectionString = builder.Configuration.GetConnectionString("Sandbox") ?? throw new InvalidOperationException("ConnectionStrings:Sandbox is not set");
        builder.Services.AddSingleton(_ => SandboxDatabase.Create(connectionString));
        builder.Services.AddSingleton<PaymentStore>();
        builder.Services.AddSingleton<WebhookDispatcher>();
        return builder;
    }

    public static void MapSandbox(this WebApplication app)
    {
        var cfg = app.Configuration;
        var secretKey = cfg["Sandbox:SecretKey"] ?? throw new InvalidOperationException("Sandbox:SecretKey is not set");
        var publicUrl = (cfg["Sandbox:PublicUrl"] ?? "http://localhost:8101").TrimEnd('/');

        bool Authorized(HttpContext ctx) => ctx.Request.Headers.Authorization.ToString() == $"Bearer {secretKey}";
        PaymentResponse Describe(PaymentRecord p) => new(p.Id, p.Status, p.Amount, p.Currency, p.Reference, $"{publicUrl}/pay/{p.Id}",
            p.CardBrand, p.CardLast4, p.FailureReason, p.CreatedAt);
        IResult Error(int status, string message) => Results.Json(new ErrorResponse(message), SandboxJsonContext.Default.ErrorResponse, statusCode: status);

        app.MapGet("/healthz", () => Results.Text("ok"));

        app.MapPost("/v1/payments", async (CreatePaymentRequest req, HttpContext ctx, PaymentStore store) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (req.Amount is not > 0 || req.Amount > MaxAmount || decimal.Round(req.Amount.Value, 2) != req.Amount) return Error(400, "amount must be a positive value with at most two decimals");
            if (req.Currency is not "TRY") return Error(400, "Only TRY is supported");
            if (string.IsNullOrWhiteSpace(req.Reference) || req.Reference.Length > 100) return Error(400, "reference is required");
            if (!IsHttpUrl(req.ReturnUrl) || !IsHttpUrl(req.WebhookUrl)) return Error(400, "return_url and webhook_url must be http(s) URLs");
            var payment = await store.AddAsync(new PaymentRecord
            {
                Id = PaymentStore.NewId("pay"),
                Amount = req.Amount.Value,
                Currency = req.Currency,
                Reference = req.Reference.Trim(),
                Description = (req.Description ?? req.Reference).Trim(),
                ReturnUrl = req.ReturnUrl!,
                WebhookUrl = req.WebhookUrl!,
                CreatedAt = DateTime.UtcNow,
            });
            return Results.Json(Describe(payment), SandboxJsonContext.Default.PaymentResponse, statusCode: 201);
        });

        app.MapGet("/v1/payments/{id}", async (string id, HttpContext ctx, PaymentStore store) =>
            !Authorized(ctx) ? Error(401, "Invalid API key")
            : await store.FindAsync(id) is { } p ? Results.Json(Describe(p), SandboxJsonContext.Default.PaymentResponse)
            : Error(404, "Payment not found"));

        app.MapPost("/v1/payments/{id}/webhooks/resend", async (string id, HttpContext ctx, PaymentStore store, WebhookDispatcher webhooks) =>
        {
            if (!Authorized(ctx)) return Error(401, "Invalid API key");
            if (await store.FindAsync(id) is not { } p) return Error(404, "Payment not found");
            if (p.Status is not (PaymentStatus.Succeeded or PaymentStatus.Failed)) return Error(409, "The payment is not final yet");
            await webhooks.DeliverAsync(p, ctx.Request.Query["event_id"].ToString() is { Length: > 0 } e ? e : PaymentStore.NewId("evt"));
            return Results.NoContent();
        });

        app.MapGet("/pay/{id}", async (string id, PaymentStore store) =>
            await store.FindAsync(id) is not { } p ? Html(SandboxPages.NotFound(), 404)
            : p.Status == PaymentStatus.RequiresPaymentMethod ? Html(SandboxPages.CardForm(p, null, null))
            : p.Status == PaymentStatus.RequiresAction ? Results.Redirect($"/pay/{p.Id}/3ds")
            : Html(SandboxPages.Result(p)));

        app.MapPost("/pay/{id}", async (string id, HttpContext ctx, PaymentStore store, WebhookDispatcher webhooks) =>
        {
            if (await store.FindAsync(id) is not { } p) return Html(SandboxPages.NotFound(), 404);
            if (p.Status != PaymentStatus.RequiresPaymentMethod) return Results.Redirect($"/pay/{p.Id}");
            var form = await ctx.Request.ReadFormAsync();
            var number = CardRules.Digits(form["card_number"]);
            var holder = form["holder"].ToString().Trim();
            if (holder.Length is 0 or > 100) return Html(SandboxPages.CardForm(p, "Enter the name on the card.", holder), 400);
            var (outcome, error) = CardRules.Check(number, form["expiry"], form["cvc"], DateTime.UtcNow);
            if (outcome == CardOutcome.Invalid) return Html(SandboxPages.CardForm(p, error, holder), 400);

            var declined = outcome is CardOutcome.Declined or CardOutcome.InsufficientFunds;
            var changed = await store.TransitionAsync(new PaymentTransitionRecord(p.Id, PaymentStatus.RequiresPaymentMethod,
                declined ? PaymentStatus.Failed : PaymentStatus.RequiresAction, CardRules.Brand(number), number[^4..],
                !declined ? null : outcome == CardOutcome.Declined ? "card_declined" : "insufficient_funds", true));
            if (changed is null) return Results.Redirect($"/pay/{p.Id}");
            if (!declined) return Results.Redirect($"/pay/{p.Id}/3ds");
            webhooks.Send(changed);
            return Html(SandboxPages.Result(changed));
        });

        app.MapGet("/pay/{id}/3ds", async (string id, PaymentStore store) =>
            await store.FindAsync(id) is not { } p ? Html(SandboxPages.NotFound(), 404)
            : p.Status == PaymentStatus.RequiresAction ? Html(SandboxPages.Authentication(p, null))
            : Results.Redirect($"/pay/{p.Id}"));

        app.MapPost("/pay/{id}/3ds", async (string id, HttpContext ctx, PaymentStore store, WebhookDispatcher webhooks) =>
        {
            if (await store.FindAsync(id) is not { } p) return Html(SandboxPages.NotFound(), 404);
            if (p.Status != PaymentStatus.RequiresAction) return Results.Redirect($"/pay/{p.Id}");
            var form = await ctx.Request.ReadFormAsync();
            var (status, reason) = form["action"] == "cancel" ? (PaymentStatus.Failed, "authentication_cancelled")
                : CardRules.Digits(form["code"]) == CardRules.AuthenticationCode ? (PaymentStatus.Succeeded, (string?)null)
                : (PaymentStatus.Failed, "authentication_failed");
            var changed = await store.TransitionAsync(new PaymentTransitionRecord(p.Id, PaymentStatus.RequiresAction, status, null, null, reason, false));
            if (changed is null) return Results.Redirect($"/pay/{p.Id}");
            webhooks.Send(changed);
            return Results.Redirect(ReturnUrl(changed));
        });
    }

    public static string ReturnUrl(PaymentRecord p) =>
        p.ReturnUrl + (p.ReturnUrl.Contains('?') ? "&" : "?") + "payment_id=" + Uri.EscapeDataString(p.Id);

    static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    static IResult Html(string body, int status = 200) => Results.Content(body, "text/html; charset=utf-8", statusCode: status);

    internal static string Money(decimal amount, string currency) =>
        WebUtility.HtmlEncode($"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}");
}
