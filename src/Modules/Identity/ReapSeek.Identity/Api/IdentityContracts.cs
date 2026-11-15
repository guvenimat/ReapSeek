using ReapSeek.BuildingBlocks.Web;

namespace ReapSeek.Identity.Api;

internal sealed record RegisterRequest(string? Email, string? Name, string? Password);
internal sealed record LoginRequest(string? Email, string? Password);
internal sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);

internal sealed record MeResponse(UserInfo User);
