using System.Text.Json;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Api.Media;

namespace ReapSeek.Api.Seed;

public sealed record SeedCategory(long Id, long? ParentId, string Slug, string Name, string Path, int Depth, int Position, string ImageKind);
public sealed record SeedBrand(long Id, string Name, string Slug);
public sealed record SeedSeller(long Id, string Name, string Slug, string City, decimal Rating, decimal Commission);
public sealed record SeedProduct(long Id, string Slug, string Title, long BrandId, long CategoryId, string Description, string AttributesJson, string Status, DateTime CreatedAt);
public sealed record SeedVariant(long Id, long ProductId, string Sku, string OptionsJson, int Position);
public sealed record SeedImage(long ProductId, int Position, string Url, string Alt, string? Color);
public sealed record SeedOffer(long SellerId, long VariantId, decimal Price, decimal? ListPrice, int Stock, int HandlingDays, string Status);

public sealed record SeedCatalog(
    List<SeedCategory> Categories, List<SeedBrand> Brands, List<SeedSeller> Sellers, List<SeedProduct> Products,
    List<SeedVariant> Variants, List<SeedImage> Images, List<SeedOffer> Offers);

public static class CatalogGenerator
{
    public const int ProductsPerLeaf = 72;
    static readonly string[] ApparelSizes = ["XS", "S", "M", "L", "XL", "XXL"];
    static readonly string[] Storage = ["128 GB", "256 GB", "512 GB"];
    static readonly decimal[] StorageFactor = [1m, 1.15m, 1.35m];
    static readonly string[] Memory = ["8 GB", "16 GB", "32 GB"];
    static readonly decimal[] MemoryFactor = [1m, 1.2m, 1.45m];
    static readonly int[] SellerCountWeights = [45, 30, 17, 8];

    public static SeedCatalog Generate(DateTime now, int productsPerLeaf = ProductsPerLeaf)
    {
        var rng = new Random(2026);
        var categories = new List<SeedCategory>();
        var brands = new List<SeedBrand>();
        var products = new List<SeedProduct>();
        var variants = new List<SeedVariant>();
        var images = new List<SeedImage>();
        var offers = new List<SeedOffer>();

        var sellers = SeedData.Sellers.Select((s, i) => new SeedSeller(i + 1, s.Name, CatalogRules.Slugify(s.Name), s.City, s.Rating,
            Math.Round(0.10m + (decimal)rng.Next(0, 11) / 100m, 2))).ToList();

        long categoryId = 0, brandId = 0, productId = 0, variantId = 0;
        for (var d = 0; d < SeedData.Departments.Length; d++)
        {
            var dept = SeedData.Departments[d];
            var deptId = ++categoryId;
            categories.Add(new SeedCategory(deptId, null, dept.Slug, dept.Name, dept.Slug, 0, d, dept.Kind));
            var deptBrands = dept.Brands.Select(b => new SeedBrand(++brandId, b, CatalogRules.Slugify(b))).ToList();
            brands.AddRange(deptBrands);
            var deptSellers = sellers.Where((_, i) => SeedData.Sellers[i].Departments.Contains(dept.Slug)).ToList();

            for (var l = 0; l < dept.Leaves.Length; l++)
            {
                var leaf = dept.Leaves[l];
                var leafId = ++categoryId;
                categories.Add(new SeedCategory(leafId, deptId, leaf.Slug, leaf.Name, $"{dept.Slug}/{leaf.Slug}", 1, l, leaf.Kind));

                foreach (var (brand, adjective, noun, material) in Combinations(rng, deptBrands, leaf).Take(productsPerLeaf))
                {
                    var id = ++productId;
                    var title = Title(leaf, brand.Name, adjective, noun, material);
                    var status = rng.Next(100) switch { < 96 => ProductStatus.Active, < 99 => ProductStatus.Draft, _ => ProductStatus.Archived };
                    var created = now.AddMinutes(-rng.Next(0, 180 * 24 * 60));
                    products.Add(new SeedProduct(id, CatalogRules.ProductSlug(title, id), title, brand.Id, leafId,
                        Description(leaf, title, brand.Name, material), Json(Attributes(rng, leaf, adjective, material)), status, created));

                    var basePrice = leaf.MinPrice + (decimal)rng.NextDouble() * (leaf.MaxPrice - leaf.MinPrice);
                    var options = Options(rng, dept, leaf);
                    var colors = options.Select(o => o.Options.GetValueOrDefault("Color")).Where(c => c is not null).Distinct().ToList();
                    var hasColorOption = colors.Count > 0;
                    if (!hasColorOption) colors.Add(dept.Colors[rng.Next(dept.Colors.Length)]);
                    for (var i = 0; i < colors.Count; i++)
                    {
                        var color = ProductArt.FindColorByName(colors[i]);
                        images.Add(new SeedImage(id, i, ProductArt.Url(leaf.Kind, color.Slug, id), hasColorOption ? $"{title}, {color.Name}" : title,
                            hasColorOption ? color.Name : null));
                    }

                    var productVariants = options.Select((o, i) => (Variant: new SeedVariant(++variantId, id, $"RS{id}-{i + 1:00}", Json(o.Options), i), o.PriceFactor)).ToList();
                    variants.AddRange(productVariants.Select(v => v.Variant));

                    var sellerCount = Math.Min(Weighted(rng, SellerCountWeights) + 1, deptSellers.Count);
                    foreach (var seller in deptSellers.OrderBy(_ => rng.Next()).Take(sellerCount))
                    {
                        var factor = 0.93m + (decimal)rng.NextDouble() * 0.17m;
                        var handling = rng.Next(1, 4);
                        var discounted = rng.Next(100) < 35;
                        var discount = 1.15m + (decimal)rng.NextDouble() * 0.30m;
                        var paused = rng.Next(100) < 3;
                        foreach (var (variant, priceFactor) in productVariants)
                        {
                            var price = RoundPrice(basePrice * priceFactor * factor);
                            decimal? list = discounted ? Math.Max(RoundPrice(price * discount), price + 10m) : null;
                            var stock = rng.Next(100) < 6 ? 0 : rng.Next(1, 121);
                            offers.Add(new SeedOffer(seller.Id, variant.Id, price, list, stock, handling, paused ? OfferStatus.Paused : OfferStatus.Active));
                        }
                    }
                }
            }
        }
        return new SeedCatalog(categories, brands, sellers, products, variants, images, offers);
    }

