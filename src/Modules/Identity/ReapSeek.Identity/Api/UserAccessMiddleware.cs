
using Microsoft.AspNetCore.Http;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Application;

namespace ReapSeek.Identity.Api;

internal sealed class UserAccessMiddleware(RequestDelegate next, SessionService sessions)
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
