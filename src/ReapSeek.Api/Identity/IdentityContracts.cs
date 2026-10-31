namespace ReapSeek.Api.Identity;

public sealed record RegisterRequest(string? Email, string? Name, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);

public sealed record UserInfo(long Id, string Email, string Name, string Role, long? SellerId = null)
{
    const string ItemKey = "reapseek.user";

    public static ValueTask<UserInfo?> BindAsync(HttpContext ctx) => ValueTask.FromResult(ctx.Items[ItemKey] as UserInfo);

    public void AttachTo(HttpContext ctx) => ctx.Items[ItemKey] = this;
}

public sealed record MeResponse(UserInfo User);

public static class Roles
{
    public const string Customer = "customer";
    public const string Admin = "admin";
    public const string Seller = "seller";
}
