namespace ReapSeek.Api.Identity;

public sealed record RegisterRequest(string? Email, string? Name, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);

public sealed record MeResponse(UserInfo User);
