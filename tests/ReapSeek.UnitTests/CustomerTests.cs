using ReapSeek.Identity.Api;
using ReapSeek.Identity.Infrastructure;
using System.Text.Json;

namespace ReapSeek.UnitTests;

public class CustomerTests
{
    static ReapDeepEvent Event(string name, string columns, string properties) =>
        new(name, DateTime.UnixEpoch, "customer-1", JsonDocument.Parse(columns).RootElement, JsonDocument.Parse(properties).RootElement);

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("ayse", "%ayse%")]
    [InlineData("50%_off", "%50\\%\\_off%")]
    [InlineData("a\\b", "%a\\\\b%")]
    public void Search_patterns_escape_like_wildcards(string? q, string? expected) => Assert.Equal(expected, CustomerQuery.Pattern(q));

    [Fact]
    public void Every_sort_has_a_stable_order_and_puts_customers_without_orders_last()
    {
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CustomerRow Row(long id, int orders, decimal spent, DateTime? last) => new(id, "n", "e@example.com", at, null, orders, spent, last);
        var rows = new[] { Row(1, 0, 0, null), Row(2, 2, 50, at.AddDays(1)), Row(3, 2, 50, at.AddDays(2)), Row(4, 1, 80, at) };
        Assert.Equal([4L, 3, 2, 1], CustomerQuery.Sort(rows, "spent").Select(r => r.Id));
        Assert.Equal([3L, 2, 4, 1], CustomerQuery.Sort(rows, "orders").Select(r => r.Id));
        Assert.Equal([3L, 2, 4, 1], CustomerQuery.Sort(rows, "last_order").Select(r => r.Id));
        Assert.All(CustomerQuery.Sorts, s => Assert.Equal(4, CustomerQuery.Sort(rows, s).Select(r => r.Id).Distinct().Count()));
    }

    [Fact]
    public void Activity_reads_the_item_from_columns_or_properties()
    {
        Assert.Equal("42", ActivityRules.ItemId(Event("product_viewed", """{"item_id":"42"}""", "{}")));
        Assert.Equal("7", ActivityRules.ItemId(Event("recommendation_clicked", "{}", """{"item_id":"7"}""")));
        Assert.Null(ActivityRules.ItemId(Event("checkout_started", """{"ip":"1.2.3.4"}""", "{}")));
    }

    [Fact]
    public void Activity_details_summarize_the_event()
    {
        Assert.Equal("\"coffee & tea\"", ActivityRules.Detail(Event("search_performed", "{}", """{"query":"coffee & tea","results":24}""")));
        Assert.Equal("RS261031003900, 1619.8 TRY", ActivityRules.Detail(Event("purchase", "{}", """{"order_id":"RS261031003900","amount":1619.8,"currency":"TRY"}""")));
        Assert.Equal("x2", ActivityRules.Detail(Event("add_to_cart", "{}", """{"quantity":2}""")));
        Assert.Null(ActivityRules.Detail(Event("checkout_started", "{}", """{"source":"web"}""")));
    }
}
