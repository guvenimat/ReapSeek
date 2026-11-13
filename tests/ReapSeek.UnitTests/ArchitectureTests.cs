using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using ReapSeek.Domain.Common;
using ReapSeek.Shared.Providers;

namespace ReapSeek.UnitTests;

public class ArchitectureTests
{
    static readonly Assembly Domain = typeof(AggregateRoot).Assembly;
    static readonly Assembly Shared = typeof(IPaymentGateway).Assembly;
    static readonly Assembly ModulesAssembly = typeof(ReapSeek.Modules.Ordering.OrderStore).Assembly;
    static readonly Assembly Api = typeof(ReapSeek.Api.Infrastructure.ApiResults).Assembly;
    static readonly Assembly Worker = typeof(ReapSeek.Worker.Outbox.OutboxDispatcher).Assembly;

    static readonly string[] ApiModules = ["Identity", "Catalog", "Sellers", "Ordering", "Engagement", "Marketing"];
    static readonly string[] PlatformAreas = ["Infrastructure", "Seed", "Health", "Operations"];

    static readonly Dictionary<string, string[]> PublicApi = new()
    {
        ["Identity"] = ["UserInfo", "UserAccess", "SessionService", "SessionAudience", "SellerAccounts", "SellerLogin"],
        ["Catalog"] = ["CatalogService", "CatalogRules", "SearchService", "ProductCard", "ProductStatus",
            "ProductFilter", "ProductsResponse", "ProductDetail", "CreateProductRequest", "UpdateProductRequest", "OfferRequest", "CategoriesResponse", "BrandsResponse", "VariantRow", "OfferRow", "ProductImage", "VariantProductRecord",
            "SellerCatalog", "SellerOfferRow", "ProposalRow", "BuyBoxRecord"],
        ["Sellers"] = [],
        ["Ordering"] = ["CartService"],
        ["Engagement"] = [],
        ["Marketing"] = [],
    };

    static IEnumerable<string> TopLevelAreas => Api.GetTypes()
        .Select(t => t.Namespace).OfType<string>()
        .Where(n => n.StartsWith("ReapSeek.Api.", StringComparison.Ordinal))
        .Select(n => n["ReapSeek.Api.".Length..].Split('.')[0])
        .Distinct();

    static string ModuleOf(Type t) => t.Namespace!["ReapSeek.Api.".Length..].Split('.')[0];

    static string[] OutsideOf(string module) =>
        ApiModules.Where(m => m != module).Select(m => $"ReapSeek.Api.{m}").ToArray();

    static List<string> ModuleViolations(string module, string[] targets)
    {
        if (targets.Length == 0) return [];
        var failures = new List<string>();
        foreach (var ns in OutsideOf(module))
        {
            var r = Types.InAssembly(Api).That().ResideInNamespaceStartingWith(ns + ".").Or().ResideInNamespace(ns)
                .ShouldNot().HaveDependencyOnAny(targets).GetResult();
            if (!r.IsSuccessful) failures.AddRange(r.FailingTypeNames.Select(t => $"{t} -> {module}"));
        }
        return failures;
    }

    static void Pass(TestResult result, string rule) =>
        Assert.True(result.IsSuccessful, $"{rule}: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void The_domain_depends_on_nothing_but_the_base_library() =>
        Pass(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "Dapper", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions", "StackExchange.Redis", "System.Net.Http", "System.Text.Json",
            "ReapSeek.Shared", "ReapSeek.Modules", "ReapSeek.Api", "ReapSeek.Worker").GetResult(), "Domain");

    [Fact]
    public void Shared_code_knows_neither_the_API_nor_the_worker() =>
        Pass(Types.InAssembly(Shared).ShouldNot().HaveDependencyOnAny("ReapSeek.Api", "ReapSeek.Worker", "Microsoft.AspNetCore").GetResult(), "Shared");

    [Fact]
    public void Shared_code_is_infrastructure_only() =>
        Pass(Types.InAssembly(Shared).ShouldNot().HaveDependencyOnAny("ReapSeek.Domain", "ReapSeek.Modules").GetResult(), "Shared");

    [Fact]
    public void Module_code_knows_neither_the_API_nor_the_worker() =>
        Pass(Types.InAssembly(ModulesAssembly).ShouldNot().HaveDependencyOnAny("ReapSeek.Api", "ReapSeek.Worker", "Microsoft.AspNetCore").GetResult(), "Modules");

    [Fact]
    public void The_worker_does_not_depend_on_the_API() =>
        Pass(Types.InAssembly(Worker).ShouldNot().HaveDependencyOn("ReapSeek.Api").GetResult(), "Worker");

    [Fact]
    public void Endpoints_hold_no_SQL() =>
        Pass(Types.InAssembly(Api).That().HaveNameEndingWith("Endpoints").ShouldNot().HaveDependencyOnAny("Dapper", "Npgsql").GetResult(), "Endpoints");

