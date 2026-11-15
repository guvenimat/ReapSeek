using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Application;
using ReapSeek.Modules.Identity;

namespace ReapSeek.Identity.Api;

internal sealed record ForgotPasswordRequest(string? Email);
internal sealed record ResetPasswordRequest(string? Token, string? Password);
internal sealed record TokenRequest(string? Token);
internal sealed record TokenCheckResponse(bool Valid);

internal static class AccountRecoveryEndpoints
{
    const string InvalidLink = "This link is invalid or has expired. Ask for a new one.";

    public static void MapAccountRecovery(this RouteGroupBuilder auth, RouteGroupBuilder backofficeAuth, IEndpointRouteBuilder api)
    {
        auth.MapPost("/password/forgot", async (ForgotPasswordRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
        {
            await recovery.RequestPasswordResetAsync(req.Email, Roles.Customer, ct);
            return Results.Accepted();
        }).RequireRateLimiting(RateLimits.Auth);
        backofficeAuth.MapPost("/password/forgot", async (ForgotPasswordRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
        {
            await recovery.RequestPasswordResetAsync(req.Email, Roles.Seller, ct);
            return Results.Accepted();
        }).RequireRateLimiting(RateLimits.Auth);

        auth.MapGet("/password/reset", async (string? token, AccountRecoveryService recovery, CancellationToken ct) =>
            ApiResults.Ok(new TokenCheckResponse(await recovery.CheckAsync(token, AccountTokens.PasswordReset, ct)), IdentityJsonContext.Default.TokenCheckResponse));
        auth.MapPost("/password/reset", async (ResetPasswordRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
            await recovery.ResetPasswordAsync(req.Token, req.Password, ct) ? Results.NoContent() : ApiResults.BadRequest(InvalidLink))
            .RequireRateLimiting(RateLimits.Auth);

        auth.MapPost("/email/verify", async (TokenRequest req, AccountRecoveryService recovery, CancellationToken ct) =>
            await recovery.VerifyEmailAsync(req.Token, ct) ? Results.NoContent() : ApiResults.BadRequest(InvalidLink))
            .RequireRateLimiting(RateLimits.Auth);
        auth.MapPost("/email/verification", async (UserInfo user, AccountRecoveryService recovery, CancellationToken ct) =>
            await recovery.ResendVerificationAsync(user.Id, ct) ? Results.Accepted() : ApiResults.Conflict("Your email address is already verified"))
            .RequireCustomer();

        api.MapPost("/backoffice/customers/{id:long}/password-reset", (long id, AccountRecoveryService recovery, CancellationToken ct) =>
            recovery.AdminPasswordResetAsync(id, ct)).RequireAdmin();
    }
}
