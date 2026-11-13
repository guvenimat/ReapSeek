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
    static readonly Assembly Api = typeof(ReapSeek.Api.Infrastructure.ApiResults).Assembly;
    static readonly Assembly Worker = typeof(ReapSeek.Worker.Outbox.OutboxDispatcher).Assembly;

    static readonly string[][] ModuleGroups =
    [
        ["Carts", "Checkout", "Payments", "Orders", "Returns", "Shipping"],
        ["Catalog", "Products", "Search", "Media"],
    ];

    static IEnumerable<string> Modules => Api.GetTypes()
        .Select(t => t.Namespace).OfType<string>()
        .Where(n => n.StartsWith("ReapSeek.Api.", StringComparison.Ordinal))
        .Select(n => n["ReapSeek.Api.".Length..].Split('.')[0])
        .Distinct().Where(m => m is not "Infrastructure" and not "Seed");

    static string[] GroupOf(string module) => ModuleGroups.FirstOrDefault(g => g.Contains(module)) ?? [module];

    static void Pass(TestResult result, string rule) =>
        Assert.True(result.IsSuccessful, $"{rule}: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void The_domain_depends_on_nothing_but_the_base_library() =>
        Pass(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "Dapper", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions", "StackExchange.Redis", "System.Net.Http", "System.Text.Json",
            "ReapSeek.Shared", "ReapSeek.Api", "ReapSeek.Worker").GetResult(), "Domain");

    [Fact]
    public void Shared_code_knows_neither_the_API_nor_the_worker() =>
        Pass(Types.InAssembly(Shared).ShouldNot().HaveDependencyOnAny("ReapSeek.Api", "ReapSeek.Worker", "Microsoft.AspNetCore").GetResult(), "Shared");

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
        "ReapSeek.Api.Search.CategoryCache",
        "ReapSeek.Api.Orders.OrderQueries",
        "ReapSeek.Api.Identity.AccountRecoveryService",
        "ReapSeek.Api.Media.ProductImageService",
        "ReapSeek.Api.Returns.ReturnService",
        "ReapSeek.Api.Shipping.ShippingService",
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
    public void Data_access_types_are_used_only_inside_their_module_group()
    {
        var failures = new List<string>();
        var dataAccess = DataAccessTypes("Dapper", "Npgsql");
        Assert.Contains(dataAccess, t => t.Name == "CheckoutService");
        foreach (var module in Modules)
        {
            var owned = dataAccess.Where(t => t.Namespace == $"ReapSeek.Api.{module}").Select(t => t.FullName!).ToArray();
            if (owned.Length == 0) continue;
            var allowed = GroupOf(module).Select(m => $"ReapSeek.Api.{m}").Append("ReapSeek.Api.Infrastructure").Append("ReapSeek.Api.Seed").ToArray();
            var result = Types.InAssembly(Api).That().ResideInNamespaceStartingWith("ReapSeek.Api")
                .And().DoNotResideInNamespace(allowed[0]);
            foreach (var ns in allowed.Skip(1)) result = result.And().DoNotResideInNamespace(ns);
            var r = result.ShouldNot().HaveDependencyOnAny(owned).GetResult();
            if (!r.IsSuccessful) failures.AddRange(r.FailingTypeNames.Select(t => $"{t} -> {module}"));
        }
        Assert.Empty(failures);
    }

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
    public void Modules_exist_where_the_rules_expect_them() =>
        Assert.Subset(Modules.ToHashSet(), ModuleGroups.SelectMany(g => g).ToHashSet());
}
