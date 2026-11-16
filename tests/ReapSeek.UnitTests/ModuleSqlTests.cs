using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ReapSeek.BuildingBlocks.Messaging;

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

    [GeneratedRegex(@"(?<![\w.])(?:(\w+)\.)?(outbox|inbox)\b")]
    private static partial Regex MessagingTable();

    static string OwnerOf(Match messagingTable) =>
        messagingTable.Groups[1].Success ? messagingTable.Groups[1].Value : messagingTable.Groups[2].Value == "outbox" ? "platform" : "unqualified";

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
                foreach (Match m in MessagingTable().Matches(text))
                    if (OwnerOf(m) is var schema && schema != module && schema != "platform")
                        yield return (relative, module, m.Groups[2].Value, schema);
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
        Assert.Equal("sellers", SchemaOf(Path.Combine("src", "ReapSeek.Api", "Sellers", "SellerRepository.cs")));
        var sellers = Path.Combine("src", "Modules", "Sellers", "ReapSeek.Sellers", "Infrastructure", "SellerRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, sellers)));
        Assert.Equal("sellers", SchemaOf(sellers));
        Assert.Equal("catalog", SchemaOf(Path.Combine("src", "ReapSeek.Worker", "Catalog", "Search", "SearchRepository.cs")));
        Assert.Null(SchemaOf(Path.Combine("src", "ReapSeek.Api", "Seed", "CatalogSeeder.cs")));
        var reviews = Path.Combine("src", "Modules", "Engagement", "ReapSeek.Engagement", "Infrastructure", "ReviewRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, reviews)));
        Assert.Equal("engagement", SchemaOf(reviews));
        var campaigns = Path.Combine("src", "Modules", "Marketing", "ReapSeek.Marketing", "Infrastructure", "CampaignRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, campaigns)));
        Assert.Equal("marketing", SchemaOf(campaigns));
        var ledger = Path.Combine("src", "Modules", "Finance", "ReapSeek.Finance", "Infrastructure", "SellerFinanceRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, ledger)));
        Assert.Equal("finance", SchemaOf(ledger));
        var accounts = Path.Combine("src", "Modules", "Identity", "ReapSeek.Identity", "Infrastructure", "AccountRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, accounts)));
        Assert.Equal("identity", SchemaOf(accounts));
        var products = Path.Combine("src", "Modules", "Catalog", "ReapSeek.Catalog", "Infrastructure", "CatalogRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, products)));
        Assert.Equal("catalog", SchemaOf(products));
        var carts = Path.Combine("src", "Modules", "Ordering", "ReapSeek.Ordering", "Infrastructure", "CartRepository.cs");
        Assert.True(File.Exists(Path.Combine(root, carts)));
        Assert.Equal("ordering", SchemaOf(carts));
    }

    [Fact]
    public void Every_table_belongs_to_a_module()
    {
        var created = MigrationFiles().SelectMany(f => Regex.Matches(f.Sql, @"CREATE (?:TABLE|VIEW|SEQUENCE)(?: IF NOT EXISTS)? ([\w.]+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)).ToList();
        Assert.NotEmpty(created);
        var known = Tables.SelectMany(t => t.Value.Select(n => $"{t.Key}.{n}"))
            .Concat(MessagingModules.All.SelectMany(m => new[] { m.Outbox, m.Inbox })).ToHashSet();
        Assert.Empty(created.Except(known));
        Assert.Empty(known.Except(created));
    }

    [Fact]
    public void Migrations_live_only_in_module_folders()
    {
        var dir = Path.Combine(Root(), "src", "ReapSeek.Shared", "Migrations");
        Assert.Empty(Directory.EnumerateFiles(dir, "*.sql"));
        Assert.Equal(Tables.Keys.Order(), Directory.EnumerateDirectories(dir).Select(Path.GetFileName).Order()!);
        Assert.All(Tables.Keys, m => Assert.True(File.Exists(Path.Combine(dir, m, "0001_baseline.sql")), m));
    }

    [Fact]
    public void A_module_migration_never_mentions_another_module_schema()
    {
        foreach (var (module, file, sql) in MigrationFiles())
            foreach (var other in Tables.Keys.Append("public").Where(s => s != module))
                Assert.False(Regex.IsMatch(sql, $@"\b{other}\s*\.", RegexOptions.IgnoreCase), $"{file} mentions {other}.");
    }

    [Fact]
    public void Every_object_in_a_module_migration_is_created_in_that_module_schema()
    {
        var statement = new Regex(@"\bCREATE\s+(?:OR\s+REPLACE\s+)?(?:UNIQUE\s+)?(?:MATERIALIZED\s+)?(TABLE|VIEW|SEQUENCE|FUNCTION|PROCEDURE|TYPE|DOMAIN|TRIGGER|INDEX|SCHEMA)\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?([\w.]+)(?:[^;]*?\bON\s+([\w.]+))?",
            RegexOptions.IgnoreCase);
        var count = 0;
        foreach (var (module, file, sql) in MigrationFiles())
            foreach (Match m in statement.Matches(sql))
            {
                var kind = m.Groups[1].Value.ToUpperInvariant();
                var target = kind is "INDEX" or "TRIGGER" ? m.Groups[3].Value : m.Groups[2].Value;
                if (kind == "SCHEMA") Assert.Equal(module, target);
                else Assert.True(target.StartsWith($"{module}.", StringComparison.Ordinal), $"{file}: {m.Value}");
                count++;
            }
        Assert.True(count > 50);
    }

    [Fact]
    public void Every_module_has_its_own_outbox_and_inbox_with_its_own_inbox_id_range()
    {
        Assert.Equal(Tables.Keys.Where(m => m != "platform").Order(), MessagingModules.All.Select(m => m.Name).Order());
        Assert.Equal(MessagingModules.All.Count, MessagingModules.All.Select(m => m.Ordinal).Distinct().Count());
        foreach (var module in MessagingModules.All)
        {
            var sql = string.Concat(MigrationFiles().Where(f => f.Module == module.Name).Select(f => f.Sql));
            Assert.Contains($"CREATE TABLE {module.Outbox} (", sql);
            Assert.Contains($"CREATE TABLE {module.Inbox} (", sql);
            Assert.Contains($"START WITH {module.FirstInboxId} MINVALUE {module.FirstInboxId} MAXVALUE {module.FirstInboxId + MessagingModules.InboxIdRange - 2}", sql);
            Assert.Contains("UNIQUE (source_module, source_message_id, handler_name)", sql);
            Assert.Equal(module, MessagingModules.OfInboxId(module.FirstInboxId));
        }
        Assert.Null(MessagingModules.OfInboxId(42));
        Assert.True((MessagingModules.All.Max(m => m.Ordinal) + 1) * MessagingModules.InboxIdRange < 1L << 53);
    }

    [Fact]
    public void Messaging_tables_are_owned_by_their_qualifying_schema()
    {
        string[] Owners(string sql) => MessagingTable().Matches(sql).Select(OwnerOf).ToArray();
        Assert.Equal(["engagement", "catalog"], Owners("INSERT INTO engagement.outbox SELECT * FROM catalog.inbox"));
        Assert.Equal(["platform", "unqualified"], Owners("SELECT 1 FROM outbox, inbox"));
        Assert.Empty(Owners("SELECT outbox_id, x.outbox_failed FROM t"));
    }

    static IEnumerable<(string Module, string File, string Sql)> MigrationFiles()
    {
        var dir = Path.Combine(Root(), "src", "ReapSeek.Shared", "Migrations");
        return Directory.EnumerateDirectories(dir).SelectMany(d => Directory.EnumerateFiles(d, "*.sql")
            .Select(f => (Path.GetFileName(d), Path.GetRelativePath(dir, f), File.ReadAllText(f))));
    }
}
