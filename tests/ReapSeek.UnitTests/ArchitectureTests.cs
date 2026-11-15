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
    static readonly Assembly Api = typeof(ReapSeek.Api.Infrastructure.HostingExtensions).Assembly;
    static readonly Assembly Web = typeof(ReapSeek.BuildingBlocks.Web.ApiResults).Assembly;
    static readonly Assembly Worker = typeof(ReapSeek.Worker.Outbox.OutboxDispatcher).Assembly;
    static readonly Assembly[] ContractProjects =
    [
        typeof(ReapSeek.Engagement.Contracts.ReviewStat).Assembly,
        typeof(ReapSeek.Marketing.Contracts.CustomerCoupon).Assembly,
        typeof(ReapSeek.Finance.Contracts.ISellerFinance).Assembly,
        typeof(ReapSeek.Identity.Contracts.ISellerAccounts).Assembly,
    ];

    static readonly Dictionary<Assembly, Type> ModuleProjects = new()
    {
        [typeof(ReapSeek.Engagement.EngagementModule).Assembly] = typeof(ReapSeek.Engagement.EngagementModule),
        [typeof(ReapSeek.Marketing.MarketingModule).Assembly] = typeof(ReapSeek.Marketing.MarketingModule),
        [typeof(ReapSeek.Finance.FinanceModule).Assembly] = typeof(ReapSeek.Finance.FinanceModule),
        [typeof(ReapSeek.Identity.IdentityModule).Assembly] = typeof(ReapSeek.Identity.IdentityModule),
    };

    static readonly string[] ApiModules = ["Identity", "Catalog", "Sellers", "Ordering", "Finance", "Engagement", "Marketing"];
    static readonly string[] PlatformAreas = ["Infrastructure", "Seed", "Health", "Operations"];

    static readonly Dictionary<string, string[]> PublicApi = new()
    {
        ["Identity"] = [],
        ["Catalog"] = ["CatalogService", "CatalogRules", "SearchService", "ProductStatus",
            "ProductFilter", "ProductsResponse", "ProductDetail", "CreateProductRequest", "UpdateProductRequest", "OfferRequest", "CategoriesResponse", "BrandsResponse", "VariantRow", "OfferRow", "ProductImage", "VariantProductRecord",
            "SellerCatalog", "SellerOfferRow", "ProposalRow", "BuyBoxRecord"],
        ["Sellers"] = [],
        ["Ordering"] = ["CartService", "SellerPackages", "SellerPackageRow", "SellerPackagesResponse", "PackageAddress", "PackageLineRow", "PackageFilter"],
        ["Finance"] = [],
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
    public void Endpoints_hold_no_SQL()
    {
        Pass(Types.InAssembly(Api).That().HaveNameEndingWith("Endpoints").ShouldNot().HaveDependencyOnAny("Dapper", "Npgsql").GetResult(), "Endpoints");
        foreach (var module in ModuleProjects.Keys)
            Pass(Types.InAssembly(module).That().HaveNameEndingWith("Endpoints").ShouldNot().HaveDependencyOnAny("Dapper", "Npgsql").GetResult(), module.GetName().Name!);
    }

    [Fact]
    public void Module_projects_expose_only_their_entry_class()
    {
        foreach (var (module, entry) in ModuleProjects)
            Assert.Equal([entry], module.GetExportedTypes());
    }

    [Fact]
    public void Module_projects_know_neither_the_API_nor_the_worker()
    {
        foreach (var module in ModuleProjects.Keys)
            Pass(Types.InAssembly(module).ShouldNot().HaveDependencyOnAny("ReapSeek.Api", "ReapSeek.Worker").GetResult(), module.GetName().Name!);
    }

    [Fact]
    public void Module_projects_reference_no_other_module_project()
    {
        var names = ModuleProjects.Keys.Select(m => m.GetName().Name!).ToHashSet();
        foreach (var module in ModuleProjects.Keys)
            Assert.DoesNotContain(module.GetReferencedAssemblies(), r => names.Contains(r.Name!));
    }

    [Fact]
    public void Module_contracts_depend_on_nothing_but_the_base_library()
    {
        foreach (var contracts in ContractProjects)
        {
            Pass(Types.InAssembly(contracts).ShouldNot().HaveDependencyOnAny(
                "Dapper", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions", "ReapSeek.Shared", "ReapSeek.Domain", "ReapSeek.Modules",
                "ReapSeek.BuildingBlocks", "ReapSeek.Api", "ReapSeek.Worker").GetResult(), contracts.GetName().Name!);
            Assert.DoesNotContain(contracts.GetReferencedAssemblies(), r => r.Name!.StartsWith("ReapSeek.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Web_building_blocks_know_no_module() =>
        Pass(Types.InAssembly(Web).ShouldNot().HaveDependencyOnAny(
            "ReapSeek.Api", "ReapSeek.Worker", "ReapSeek.Modules", "ReapSeek.Domain", "ReapSeek.Engagement", "ReapSeek.Marketing", "ReapSeek.Finance", "ReapSeek.Identity").GetResult(), "BuildingBlocks.Web");

    static readonly string[] SqlOutsideRepositories =
    [
        "ReapSeek.Api.Health.DatabaseProbe",
        "ReapSeek.Api.Seed.CatalogSeeder",
        "ReapSeek.Api.Catalog.Search.CategoryCache",
        "ReapSeek.Api.Ordering.Orders.OrderQueries",
        "ReapSeek.Identity.Application.AccountRecoveryService",
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

    static Type[] DataAccessTypes(params string[] prefixes) => DataAccessTypesIn(Api, "ReapSeek.Api.", prefixes);

    static Type[] DataAccessTypesIn(Assembly assembly, string rootNamespace, params string[] prefixes)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        return module.GetTypes()
            .Where(t => Uses(t, prefixes))
            .Select(t => Outermost(t).FullName)
            .Where(n => n.StartsWith(rootNamespace, StringComparison.Ordinal))
            .Distinct().Select(n => assembly.GetType(n, throwOnError: true)!).ToArray();
    }

    static string RootNamespace(Type entry) => entry.Namespace! + ".";

    [Fact]
    public void Data_access_types_are_used_only_inside_their_module()
    {
        var dataAccess = DataAccessTypes("Dapper", "Npgsql");
        Assert.Contains(dataAccess, t => t.Name == "CheckoutService");
        var failures = ApiModules.SelectMany(m => ModuleViolations(m,
            dataAccess.Where(t => ModuleOf(t) == m && !PublicApi[m].Contains(t.Name)).Select(t => t.FullName!).ToArray())).ToList();
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
            .Concat(ModuleProjects.SelectMany(m => DataAccessTypesIn(m.Key, RootNamespace(m.Value), "Dapper")))
            .Where(t => !t.Name.EndsWith("Repository", StringComparison.Ordinal) && !SqlOutsideRepositories.Contains(t.FullName))
            .Select(t => t.FullName!).Order().ToList();
        Assert.True(offenders.Count == 0, $"SQL outside repositories: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_SQL_rules_see_module_projects()
    {
        var engagement = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Engagement.EngagementModule));
        Assert.Contains(DataAccessTypesIn(engagement.Key, RootNamespace(engagement.Value), "Dapper"), t => t.Name == "ReviewRepository");
        var marketing = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Marketing.MarketingModule));
        Assert.Contains(DataAccessTypesIn(marketing.Key, RootNamespace(marketing.Value), "Dapper"), t => t.Name == "CampaignRepository");
        Assert.Contains(DataAccessTypesIn(marketing.Key, RootNamespace(marketing.Value), "Dapper"), t => t.Name == "AlertRepository");
        var finance = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Finance.FinanceModule));
        Assert.Contains(DataAccessTypesIn(finance.Key, RootNamespace(finance.Value), "Dapper"), t => t.Name == "SellerFinanceRepository");
        var identity = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Identity.IdentityModule));
        Assert.Contains(DataAccessTypesIn(identity.Key, RootNamespace(identity.Value), "Dapper"), t => t.Name == "AccountRepository");
        Assert.Contains(DataAccessTypesIn(identity.Key, RootNamespace(identity.Value), "Dapper"), t => t.Name == "AccountRecoveryService");
    }

    [Fact]
    public void The_SQL_whitelist_has_no_stale_entries()
    {
        var actual = DataAccessTypes("Dapper")
            .Concat(ModuleProjects.SelectMany(m => DataAccessTypesIn(m.Key, RootNamespace(m.Value), "Dapper")))
            .Select(t => t.FullName!).ToHashSet();
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
