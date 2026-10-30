using ReapSeek.Api.Catalog;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Api.Sellers;

public static class SellerEndpoints
{
    public static void MapSellersAdmin(this RouteGroupBuilder group)
    {
        group.RequireAdmin();
        group.MapGet("/", async (SellerRepository sellers, CancellationToken ct) =>
            ApiResults.Ok(new SellersResponse(await sellers.ListAsync(ct)), AppJsonContext.Default.SellersResponse));
        group.MapPost("/", Create);
        group.MapPatch("/{id:long}", Update);
    }

    static async Task<IResult> Create(CreateSellerRequest req, SellerRepository sellers, CancellationToken ct)
    {
        var name = Ensure.NotBlank(req.Name, "name", 80);
        var slug = CatalogRules.Slugify(name);
        Ensure.That(slug.Length > 0, "name must contain letters or digits");
        var city = req.City?.Trim() ?? "";
        Ensure.That(city.Length <= 60, "city can be at most 60 characters");
        var id = await sellers.CreateAsync(name, slug, city, SellerRules.Commission(req.CommissionRate), ct)
            ?? throw new ConflictException($"Seller '{name}' already exists");
        return ApiResults.Created((await sellers.GetAsync(id, ct))!, AppJsonContext.Default.SellerRow);
    }

    static async Task<IResult> Update(long id, UpdateSellerRequest req, SellerRepository sellers, CancellationToken ct)
    {
        var current = await sellers.GetAsync(id, ct) ?? throw new NotFoundException("Seller not found");
        var name = req.Name is null ? current.Name : Ensure.NotBlank(req.Name, "name", 80);
        var city = req.City?.Trim() ?? current.City;
        Ensure.That(city.Length <= 60, "city can be at most 60 characters");
        var status = req.Status is null ? current.Status : Ensure.OneOf(req.Status, "status", SellerStatus.All);
        var commission = req.CommissionRate is null ? current.CommissionRate : SellerRules.Commission(req.CommissionRate);
        if (!await sellers.UpdateAsync(id, name, city, status, commission, ct)) throw new NotFoundException("Seller not found");
        return ApiResults.Ok((await sellers.GetAsync(id, ct))!, AppJsonContext.Default.SellerRow);
    }
}
