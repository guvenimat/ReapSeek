using System.Globalization;

namespace ReapSeek.Api.Products;

public static class ProductRules
{
    public const int LowStockThreshold = 5;
    static readonly string[] AxisOrder = ["Color", "Size", "Storage", "Memory"];
    static readonly string[] SizeOrder = ["XS", "S", "M", "L", "XL", "XXL"];

    public static List<VariantAxis> Axes(IEnumerable<Dictionary<string, string>> variantOptions)
    {
        var options = variantOptions.ToList();
        return options.SelectMany(o => o.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(k => Array.IndexOf(AxisOrder, k) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(k => k, StringComparer.Ordinal)
            .Select(axis => new VariantAxis(axis, SortValues(axis, options.Select(o => o.GetValueOrDefault(axis)).OfType<string>().Distinct(StringComparer.Ordinal))))
            .ToList();
    }

    static List<string> SortValues(string axis, IEnumerable<string> values)
    {
        var list = values.ToList();
        if (axis == "Color") return list;
        return list
            .OrderBy(v => Array.IndexOf(SizeOrder, v) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(v => LeadingNumber(v))
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    static double LeadingNumber(string value)
    {
        var digits = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.MaxValue;
    }

    public static int Discount(decimal price, decimal? listPrice) =>
        listPrice is { } lp && lp > price ? (int)Math.Round((1 - price / lp) * 100, MidpointRounding.AwayFromZero) : 0;

    public static int? LowStock(int stock) => stock <= LowStockThreshold ? stock : null;
}
