using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ReapSeek.BuildingBlocks.Web;

public sealed record UserInfo(long Id, string Email, string Name, string Role, long? SellerId = null, bool EmailVerified = true)
{
    const string ItemKey = "reapseek.user";

    public static ValueTask<UserInfo?> BindAsync(HttpContext ctx) => ValueTask.FromResult(ctx.Items[ItemKey] as UserInfo);

    public void AttachTo(HttpContext ctx) => ctx.Items[ItemKey] = this;
}

public static class Roles
{
    public const string Customer = "customer";
    public const string Admin = "admin";
    public const string Seller = "seller";
}

public sealed record SessionAudience(string Name, string CookieName, TimeSpan Lifetime, SameSiteMode SameSite)
{
    public static readonly SessionAudience Storefront = new("storefront", "rs_session", TimeSpan.FromDays(30), SameSiteMode.Lax);
    public static readonly SessionAudience Backoffice = new("backoffice", "rs_bo_session", TimeSpan.FromHours(12), SameSiteMode.Strict);
}

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
