using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Mono.Cecil;
using NetArchTest.Rules;
using ReapSeek.Domain.Common;
using ReapSeek.Shared.Providers;

namespace ReapSeek.UnitTests;

public partial class ArchitectureTests
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
        typeof(ReapSeek.Catalog.Contracts.ISellerCatalog).Assembly,
        typeof(ReapSeek.Ordering.Contracts.ISellerPackages).Assembly,
    ];

    static readonly Dictionary<Assembly, Type> ModuleProjects = new()
    {
        [typeof(ReapSeek.Engagement.EngagementModule).Assembly] = typeof(ReapSeek.Engagement.EngagementModule),
        [typeof(ReapSeek.Marketing.MarketingModule).Assembly] = typeof(ReapSeek.Marketing.MarketingModule),
        [typeof(ReapSeek.Finance.FinanceModule).Assembly] = typeof(ReapSeek.Finance.FinanceModule),
        [typeof(ReapSeek.Identity.IdentityModule).Assembly] = typeof(ReapSeek.Identity.IdentityModule),
        [typeof(ReapSeek.Catalog.CatalogModule).Assembly] = typeof(ReapSeek.Catalog.CatalogModule),
        [typeof(ReapSeek.Ordering.OrderingModule).Assembly] = typeof(ReapSeek.Ordering.OrderingModule),
        [typeof(ReapSeek.Sellers.SellersModule).Assembly] = typeof(ReapSeek.Sellers.SellersModule),
    };

    static readonly string[] PlatformAreas = ["Infrastructure", "Seed", "Health", "Operations"];
    const string CompositionRoot = "Infrastructure";

    static readonly string[] ModuleProjectDependencies =
        ["ReapSeek.Shared", "ReapSeek.Modules", "ReapSeek.Domain", "ReapSeek.BuildingBlocks", "ReapSeek.BuildingBlocks.Web"];

    static IEnumerable<string> TopLevelAreas => Api.GetTypes()
        .Select(t => t.Namespace).OfType<string>()
        .Where(n => n.StartsWith("ReapSeek.Api.", StringComparison.Ordinal))
        .Select(n => n["ReapSeek.Api.".Length..].Split('.')[0])
        .Distinct();

    static string AreaOf(Type t) => t.Namespace!["ReapSeek.Api.".Length..].Split('.')[0];

    static List<string> AreaViolations(string area, string[] targets)
    {
        if (targets.Length == 0) return [];
        var failures = new List<string>();
        foreach (var ns in PlatformAreas.Where(a => a != area && a != CompositionRoot).Select(a => $"ReapSeek.Api.{a}"))
        {
            var r = Types.InAssembly(Api).That().ResideInNamespaceStartingWith(ns + ".").Or().ResideInNamespace(ns)
                .ShouldNot().HaveDependencyOnAny(targets).GetResult();
            if (!r.IsSuccessful) failures.AddRange(r.FailingTypeNames.Select(t => $"{t} -> {area}"));
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
            "ReapSeek.Api", "ReapSeek.Worker", "ReapSeek.Modules", "ReapSeek.Domain", "ReapSeek.Engagement", "ReapSeek.Marketing", "ReapSeek.Finance", "ReapSeek.Identity", "ReapSeek.Catalog", "ReapSeek.Ordering", "ReapSeek.Sellers").GetResult(), "BuildingBlocks.Web");

    static readonly Assembly BuildingBlocks = typeof(ReapSeek.BuildingBlocks.Messaging.ModuleOutbox).Assembly;

    [Fact]
    public void Building_blocks_know_no_module()
    {
        Pass(Types.InAssembly(BuildingBlocks).ShouldNot().HaveDependencyOnAny(
            "ReapSeek.Api", "ReapSeek.Worker", "ReapSeek.Modules", "ReapSeek.Domain", "ReapSeek.Engagement", "ReapSeek.Marketing", "ReapSeek.Finance",
            "ReapSeek.Identity", "ReapSeek.Catalog", "ReapSeek.Ordering", "ReapSeek.Sellers", "ReapSeek.BuildingBlocks.Web", "Microsoft.AspNetCore").GetResult(), "BuildingBlocks");
        Assert.Equal(["ReapSeek.Shared"], BuildingBlocks.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("ReapSeek.", StringComparison.Ordinal)));
        Assert.DoesNotContain(Shared.GetReferencedAssemblies(), r => r.Name!.StartsWith("ReapSeek.BuildingBlocks", StringComparison.Ordinal));
    }

    internal static IEnumerable<(Type Type, string Name, JsonSerializerContext? Context)> IntegrationEvents() =>
        ContractProjects.SelectMany(assembly =>
        {
            var contexts = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(JsonSerializerContext)))
                .Select(t => (JsonSerializerContext)t.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!).ToList();
            return assembly.GetTypes()
                .Where(t => t.GetField("EventName", BindingFlags.Public | BindingFlags.Static) is not null || IntegrationEventName().IsMatch(t.Name))
                .Select(t => (t, t.GetField("EventName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string ?? "",
                    contexts.FirstOrDefault(c => c.GetTypeInfo(t) is not null)));
        });

    [System.Text.RegularExpressions.GeneratedRegex("V[0-9]+$")]
    private static partial System.Text.RegularExpressions.Regex IntegrationEventName();

    [Fact]
    public void Integration_events_are_public_versioned_records_serializable_through_their_contracts_context()
    {
        var events = IntegrationEvents().ToList();
        Assert.Contains(events, e => e.Type == typeof(ReapSeek.Engagement.Contracts.QuestionAskedV1));
        Type[] converted =
        [
            typeof(ReapSeek.Engagement.Contracts.QuestionAnsweredV1), typeof(ReapSeek.Engagement.Contracts.PriceDroppedV1),
            typeof(ReapSeek.Marketing.Contracts.CampaignSendV1), typeof(ReapSeek.Marketing.Contracts.CouponIssuedV1), typeof(ReapSeek.Marketing.Contracts.AlertReceivedV1),
            typeof(ReapSeek.Identity.Contracts.UserRegisteredV1), typeof(ReapSeek.Identity.Contracts.AccountEmailV1),
        ];
        Assert.All(converted, t => Assert.Contains(events, e => e.Type == t));
        foreach (var (type, name, context) in events)
        {
            Assert.True(type.IsPublic && type.IsSealed && type.GetMethod("<Clone>$") is not null, $"{type.Name} must be a public sealed record");
            Assert.Matches(IntegrationEventName(), type.Name);
            var version = IntegrationEventName().Match(type.Name).Value[1..];
            var module = type.Namespace!.Split('.')[1].ToLowerInvariant();
            Assert.Equal($"{module}.", name[..(module.Length + 1)]);
            Assert.EndsWith($".v{version}", name);
            Assert.NotNull(context);
            Assert.Equal(JsonNamingPolicy.SnakeCaseLower, context!.Options.PropertyNamingPolicy);
            var instance = RuntimeHelpers.GetUninitializedObject(type);
            var json = JsonSerializer.Serialize(instance, context.GetTypeInfo(type)!);
            Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize(json, context.GetTypeInfo(type)!), context.GetTypeInfo(type)!));
        }
    }

    [Fact]
    public void Integration_event_wire_names_are_unique()
    {
        var names = IntegrationEvents().Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    static readonly string[] SqlOutsideRepositories =
    [
        "ReapSeek.Api.Health.DatabaseProbe",
        "ReapSeek.Api.Seed.CatalogSeeder",
        "ReapSeek.Catalog.Infrastructure.CategoryCache",
        "ReapSeek.Ordering.Application.OrderQueries",
        "ReapSeek.Identity.Application.AccountRecoveryService",
        "ReapSeek.Catalog.Application.ProductImageService",
        "ReapSeek.Ordering.Application.ReturnService",
        "ReapSeek.Ordering.Application.ShippingService",
        "ReapSeek.Catalog.Jobs.ProductCacheInvalidator",
        "ReapSeek.Engagement.Jobs.PriceDropJob",
        "ReapSeek.Marketing.Jobs.CampaignHandler",
        "ReapSeek.Ordering.Jobs.CartReminderJob",
        "ReapSeek.Ordering.Jobs.PaymentExpiryJob",
        "ReapSeek.Ordering.Jobs.ShippingDeadlineJob",
        "ReapSeek.Sellers.Jobs.SellerRatingJob",
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
        foreach (var (module, entry) in ModuleProjects)
        {
            var types = DataAccessTypesIn(module, RootNamespace(entry), "Dapper", "Npgsql");
            Assert.NotEmpty(types);
            Assert.All(types, t => Assert.False(t.IsVisible, $"{t.FullName} is visible outside {module.GetName().Name}"));
        }
        var sellers = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Sellers.SellersModule));
        Assert.Contains(DataAccessTypesIn(sellers.Key, RootNamespace(sellers.Value), "Dapper", "Npgsql"), t => t.Name == "SellerPanelRepository");

        var dataAccess = DataAccessTypes("Dapper", "Npgsql");
        Assert.Contains(dataAccess, t => t.Name == "OutboxAdminRepository");
        var failures = PlatformAreas.Where(a => a != CompositionRoot).SelectMany(a => AreaViolations(a,
            dataAccess.Where(t => AreaOf(t) == a).Select(t => t.FullName!).ToArray())).ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Modules_use_each_other_only_through_their_contracts()
    {
        var allowed = ContractProjects.Select(c => c.GetName().Name!).Concat(ModuleProjectDependencies).ToHashSet();
        foreach (var module in ModuleProjects.Keys)
        {
            var references = module.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("ReapSeek.", StringComparison.Ordinal)).ToList();
            Assert.All(references, r => Assert.True(allowed.Contains(r), $"{module.GetName().Name} references {r}"));
        }
        var sellers = typeof(ReapSeek.Sellers.SellersModule).Assembly.GetReferencedAssemblies().Select(r => r.Name!).ToHashSet();
        Assert.Superset(new HashSet<string> { "ReapSeek.Catalog.Contracts", "ReapSeek.Ordering.Contracts", "ReapSeek.Finance.Contracts", "ReapSeek.Identity.Contracts" }, sellers);
    }

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
        var catalog = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Catalog.CatalogModule));
        Assert.Contains(DataAccessTypesIn(catalog.Key, RootNamespace(catalog.Value), "Dapper"), t => t.Name == "CatalogRepository");
        Assert.Contains(DataAccessTypesIn(catalog.Key, RootNamespace(catalog.Value), "Dapper"), t => t.Name == "CategoryCache");
        var ordering = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Ordering.OrderingModule));
        Assert.Contains(DataAccessTypesIn(ordering.Key, RootNamespace(ordering.Value), "Dapper"), t => t.Name == "CartRepository");
        Assert.Contains(DataAccessTypesIn(ordering.Key, RootNamespace(ordering.Value), "Dapper"), t => t.Name == "OrderQueries");
        var sellers = ModuleProjects.Single(m => m.Value == typeof(ReapSeek.Sellers.SellersModule));
        Assert.Contains(DataAccessTypesIn(sellers.Key, RootNamespace(sellers.Value), "Dapper"), t => t.Name == "SellerRepository");
        Assert.Contains(DataAccessTypesIn(sellers.Key, RootNamespace(sellers.Value), "Dapper"), t => t.Name == "StoreRepository");
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
    public void Module_jobs_live_in_their_module_projects()
    {
        string[] platform = ["ReapSeek.Worker.Outbox", "ReapSeek.Worker.Platform"];
        var workerJobs = Worker.GetTypes().Where(t => t.IsSubclassOf(typeof(BackgroundService))).ToList();
        Assert.Contains(workerJobs, t => t.Name == "OutboxProcessor");
        Assert.All(workerJobs, t => Assert.True(platform.Any(p => t.Namespace!.StartsWith(p + ".", StringComparison.Ordinal) || t.Namespace == p),
            $"{t.FullName} is a module job in the worker"));
        var moduleJobs = ModuleProjects.Keys.SelectMany(m => m.GetTypes()).Where(t => t.IsSubclassOf(typeof(BackgroundService))).Select(t => t.Name).ToHashSet();
        Assert.Superset(new HashSet<string> { "SearchBootstrap", "PriceDropJob", "PayoutJob", "CartReminderJob", "PaymentExpiryJob", "ShippingDeadlineJob", "SellerRatingJob" }, moduleJobs);
    }

    [Fact]
    public void The_API_holds_only_platform_code()
    {
        Assert.Equal(PlatformAreas.Order(), TopLevelAreas.Order());
        var outside = Api.GetTypes().Where(t => t.Namespace is { } n && n.StartsWith("ReapSeek.", StringComparison.Ordinal)
                && !PlatformAreas.Any(a => n == $"ReapSeek.Api.{a}" || n.StartsWith($"ReapSeek.Api.{a}.", StringComparison.Ordinal)))
            .Select(t => t.FullName!).ToList();
        Assert.True(outside.Count == 0, $"Non-platform code in the API: {string.Join(", ", outside)}");
    }
}