    static readonly string[] SqlOutsideRepositories =
    [
        "ReapSeek.Api.Health.DatabaseProbe",
        "ReapSeek.Api.Seed.CatalogSeeder",
        "ReapSeek.Api.Catalog.Search.CategoryCache",
        "ReapSeek.Api.Ordering.Orders.OrderQueries",
        "ReapSeek.Api.Identity.AccountRecoveryService",
        "ReapSeek.Api.Catalog.Media.ProductImageService",
        "ReapSeek.Api.Ordering.Returns.ReturnService",
        "ReapSeek.Api.Ordering.Shipping.ShippingService",
    ];

    static TypeReference Outermost(TypeReference t)
    {
        while (t.DeclaringType is not null) t = t.DeclaringType;
        return t;
    }

    static bool InNamespace(TypeReference? t, string[] prefixes) =>
        t is not null && prefixes.Any(p => Outermost(t.GetElementType()).Namespace.StartsWith(p, StringComparison.Ordinal));

    static bool Uses(TypeDefinition type, string[] prefixes) =>
        type.Fields.Any(f => InNamespace(f.FieldType, prefixes))
        || type.Methods.Any(m => m.Parameters.Any(p => InNamespace(p.ParameterType, prefixes))
            || (m.HasBody && m.Body.Instructions.Any(i => i.Operand switch
            {
                MemberReference { DeclaringType: { } d } member when InNamespace(d, prefixes) => true,
                MethodReference { ReturnType: var r } when InNamespace(r, prefixes) => true,
                TypeReference r => InNamespace(r, prefixes),
                _ => false,
            })));

    static Type[] DataAccessTypes(params string[] prefixes)
    {
        using var module = ModuleDefinition.ReadModule(Api.Location);
        return module.GetTypes()
            .Where(t => Uses(t, prefixes))
            .Select(t => Outermost(t).FullName)
            .Where(n => n.StartsWith("ReapSeek.Api.", StringComparison.Ordinal))
            .Distinct().Select(n => Api.GetType(n, throwOnError: true)!).ToArray();
    }

    [Fact]
    public void Data_access_types_are_used_only_inside_their_module()
    {
        var dataAccess = DataAccessTypes("Dapper", "Npgsql");
        Assert.Contains(dataAccess, t => t.Name == "CheckoutService");
        var failures = ApiModules.SelectMany(m => ModuleViolations(m,
            dataAccess.Where(t => ModuleOf(t) == m).Select(t => t.FullName!).ToArray())).ToList();
        Assert.Empty(failures);
    }

    [Fact]
    public void Modules_use_each_other_only_through_their_public_API()
    {
        var failures = ApiModules.SelectMany(m => ModuleViolations(m, Api.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("ReapSeek.Api.", StringComparison.Ordinal) && ModuleOf(t) == m)
            .Where(t => !t.IsNested && !PublicApi[m].Contains(t.Name))
            .Select(t => t.FullName!).ToArray())).Distinct().Order().ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void The_public_API_lists_only_existing_types()
    {
        var names = Api.GetTypes().Where(t => !t.IsNested && t.Namespace is not null).GroupBy(ModuleOfOrEmpty).ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToHashSet());
        Assert.All(PublicApi, kv => Assert.All(kv.Value, name => Assert.Contains(name, names[kv.Key])));
    }

    static string ModuleOfOrEmpty(Type t) => t.Namespace!.StartsWith("ReapSeek.Api.", StringComparison.Ordinal) ? ModuleOf(t) : "";

    [Fact]
    public void SQL_lives_in_repositories()
    {
        var offenders = DataAccessTypes("Dapper")
            .Where(t => !t.Name.EndsWith("Repository", StringComparison.Ordinal) && !SqlOutsideRepositories.Contains(t.FullName))
            .Select(t => t.FullName!).Order().ToList();
        Assert.True(offenders.Count == 0, $"SQL outside repositories: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_SQL_whitelist_has_no_stale_entries()
    {
        var actual = DataAccessTypes("Dapper").Select(t => t.FullName!).ToHashSet();
        Assert.All(SqlOutsideRepositories, name => Assert.Contains(name, actual));
    }

    [Fact]
    public void Only_the_composition_root_knows_the_Sandbox_adapter()
    {
        Pass(Types.InAssembly(Api).That().DoNotResideInNamespace("ReapSeek.Api.Infrastructure")
            .ShouldNot().HaveDependencyOn(typeof(SandboxProvider).FullName!).GetResult(), "API");
        Pass(Types.InAssembly(Worker).That().DoNotResideInNamespace("ReapSeek.Worker.Infrastructure")
            .ShouldNot().HaveDependencyOn(typeof(SandboxProvider).FullName!).GetResult(), "Worker");
    }

    [Fact]
    public void Every_API_area_is_a_module_or_platform_code() =>
        Assert.Subset(ApiModules.Concat(PlatformAreas).ToHashSet(), TopLevelAreas.ToHashSet());
}