    public static decimal RoundPrice(decimal value) => value switch
    {
        < 100m => Math.Floor(value) + 0.90m,
        < 1000m => Math.Floor(value / 10m) * 10m + 9.90m,
        _ => Math.Floor(value / 100m) * 100m + 99m,
    };

    static IEnumerable<(SeedBrand Brand, string Adjective, string Noun, string Material)> Combinations(Random rng, List<SeedBrand> brands, LeafSpec leaf)
    {
        var all = (from b in brands from a in leaf.Adjectives from n in leaf.Nouns from m in leaf.Materials
                   where !Words(a).Overlaps(Words(n + " " + m)) && !Words(m).Overlaps(Words(n))
                   select (b, a, n, m)).ToList();
        for (var i = all.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }
        var titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in all)
            if (titles.Add(Title(leaf, c.b.Name, c.a, c.n, c.m))) yield return c;
    }

    static string Title(LeafSpec leaf, string brand, string adjective, string noun, string material)
    {
        if (leaf.Kind == "book" && leaf.Slug != "board-games") return $"{noun}, {adjective}";
        if (material.Length == 0) return $"{brand} {adjective} {noun}";
        return IsSize(material) ? $"{brand} {adjective} {noun}, {material}" : $"{brand} {adjective} {material} {noun}";
    }

    static HashSet<string> Words(string text) =>
        text.Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    static bool IsSize(string material) => material.Length > 0 && char.IsDigit(material[0]);

    static List<(Dictionary<string, string> Options, decimal PriceFactor)> Options(Random rng, DepartmentSpec dept, LeafSpec leaf)
    {
        List<string> Colors(int max) => dept.Colors.OrderBy(_ => rng.Next()).Take(rng.Next(1, max + 1)).ToList();
        IEnumerable<(Dictionary<string, string>, decimal)> Cross(List<string> colors, string axis, IEnumerable<(string Value, decimal Factor)> values) =>
            from c in colors from v in values select (new Dictionary<string, string> { ["Color"] = c, [axis] = v.Value }, v.Factor);

        switch (leaf.Style)
        {
            case VariantStyle.Apparel:
            {
                var start = rng.Next(0, 3);
                var sizes = ApparelSizes.Skip(start).Take(rng.Next(3, 6)).Select(s => (s, 1m));
                return Cross(Colors(3), "Size", sizes).ToList();
            }
            case VariantStyle.Shoes:
            {
                var (min, max) = leaf.Slug == "kids-shoes" ? (24, 35) : (36, 45);
                var start = rng.Next(min, max - 4);
                var sizes = Enumerable.Range(start, Math.Min(rng.Next(4, 8), max - start + 1)).Select(s => (s.ToString(), 1m));
                return Cross(Colors(2), "Size", sizes).ToList();
            }
            case VariantStyle.Phone:
            {
                var first = rng.Next(0, 2);
                var tiers = Enumerable.Range(first, rng.Next(1, 4 - first)).Select(i => (Storage[i], StorageFactor[i]));
                return Cross(Colors(3), "Storage", tiers).ToList();
            }
            case VariantStyle.Laptop:
            {
                var first = rng.Next(0, 2);
                var tiers = Enumerable.Range(first, rng.Next(1, 3)).Where(i => i < Memory.Length).Select(i => (Memory[i], MemoryFactor[i]));
                return Cross(Colors(1), "Memory", tiers).ToList();
            }
            case VariantStyle.ColorOnly:
                return Colors(3).Select(c => (new Dictionary<string, string> { ["Color"] = c }, 1m)).ToList();
            default:
                return [(new Dictionary<string, string>(), 1m)];
        }
    }

    static Dictionary<string, string> Attributes(Random rng, LeafSpec leaf, string adjective, string material)
    {
        var a = new Dictionary<string, string>();
        switch (leaf.Kind)
        {
            case "dress" or "shirt" or "pants":
                a["Material"] = material;
                a["Fit"] = new[] { "Regular", "Slim", "Relaxed", "Oversized" }[rng.Next(4)];
                a["Care"] = "Machine wash at 30°C";
                break;
            case "shoe":
                a["Upper"] = material;
                a["Sole"] = "Rubber";
                break;
            case "phone":
                a["Screen"] = new[] { "6.1 in", "6.4 in", "6.7 in" }[rng.Next(3)];
                a["Battery"] = $"{rng.Next(40, 56) * 100} mAh";
                a["Camera"] = $"{new[] { 48, 50, 64, 108 }[rng.Next(4)]} MP";
                a["Warranty"] = "2 years";
                break;
            case "laptop":
                a["Screen"] = new[] { "13.3 in", "14 in", "15.6 in", "16 in" }[rng.Next(4)];
                a["Processor"] = new[] { "8-core", "10-core", "12-core" }[rng.Next(3)];
                a["Storage"] = new[] { "512 GB SSD", "1 TB SSD" }[rng.Next(2)];
                a["Warranty"] = "2 years";
                break;
            case "book" when leaf.Slug != "board-games":
                a["Format"] = adjective;
                a["Language"] = "English";
                a["Pages"] = rng.Next(32, 520).ToString();
                break;
            default:
                if (IsSize(material)) a["Size"] = material;
                else if (material.Length > 0) a["Material"] = material;
                break;
        }
        a["Origin"] = rng.Next(100) < 60 ? "Türkiye" : "Imported";
        return a;
    }

    static string Description(LeafSpec leaf, string title, string brand, string material)
    {
        var detail = leaf.Kind switch
        {
            "dress" or "shirt" or "pants" => $"Cut from {(material.Length > 0 ? material.ToLowerInvariant() : "soft fabric")} for all-day comfort, with a fit that works from the office to the weekend.",
            "shoe" => "A cushioned insole and a flexible rubber sole keep you comfortable from the first step.",
            "bag" => "Plenty of room for the essentials, with an inside pocket for your phone and keys.",
            "watch" => "Water resistant to 50 metres, with a scratch-resistant face and an adjustable strap.",
            "phone" => "A bright edge-to-edge display, an all-day battery and a camera that handles low light with ease.",
            "laptop" => "A fast processor, a sharp display and a battery that lasts a full working day.",
            "headphones" => "Rich sound, a comfortable fit and a battery that lasts for long trips.",
            "sofa" => "Deep cushions and a sturdy hardwood frame, delivered and assembled at no extra cost.",
            "lamp" => "Warm, glare-free light that works with standard E27 bulbs.",
            "mug" => "Dishwasher and microwave safe, made to be used every day.",
            "bottle" => "Dermatologically tested and suitable for everyday use.",
            "dumbbell" => "Built for home workouts, with a non-slip grip and a compact footprint.",
            "book" => "A favourite of readers and a great gift.",
            "toy" => "Tested to European toy safety standards and made to last.",
            "jar" => "Carefully sourced and packed fresh for flavour.",
            _ => "Made to last and easy to care for.",
        };
        return leaf.Kind == "book" && leaf.Slug != "board-games" ? $"{title}, published by {brand}. {detail}" : detail;
    }

    static int Weighted(Random rng, int[] weights)
    {
        var roll = rng.Next(weights.Sum());
        for (var i = 0; i < weights.Length; i++)
        {
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return weights.Length - 1;
    }

    static string Json(Dictionary<string, string> map) => JsonSerializer.Serialize(map, AppJsonContext.Default.DictionaryStringString);
}
