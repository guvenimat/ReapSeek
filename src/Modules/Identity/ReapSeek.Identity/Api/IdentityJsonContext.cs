using System.Text.Json.Serialization;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Identity.Infrastructure;

namespace ReapSeek.Identity.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(PasswordChangeRequest))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(CustomersResponse))]
[JsonSerializable(typeof(ForgotPasswordRequest))]
[JsonSerializable(typeof(ResetPasswordRequest))]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenCheckResponse))]
[JsonSerializable(typeof(CustomerDetail))]
[JsonSerializable(typeof(CustomerAnalytics))]
[JsonSerializable(typeof(SuspendRequest))]
internal sealed partial class IdentityJsonContext : JsonSerializerContext;
