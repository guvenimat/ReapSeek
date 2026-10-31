using System.Text.Json;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Media;
using ReapSeek.Api.Search;
using ReapSeek.Shared;

namespace ReapSeek.Api.Catalog;

public sealed class CatalogService(CatalogRepository catalog, CategoryCache categoryCache)
{
    const int MaxPageSize = 100;

    public async Task<CategoryRow> CreateCategoryAsync(CreateCategoryRequest req, CancellationToken ct)
    {
        var name = Ensure.NotBlank(req.Name, "name", 80);
        var slug = CatalogRules.Slugify(string.IsNullOrWhiteSpace(req.Slug) ? name : req.Slug);
        Ensure.That(slug.Length > 0, "slug must contain letters or digits");
        var kind = req.ImageKind?.Trim() is { Length: > 0 } k ? k : null;
        Ensure.That(kind is null || ProductArt.IsKind(kind), $"image_kind: {string.Join(" | ", ProductArt.Kinds.Order())}");

        CatalogRepository.CategoryRecord? parent = null;
        if (req.ParentId is { } parentId)
        {
            parent = await catalog.GetCategoryAsync(parentId, ct) ?? throw new ValidationException("parent_id does not exist");
            Ensure.That(parent.Depth < 2, "Categories can be at most three levels deep");
        }
        var path = parent is null ? slug : $"{parent.Path}/{slug}";
        var id = await catalog.CreateCategoryAsync(parent?.Id, slug, name, path, parent is null ? 0 : parent.Depth + 1, kind ?? parent?.ImageKind ?? "box", ct)
            ?? throw new ConflictException($"A category with the slug '{slug}' already exists");
        await categoryCache.InvalidateAsync();
        return (await catalog.ListCategoriesAsync(ct)).First(c => c.Id == id);
    }

    public async Task<BrandRow> CreateBrandAsync(CreateBrandRequest req, CancellationToken ct)
    {
        var name = Ensure.NotBlank(req.Name, "name", 80);
        var slug = CatalogRules.Slugify(name);
        Ensure.That(slug.Length > 0, "name must contain letters or digits");
        return await catalog.CreateBrandAsync(name, slug, ct) ?? throw new ConflictException($"Brand '{name}' already exists");
    }

    public static ProductFilter Filter(string? q, string? status, string? category, long? brandId, int? page, int? pageSize)
    {
        var search = q?.Trim() is { Length: > 0 } s ? s : null;
        Ensure.That(search is null || search.Length <= 100, "q can be at most 100 characters");
        var st = status?.Trim() is { Length: > 0 } x ? Ensure.OneOf(x, "status", ProductStatus.All) : null;
        var path = category?.Trim().Trim('/') is { Length: > 0 } c ? c : null;
        return new ProductFilter(search, st, path, brandId, Ensure.InRange(page ?? 1, 1, 10_000, "page"), Ensure.InRange(pageSize ?? 25, 1, MaxPageSize, "page_size"));
    }

    public async Task<ProductsResponse> ListProductsAsync(ProductFilter filter, CancellationToken ct)
    {
        var (items, total) = await catalog.ListProductsAsync(filter, ct);
        return new ProductsResponse(items, total, filter.Page, filter.PageSize);
    }

    public async Task<ProductDetail> GetProductAsync(long id, CancellationToken ct) =>
        await catalog.GetProductAsync(id, ct) ?? throw new NotFoundException("Product not found");

    public async Task<ProductDetail> CreateProductAsync(CreateProductRequest req, CancellationToken ct)
    {
        var title = CatalogRules.Title(req.Title);
        var description = CatalogRules.Description(req.Description);
        var attributes = CatalogRules.Attributes(req.Attributes);
        var variants = CatalogRules.VariantMatrix(req.VariantAxes);
        var (brandId, category) = await ResolveAsync(req.BrandId, req.CategoryId, ct);

        var id = await catalog.NextProductIdAsync(ct);
        var colorAxis = CatalogRules.ColorAxis(req.VariantAxes?.Keys ?? Enumerable.Empty<string>());
        var colors = colorAxis is null
            ? [(Name: (string?)null, Art: ProductArt.ColorFor(id))]
            : req.VariantAxes![colorAxis].Select(n => (Name: (string?)n.Trim(), Art: ProductArt.FindColorByName(n.Trim()))).DistinctBy(c => c.Art.Slug).ToList();
        var images = colors.Select((c, i) => new ProductImage(i, ProductArt.Url(category.ImageKind, c.Art.Slug, id),
            c.Name is null ? title : $"{title}, {c.Name}", c.Name)).ToList();

        await catalog.InsertProductAsync(new NewProduct(id, CatalogRules.ProductSlug(title, id), title, brandId, category.Id, description,
            JsonSerializer.Serialize(attributes, AppJsonContext.Default.DictionaryStringString),
            variants.Select((v, i) => ($"RS{id}-{i + 1:00}", JsonSerializer.Serialize(v, AppJsonContext.Default.DictionaryStringString))).ToList(),
            images), ct);
        return await GetProductAsync(id, ct);
    }

    public async Task<ProductDetail> UpdateProductAsync(long id, UpdateProductRequest req, CancellationToken ct)
    {
        var current = await GetProductAsync(id, ct);
        var title = req.Title is null ? current.Title : CatalogRules.Title(req.Title);
        var description = req.Description is null ? current.Description : CatalogRules.Description(req.Description);
        var attributes = req.Attributes is null ? current.Attributes : CatalogRules.Attributes(req.Attributes);
        var status = req.Status is null ? current.Status : Ensure.OneOf(req.Status, "status", ProductStatus.All);
        var (brandId, category) = await ResolveAsync(req.BrandId ?? current.BrandId, req.CategoryId ?? current.CategoryId, ct);

        if (!await catalog.UpdateProductAsync(id, title, brandId, category.Id, description,
                JsonSerializer.Serialize(attributes, AppJsonContext.Default.DictionaryStringString), status, ct))
            throw new NotFoundException("Product not found");
        return await GetProductAsync(id, ct);
    }

    public async Task<ProductDetail> UpsertOfferAsync(OfferRequest req, CancellationToken ct, Func<VariantProductRecord, bool>? allowed = null)
    {
        var offer = CatalogRules.Offer(req);
        if (allowed is not null && (await catalog.ProductOfVariantAsync(offer.VariantId, ct) is not { } product || !allowed(product)))
            throw new NotFoundException("Variant not found");
        var result = await catalog.UpsertOfferAsync(offer, ct) ?? throw new NotFoundException("Seller or variant not found");
        return await GetProductAsync(result.ProductId, ct);
    }

    public async Task<ProductDetail> DeleteOfferAsync(long offerId, CancellationToken ct)
    {
        var productId = await catalog.DeleteOfferAsync(offerId, ct) ?? throw new NotFoundException("Offer not found");
        return await GetProductAsync(productId, ct);
    }

    async Task<(long BrandId, CatalogRepository.CategoryRecord Category)> ResolveAsync(long? brandId, long? categoryId, CancellationToken ct)
    {
        Ensure.That(brandId is > 0, "brand_id is required");
        Ensure.That(categoryId is > 0, "category_id is required");
        Ensure.That(await catalog.BrandExistsAsync(brandId!.Value, ct), "brand_id does not exist");
        var category = await catalog.GetCategoryAsync(categoryId!.Value, ct) ?? throw new ValidationException("category_id does not exist");
        Ensure.That(category.Depth > 0, "Products must be placed in a subcategory, not a department");
        return (brandId.Value, category);
    }
}
