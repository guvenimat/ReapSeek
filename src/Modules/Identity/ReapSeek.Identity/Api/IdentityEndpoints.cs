using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Application;
using ReapSeek.Identity.Infrastructure;
using ReapSeek.Modules.Identity;
using ReapSeek.Shared;

namespace ReapSeek.Identity.Api;

internal static class IdentityEndpoints
{
    public static void MapStorefrontAuth(this RouteGroupBuilder group)
    {
        group.MapPost("/register", Register).RequireRateLimiting(RateLimits.Auth);
        group.MapPost("/login", StorefrontLogin).RequireRateLimiting(RateLimits.Auth);
        group.MapPost("/logout", (HttpContext ctx, SessionService sessions) => Logout(ctx, sessions, SessionAudience.Storefront));
        group.MapGet("/me", (UserInfo user) => ApiResults.Ok(new MeResponse(user), IdentityJsonContext.Default.MeResponse)).RequireCustomer();
        group.MapPost("/password", (PasswordChangeRequest req, UserInfo user, HttpContext ctx, SessionService sessions) =>
            ChangePassword(req, user, ctx, sessions, SessionAudience.Storefront)).RequireCustomer();
    }

    public static void MapBackofficeAuth(this RouteGroupBuilder group)
    {
        group.MapPost("/login", (LoginRequest req, HttpContext ctx, SessionService sessions) => Login(req, ctx, sessions, SessionAudience.Backoffice))
            .RequireRateLimiting(RateLimits.Auth);
        group.MapPost("/logout", (HttpContext ctx, SessionService sessions) => Logout(ctx, sessions, SessionAudience.Backoffice));
        group.MapGet("/me", (UserInfo user) => ApiResults.Ok(new MeResponse(user), IdentityJsonContext.Default.MeResponse)).RequireBackofficeUser();
        group.MapPost("/password", (PasswordChangeRequest req, UserInfo user, HttpContext ctx, SessionService sessions) =>
            ChangePassword(req, user, ctx, sessions, SessionAudience.Backoffice)).RequireBackofficeUser();
    }

    static async Task<IResult> Register(RegisterRequest req, HttpContext ctx, SessionService sessions, AccountRepository accounts, IStorefrontSignIn signIn)
    {
        var registration = IdentityRules.Validate(req);
        var id = await accounts.CreateCustomerAsync(registration.Email, registration.Name, PasswordHasher.Hash(registration.Password), ctx.RequestAborted);
        if (id is null) return ApiResults.Conflict("An account with this email already exists");
        var user = await sessions.SignInAsync(ctx, SessionAudience.Storefront, id.Value);
        await signIn.SignedInAsync(ctx, user.Id, ctx.RequestAborted);
        return ApiResults.Created(new MeResponse(user), IdentityJsonContext.Default.MeResponse);
    }

    static async Task<IResult> Login(LoginRequest req, HttpContext ctx, SessionService sessions, SessionAudience audience)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password) || req.Password.Length > PasswordHasher.MaxLength)
            return ApiResults.BadRequest("email and password are required");
        var credentials = await sessions.AuthenticateAsync(req.Email, req.Password, ctx.RequestAborted);
        if (credentials is null) return ApiResults.Unauthorized("Invalid email or password");
        if (audience == SessionAudience.Backoffice && credentials.Role is not (Roles.Admin or Roles.Seller))
            return ApiResults.Forbidden("This account has no back office access");
        var user = await sessions.SignInAsync(ctx, audience, credentials.Id);
        return ApiResults.Ok(new MeResponse(user), IdentityJsonContext.Default.MeResponse);
    }

    static async Task<IResult> StorefrontLogin(LoginRequest req, HttpContext ctx, SessionService sessions, IStorefrontSignIn signIn)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password) || req.Password.Length > PasswordHasher.MaxLength)
            return ApiResults.BadRequest("email and password are required");
        var credentials = await sessions.AuthenticateAsync(req.Email, req.Password, ctx.RequestAborted);
        if (credentials is null) return ApiResults.Unauthorized("Invalid email or password");
        if (credentials.Suspended) return ApiResults.Forbidden("This account is suspended. Contact customer support.");
        var user = await sessions.SignInAsync(ctx, SessionAudience.Storefront, credentials.Id);
        await signIn.SignedInAsync(ctx, user.Id, ctx.RequestAborted);
        return ApiResults.Ok(new MeResponse(user), IdentityJsonContext.Default.MeResponse);
    }

    static async Task<IResult> Logout(HttpContext ctx, SessionService sessions, SessionAudience audience)
    {
        await sessions.SignOutAsync(ctx, audience);
        return Results.NoContent();
    }

    static async Task<IResult> ChangePassword(PasswordChangeRequest req, UserInfo user, HttpContext ctx, SessionService sessions, SessionAudience audience)
    {
        Ensure.That(!string.IsNullOrEmpty(req.CurrentPassword), "current_password is required");
        Ensure.That(PasswordHasher.IsValidLength(req.NewPassword), PasswordHasher.LengthRule);
        return await sessions.ChangePasswordAsync(ctx, audience, user.Id, req.CurrentPassword, req.NewPassword)
            ? Results.NoContent()
            : ApiResults.BadRequest("Current password is incorrect");
    }
}
