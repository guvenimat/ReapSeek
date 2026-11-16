using Microsoft.AspNetCore.Http;

namespace ReapSeek.BuildingBlocks.Web;

public interface ISessionReader
{
    ValueTask<UserInfo?> GetCurrentAsync(HttpContext ctx, SessionAudience audience);
}

public interface IStorefrontSignIn
{
    Task SignedInAsync(HttpContext ctx, long customerId, CancellationToken ct);
}

public interface IStorefrontCart
{
    Task<long[]> ProductIdsAsync(HttpContext ctx, CancellationToken ct);
}
