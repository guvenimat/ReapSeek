using ReapSeek.Api.Products;

namespace ReapSeek.UnitTests;

public class ProductTests
{
    static Dictionary<string, string> O(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Axes_put_color_first_and_sort_sizes_like_a_size_chart()
    {
        var axes = ProductRules.Axes([
            O(("Size", "XL"), ("Color", "Navy")),
            O(("Size", "S"), ("Color", "Navy")),
            O(("Size", "M"), ("Color", "Black")),
        ]);
        Assert.Equal(["Color", "Size"], axes.Select(a => a.Name));
        Assert.Equal(["Navy", "Black"], axes[0].Values);
        Assert.Equal(["S", "M", "XL"], axes[1].Values);
    }

    [Fact]
    public void Numeric_option_values_sort_by_their_number()
    {
        var axes = ProductRules.Axes([O(("Storage", "512 GB")), O(("Storage", "128 GB")), O(("Storage", "256 GB")), O(("Material", "Wool"))]);
        Assert.Equal(["Storage", "Material"], axes.Select(a => a.Name));
        Assert.Equal(["128 GB", "256 GB", "512 GB"], axes[0].Values);
        Assert.Equal(["40", "42", "44"], ProductRules.Axes([O(("Size", "44")), O(("Size", "40")), O(("Size", "42"))])[0].Values);
    }

    [Fact]
    public void Products_without_options_have_no_axes() =>
        Assert.Empty(ProductRules.Axes([new Dictionary<string, string>()]));

    [Theory]
    [InlineData(70.0, 100.0, 30)]
    [InlineData(777.0, 999.0, 22)]
    [InlineData(100.0, 100.0, 0)]
    [InlineData(100.0, null, 0)]
    [InlineData(120.0, 100.0, 0)]
    public void Discount_is_a_rounded_percentage_of_the_list_price(double price, double? listPrice, int expected) =>
        Assert.Equal(expected, ProductRules.Discount((decimal)price, (decimal?)listPrice));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(6, null)]
    [InlineData(120, null)]
    public void Only_low_stock_is_revealed(int stock, int? expected) =>
        Assert.Equal(expected, ProductRules.LowStock(stock));
}
