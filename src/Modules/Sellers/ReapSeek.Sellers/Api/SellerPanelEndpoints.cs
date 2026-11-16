using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ReapSeek.BuildingBlocks.Web;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Finance.Contracts;
using ReapSeek.Identity.Contracts;
using ReapSeek.Modules.Finance;
using ReapSeek.Ordering.Contracts;
using ReapSeek.Sellers.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Sellers.Api;

internal static class SellerPanelEndpoints
{
    const int PageSize = 20;

    public static void MapSellerPanel(this IEndpointRouteBuilder api)
    {
        var seller = api.MapGroup("/seller").RequireSeller();
        seller.MapGet("/account", async (UserInfo user, SellerPanelRepository panel, CancellationToken ct) =>
            await panel.AccountAsync(SellerOf(user), ct) is { } account
                ? ApiResults.Ok(account, SellersJsonContext.Default.SellerAccount)
                : ApiResults.NotFound("Seller not found"));

        seller.MapGet("/offers", async (UserInfo user, ISellerCatalog sellerCatalog, string? q, int? page, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            var search = q?.Trim() is { Length: > 0 } s ? Ensure.NotBlank(s, "q", 100) : null;
            return ApiResults.Ok(await sellerCatalog.OffersAsync(SellerOf(user), search, p, PageSize, ct), SellersJsonContext.Default.SellerOffersResponse);
        });
        seller.MapPut("/offers", async (OfferRequest req, UserInfo user, ISellerCatalog sellerCatalog, CancellationToken ct) =>
            ApiResults.Ok(await sellerCatalog.UpsertOfferAsync(SellerOf(user), req, ct), SellersJsonContext.Default.SellerProductView));

        seller.MapGet("/catalog/products", async (ICatalogBrowser catalog, string? q, string? category, int? page, CancellationToken ct) =>
            ApiResults.Ok(await catalog.ListActiveProductsAsync(q, category, page, PageSize, ct), SellersJsonContext.Default.ProductsResponse));
        seller.MapGet("/catalog/products/{id:long}", async (long id, UserInfo user, ISellerCatalog sellerCatalog, CancellationToken ct) =>
            ApiResults.Ok(await sellerCatalog.ProductAsync(SellerOf(user), id, ct), SellersJsonContext.Default.SellerProductView));
        seller.MapGet("/catalog/categories", async (ICatalogBrowser catalog, CancellationToken ct) =>
            ApiResults.Ok(new CategoriesResponse(await catalog.ListCategoriesAsync(ct)), SellersJsonContext.Default.CategoriesResponse));
        seller.MapGet("/catalog/brands", async (ICatalogBrowser catalog, CancellationToken ct) =>
            ApiResults.Ok(new BrandsResponse(await catalog.ListBrandsAsync(ct)), SellersJsonContext.Default.BrandsResponse));
        seller.MapPost("/catalog/products", async (CreateProductRequest req, UserInfo user, ISellerCatalog sellerCatalog, CancellationToken ct) =>
            ApiResults.Created(await sellerCatalog.ProposeAsync(SellerOf(user), req, ct), SellersJsonContext.Default.SellerProductView));
        seller.MapGet("/proposals", async (UserInfo user, ISellerCatalog sellerCatalog, CancellationToken ct) =>
            ApiResults.Ok(new ProposalsResponse(await sellerCatalog.ProposalsAsync(SellerOf(user), ct)), SellersJsonContext.Default.ProposalsResponse));

        seller.MapGet("/packages", async (UserInfo user, ISellerPackages packages, string? status, int? page, CancellationToken ct) =>
        {
            var p = Ensure.InRange(page ?? 1, 1, 1000, "page");
            var (rows, total) = await packages.ListAsync(SellerOf(user), status, p, PageSize, ct);
            return ApiResults.Ok(new SellerPackagesResponse(rows, total, p, PageSize), SellersJsonContext.Default.SellerPackagesResponse);
        });
        seller.MapGet("/packages/{id:guid}", async (Guid id, UserInfo user, ISellerPackages packages, CancellationToken ct) =>
            await packages.GetAsync(SellerOf(user), id, ct) is { } package
                ? ApiResults.Ok(package, SellersJsonContext.Default.SellerPackageRow)
                : ApiResults.NotFound("Package not found"));
        seller.MapPost("/packages/{id:guid}/ship", async (Guid id, UserInfo user, ISellerPackages packages, CancellationToken ct) =>
            ApiResults.Ok(await packages.ShipAsync(SellerOf(user), id, ct), SellersJsonContext.Default.SellerPackageRow));

        seller.MapGet("/balance", async (UserInfo user, ISellerFinance finance, ISellerPackages packages, TimeProvider clock, CancellationToken ct) =>
            ApiResults.Ok(await finance.BalanceAsync(SellerOf(user), clock.GetUtcNow().UtcDateTime, await packages.InTransitAsync(SellerOf(user), ct), ct),
                SellersJsonContext.Default.SellerBalanceResponse));

        var admin = api.MapGroup("/backoffice").RequireAdmin();
        admin.MapGet("/payouts", async (ISellerFinance finance, CancellationToken ct) =>
            ApiResults.Ok(new PayoutsResponse(await finance.PayoutsAsync(null, ct)), SellersJsonContext.Default.PayoutsResponse));
        admin.MapPost("/payouts/run", async (PayoutRunner payouts, TimeProvider clock, CancellationToken ct) =>
            ApiResults.Ok(await payouts.RunAsync(clock.GetUtcNow().UtcDateTime, ct), SellersJsonContext.Default.PayoutRunResult));
        admin.MapPost("/catalog/products/{id:long}/review", async (long id, ReviewRequest req, ISellerCatalog sellerCatalog, CancellationToken ct) =>
            ApiResults.Ok(await sellerCatalog.ReviewProposalAsync(id, req.Decision, req.Note, ct), SellersJsonContext.Default.ProductDetail));
        admin.MapGet("/sellers/{id:long}/users", async (long id, ISellerAccounts accounts, CancellationToken ct) =>
            ApiResults.Ok(new SellerUsersResponse((await accounts.ListAsync(id, ct)).Select(u => new SellerUserRow(u.Id, u.Email, u.Name, u.CreatedAt)).ToList()),
                SellersJsonContext.Default.SellerUsersResponse));
        admin.MapPost("/sellers/{id:long}/users", CreateSellerUser);
    }

    static async Task<IResult> CreateSellerUser(long id, CreateSellerUserRequest req, ISellerAccounts accounts, SellerPanelRepository panel, CancellationToken ct)
    {
        _ = await panel.AccountAsync(id, ct) ?? throw new NotFoundException("Seller not found");
        var created = await accounts.CreateAsync(id, req.Email, req.Name, req.Password, ct);
        return ApiResults.Created(new SellerUserRow(created.Id, created.Email, created.Name, created.CreatedAt), SellersJsonContext.Default.SellerUserRow);
    }

    static long SellerOf(UserInfo user) => user.SellerId ?? throw new NotFoundException("Seller not found");
}
