using System.Text;
using ReapSeek.Shared;

namespace ReapSeek.Api.Catalog;

public sealed record ValidOffer(long SellerId, long VariantId, decimal Price, decimal? ListPrice, int Stock, int HandlingDays, string Status);

public static class CatalogRules
{
    public const int MaxVariants = 100;
    public const int MaxAxes = 3;
    public const int MaxAxisValues = 30;
    public const int MaxAttributes = 30;
    public const int MaxSlugLength = 80;
    public const decimal MaxPrice = 9_999_999.99m;
    public const int MaxStock = 1_000_000;
    static readonly string[] ColorAxisNames = ["color", "colour"];

    public static string Slugify(string text)
    {
        var sb = new StringBuilder(text.Length);
        var dash = false;
        foreach (var raw in text.Trim().ToLowerInvariant())
        {
            var ch = raw switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
                _ => raw,
            };
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
            if (sb.Length >= MaxSlugLength) break;
        }
        return sb.ToString().Trim('-');
    }

    public static string ProductSlug(string title, long id)
    {
        var baseSlug = Slugify(title);
        return (baseSlug.Length > 0 ? baseSlug + "-" : "") + "p" + id;
    }

    public static List<Dictionary<string, string>> VariantMatrix(Dictionary<string, List<string>>? axes)
    {
        var combos = new List<Dictionary<string, string>> { new(StringComparer.Ordinal) };
        if (axes is null || axes.Count == 0) return combos;
        Ensure.That(axes.Count <= MaxAxes, $"variant_axes can have at most {MaxAxes} axes");

        var seenAxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, rawValues) in axes)
        {
            var name = Ensure.NotBlank(rawName, "variant axis name", 40);
            Ensure.That(seenAxes.Add(name), $"Duplicate variant axis: {name}");
            var values = (rawValues ?? []).Select(v => Ensure.NotBlank(v, $"{name} value", 40)).ToList();
            Ensure.That(values.Count is > 0 and <= MaxAxisValues, $"{name} must have 1-{MaxAxisValues} values");
            Ensure.That(values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count, $"{name} has duplicate values");
            Ensure.That(combos.Count * values.Count <= MaxVariants, $"A product can have at most {MaxVariants} variants");
            combos = combos.SelectMany(c => values.Select(v => new Dictionary<string, string>(c, StringComparer.Ordinal) { [name] = v })).ToList();
        }
        return combos;
    }

    public static string? ColorAxis(IEnumerable<string> axisNames) =>
        axisNames.FirstOrDefault(n => ColorAxisNames.Contains(n.Trim().ToLowerInvariant()));

    public static Dictionary<string, string> Attributes(Dictionary<string, string>? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is null) return result;
        Ensure.That(attributes.Count <= MaxAttributes, $"attributes can have at most {MaxAttributes} entries");
        foreach (var (key, value) in attributes)
            result[Ensure.NotBlank(key, "attribute name", 40)] = Ensure.NotBlank(value, $"attribute '{key}'", 200);
        return result;
    }

    public static string Title(string? title)
    {
        var t = Ensure.NotBlank(title, "title", 200);
        Ensure.That(t.Length >= 3, "title must be at least 3 characters");
        return t;
    }

    public static string Description(string? description)
    {
        var d = description?.Trim() ?? "";
        Ensure.That(d.Length <= 5000, "description can be at most 5000 characters");
        return d;
    }

    public static ValidOffer Offer(OfferRequest req)
    {
        Ensure.That(req.SellerId is > 0, "seller_id is required");
        Ensure.That(req.VariantId is > 0, "variant_id is required");
        Ensure.That(req.Price is { } p && p >= 0.01m && p <= MaxPrice, $"price must be between 0.01 and {MaxPrice}");
        var price = Math.Round(req.Price!.Value, 2, MidpointRounding.AwayFromZero);
        var listPrice = req.ListPrice is { } lp ? Math.Round(lp, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
        Ensure.That(listPrice is null || (listPrice >= price && listPrice <= MaxPrice), "list_price must be at least the price");
        var stock = Ensure.InRange(req.Stock ?? 0, 0, MaxStock, "stock");
        var handling = Ensure.InRange(req.HandlingDays ?? 1, 1, 10, "handling_days");
        var status = Ensure.OneOf(req.Status ?? OfferStatus.Active, "status", OfferStatus.All);
        return new ValidOffer(req.SellerId!.Value, req.VariantId!.Value, price, listPrice, stock, handling, status);
    }
}
