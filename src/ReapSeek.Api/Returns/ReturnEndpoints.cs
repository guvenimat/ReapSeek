using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Domain.Returns;
using ReapSeek.Shared;

namespace ReapSeek.Api.Returns;

public static class ReturnEndpoints
{
    static readonly string[] Statuses = Enum.GetValues<ReturnStatus>().Select(ReturnStatusRules.Name).ToArray();

    public static void MapReturns(this RouteGroupBuilder api)
    {
        api.MapPost("/orders/{number}/returns", async (string number, CreateReturnRequest req, UserInfo user, ReturnService returns, CancellationToken ct) =>
            ApiResults.Created(await returns.RequestAsync(user, number, req, ct), AppJsonContext.Default.CreatedReturn)).RequireCustomer();

        var seller = api.MapGroup("/seller/returns").RequireSeller();
        seller.MapGet("/", async (UserInfo user, ReturnService returns, string? status, CancellationToken ct) =>
            ApiResults.Ok(new SellerReturnsResponse(await returns.SellerReturnsAsync(SellerOf(user),
                status is { Length: > 0 } ? Ensure.OneOf(status, "status", Statuses) : null, null, ct)), AppJsonContext.Default.SellerReturnsResponse));
        seller.MapPost("/{id:guid}/approve", async (Guid id, UserInfo user, ReturnService returns, CancellationToken ct) =>
            ApiResults.Ok(await returns.ApproveAsync(SellerOf(user), id, ct), AppJsonContext.Default.SellerReturnRow));
        seller.MapPost("/{id:guid}/reject", async (Guid id, RejectReturnRequest req, UserInfo user, ReturnService returns, CancellationToken ct) =>
            ApiResults.Ok(await returns.RejectAsync(SellerOf(user), id, req, ct), AppJsonContext.Default.SellerReturnRow));

        api.MapPost("/shipping/sandbox/webhook", async (HttpRequest request, ReturnService returns, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            return await returns.HandleShippingWebhookAsync(body, request.Headers["Sandbox-Signature"], ct) switch
            {
                ShippingWebhookOutcome.Rejected => ApiResults.Unauthorized("Invalid signature"),
                ShippingWebhookOutcome.Malformed => ApiResults.BadRequest("Malformed event"),
                _ => Results.Ok(),
            };
        });
    }

    static long SellerOf(UserInfo user) => user.SellerId ?? throw new NotFoundException("Seller not found");
}
