using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ReapSeek.UnitTests;

public partial class ModuleSqlTests
{
    static readonly Dictionary<string, string[]> Tables = new()
    {
        ["identity"] = ["users", "sessions", "account_tokens"],
        ["catalog"] = ["categories", "brands", "products", "product_images", "product_variants", "offers", "buy_box", "seller_snapshots"],
        ["sellers"] = ["sellers"],
        ["ordering"] = ["carts", "cart_lines", "orders", "order_packages", "order_lines", "payments", "payment_events", "shipment_events", "returns",
            "return_lines", "addresses", "order_number_seq", "return_number_seq"],
        ["finance"] = ["seller_ledger", "payouts"],
        ["engagement"] = ["reviews", "questions", "answers", "wishlist_items"],
        ["marketing"] = ["campaigns", "coupons", "reapdeep_alert_subscriptions", "analytics_alerts"],
        ["platform"] = ["outbox"],
    };

    static readonly string[] Modules = ["Identity", "Catalog", "Sellers", "Ordering", "Finance", "Engagement", "Marketing"];
    static readonly string[] Projects = ["ReapSeek.Api", "ReapSeek.Worker", "ReapSeek.Modules"];

    static string Root([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    static string? SchemaOf(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length < 4 || !(Projects.Contains(parts[1]) || parts[1] == "Modules")) return null;
        return Modules.Contains(parts[2]) ? parts[2].ToLowerInvariant() : null;
    }

    [GeneratedRegex("\"\"\"(.*?)\"\"\"|\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\b(SELECT|UPDATE|INSERT|DELETE|nextval)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sql();

    static IEnumerable<(string File, string Module, string Table, string Owner)> CrossModuleSql()
    {
        var owner = Tables.SelectMany(t => t.Value.Select(name => (name, t.Key))).ToDictionary(x => x.name, x => x.Key);
        var table = new Regex(@"\b(" + string.Join('|', owner.Keys.OrderByDescending(k => k.Length)) + @")\b");
        var root = Root();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            if (SchemaOf(relative) is not { } module) continue;
            foreach (Match literal in StringLiteral().Matches(File.ReadAllText(file)))
            {
                var text = literal.Groups[1].Success ? literal.Groups[1].Value : literal.Groups[2].Value;
                if (!Sql().IsMatch(text)) continue;
                foreach (var name in table.Matches(text).Select(m => m.Value).Distinct())
                    if (owner[name] != module && owner[name] != "platform")
                        yield return (relative, module, name, owner[name]);
            }
        }
    }

    [Fact]
    public void Module_SQL_touches_only_its_own_schema()
    {
        var offenders = CrossModuleSql().Select(o => $"{o.File}: {o.Module} -> {o.Owner}.{o.Table}").Distinct().Order().ToList();
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void The_scanner_sees_module_code()
    {
        var root = Root();
        Assert.True(File.Exists(Path.Combine(root, "src", "ReapSeek.Modules", "Ordering", "OrderStore.cs")));
        Assert.Equal("ordering", SchemaOf(Path.Combine("src", "ReapSeek.Api", "Ordering", "Carts", "CartRepository.cs")));
        Assert.Equal("catalog", SchemaOf(Path.Combine("src", "ReapSeek.Worker", "Catalog", "Search", "SearchRepository.cs")));
        Assert.Null(SchemaOf(Path.Combine("src", "ReapSeek.Api", "Seed", "CatalogSeeder.cs")));
        var reviews = Path.Combine("src", "Modules", "Engagement", "ReapSeek.Engagement", "Infrastructure", "ReviewRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, reviews)));
        Assert.Equal("engagement", SchemaOf(reviews));
    }

    [Fact]
    public void Every_table_belongs_to_a_module()
    {
        var known = Tables.SelectMany(t => t.Value).ToHashSet();
        var migrations = Directory.EnumerateFiles(Path.Combine(Root(), "src", "ReapSeek.Shared", "Migrations"), "*.sql").Select(File.ReadAllText);
        var created = migrations.SelectMany(sql => Regex.Matches(sql, @"CREATE (?:TABLE|VIEW|SEQUENCE)(?: IF NOT EXISTS)? (?:\w+\.)?(\w+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)).Where(n => n != "schema_migrations").ToHashSet();
        Assert.Empty(created.Except(known));
    }
}
