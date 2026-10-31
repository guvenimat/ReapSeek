using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Identity;

public sealed record AccessRequirement(SessionAudience Audience, string? Role);

public static class UserAccess
{
    public static TBuilder RequireCustomer<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AccessRequirement(SessionAudience.Storefront, null));

    public static TBuilder RequireSeller<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AccessRequirement(SessionAudience.Backoffice, Roles.Seller));

    public static TBuilder RequireBackofficeUser<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AccessRequirement(SessionAudience.Backoffice, null));

    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AccessRequirement(SessionAudience.Backoffice, Roles.Admin));
}

public sealed class UserAccessMiddleware(RequestDelegate next, SessionService sessions)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<AccessRequirement>() is { } requirement)
        {
            var user = await sessions.GetCurrentAsync(ctx, requirement.Audience);
            if (user is null)
            {
                await ApiResults.Unauthorized("Not signed in").ExecuteAsync(ctx);
                return;
            }
            if (requirement.Role is not null && user.Role != requirement.Role)
            {
                await ApiResults.Forbidden("You do not have access to this resource").ExecuteAsync(ctx);
                return;
            }
            user.AttachTo(ctx);
        }
        await next(ctx);
    }
}
