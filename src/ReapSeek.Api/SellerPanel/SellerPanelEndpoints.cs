using ReapSeek.Api.Catalog;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;
using ReapSeek.Shared.Payouts;

namespace ReapSeek.Api.SellerPanel;

public static class SellerPanelEndpoints
{
    const int PageSize = 20;

    public static void MapSellerPanel(this RouteGroupBuilder api)
    {
        var seller = api.MapGroup("/seller").RequireSeller();
        seller.MapGet("/account", async (UserInfo user, SellerPanelRepository panel, CancellationToken ct) =>
            await panel.AccountAsync(SellerOf(user), ct) is { } account
                ? ApiResults.Ok(account, AppJsonContext.Default.SellerAccount)
                : ApiResults.NotFound("Seller not found"));

        seller.MapGet("/offers", async (UserInfo user, SellerPanelRepository panel, string? q, int? page, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            var search = q?.Trim() is { Length: > 0 } s ? Ensure.NotBlank(s, "q", 100) : null;
            var (rows, total) = await panel.OffersAsync(SellerOf(user), search, p, PageSize, ct);
            return ApiResults.Ok(new SellerOffersResponse(rows, total, p, PageSize), AppJsonContext.Default.SellerOffersResponse);
        });
        seller.MapPut("/offers", async (OfferRequest req, UserInfo user, SellerPanelService service, CancellationToken ct) =>
            ApiResults.Ok(await service.UpsertOfferAsync(SellerOf(user), req, ct), AppJsonContext.Default.SellerProductView));

        seller.MapGet("/catalog/products", async (CatalogService catalog, string? q, string? category, int? page, CancellationToken ct) =>
            ApiResults.Ok(await catalog.ListProductsAsync(CatalogService.Filter(q, ProductStatus.Active, category, null, page, PageSize), ct),
                AppJsonContext.Default.ProductsResponse));
        seller.MapGet("/catalog/products/{id:long}", async (long id, UserInfo user, SellerPanelService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ProductAsync(SellerOf(user), id, ct), AppJsonContext.Default.SellerProductView));
        seller.MapGet("/catalog/categories", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(new CategoriesResponse(await catalog.ListCategoriesAsync(ct)), AppJsonContext.Default.CategoriesResponse));
        seller.MapGet("/catalog/brands", async (CatalogRepository catalog, CancellationToken ct) =>
            ApiResults.Ok(new BrandsResponse(await catalog.ListBrandsAsync(ct)), AppJsonContext.Default.BrandsResponse));
        seller.MapPost("/catalog/products", async (CreateProductRequest req, UserInfo user, SellerPanelService service, CancellationToken ct) =>
            ApiResults.Created(await service.ProposeAsync(SellerOf(user), req, ct), AppJsonContext.Default.SellerProductView));
        seller.MapGet("/proposals", async (UserInfo user, SellerPanelRepository panel, CancellationToken ct) =>
            ApiResults.Ok(new ProposalsResponse(await panel.ProposalsAsync(SellerOf(user), ct)), AppJsonContext.Default.ProposalsResponse));

        seller.MapGet("/packages", async (UserInfo user, SellerPanelRepository panel, string? status, int? page, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            var (rows, total) = await panel.PackagesAsync(SellerOf(user), PackageFilter.Status(status), p, PageSize, ct);
            return ApiResults.Ok(new SellerPackagesResponse(rows, total, p, PageSize), AppJsonContext.Default.SellerPackagesResponse);
        });
        seller.MapGet("/packages/{id:guid}", async (Guid id, UserInfo user, SellerPanelRepository panel, CancellationToken ct) =>
            await panel.PackageAsync(SellerOf(user), id, ct) is { } package
                ? ApiResults.Ok(package, AppJsonContext.Default.SellerPackageRow)
                : ApiResults.NotFound("Package not found"));
        seller.MapPost("/packages/{id:guid}/ship", async (Guid id, UserInfo user, SellerPanelService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ShipAsync(SellerOf(user), id, ct), AppJsonContext.Default.SellerPackageRow));

        seller.MapGet("/balance", async (UserInfo user, SellerPanelRepository panel, TimeProvider clock, CancellationToken ct) =>
            ApiResults.Ok(await panel.BalanceAsync(SellerOf(user), clock.GetUtcNow().UtcDateTime, ct), AppJsonContext.Default.SellerBalanceResponse));

        var admin = api.MapGroup("/backoffice").RequireAdmin();
        admin.MapGet("/payouts", async (SellerPanelRepository panel, CancellationToken ct) =>
            ApiResults.Ok(new PayoutsResponse(await panel.PayoutsAsync(null, ct)), AppJsonContext.Default.PayoutsResponse));
        admin.MapPost("/payouts/run", async (PayoutRunner payouts, TimeProvider clock, CancellationToken ct) =>
            ApiResults.Ok(await payouts.RunAsync(clock.GetUtcNow().UtcDateTime, ct), AppJsonContext.Default.PayoutRunResult));
        admin.MapPost("/catalog/products/{id:long}/review", async (long id, ReviewRequest req, SellerPanelService service, CancellationToken ct) =>
            ApiResults.Ok(await service.ReviewAsync(id, req, ct), AppJsonContext.Default.ProductDetail));
        admin.MapGet("/sellers/{id:long}/users", async (long id, AccountRepository accounts, CancellationToken ct) =>
            ApiResults.Ok(new SellerUsersResponse((await accounts.SellerUsersAsync(id, ct)).Select(u => new SellerUserRow(u.Id, u.Email, u.Name, u.CreatedAt)).ToList()),
                AppJsonContext.Default.SellerUsersResponse));
        admin.MapPost("/sellers/{id:long}/users", CreateSellerUser);
    }

    static async Task<IResult> CreateSellerUser(long id, CreateSellerUserRequest req, AccountRepository accounts, SellerPanelRepository panel, CancellationToken ct)
    {
        _ = await panel.AccountAsync(id, ct) ?? throw new NotFoundException("Seller not found");
        var registration = IdentityRules.Validate(new RegisterRequest(req.Email, req.Name, req.Password));
        var userId = await accounts.CreateSellerUserAsync(id, registration.Email, registration.Name, PasswordHasher.Hash(registration.Password), ct)
            ?? throw new ConflictException("An account with this email already exists");
        return ApiResults.Created(new SellerUserRow(userId, registration.Email, registration.Name, DateTime.UtcNow), AppJsonContext.Default.SellerUserRow);
    }

    static long SellerOf(UserInfo user) => user.SellerId ?? throw new NotFoundException("Seller not found");
}
