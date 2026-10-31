using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace ReapSeek.Shared.Analytics;

public sealed record AudienceCohort(long Id, string Name, string Description, string Status, long? MemberCount, DateTime? CalculatedAt);
public sealed record AudienceCohortsResponse(List<AudienceCohort> Cohorts);
public sealed record AudiencePerson(string Id, string[] DistinctIds);
public sealed record AudiencePersonsResponse(List<AudiencePerson> Persons, string? NextCursor);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AudienceCohortsResponse))]
[JsonSerializable(typeof(AudiencePersonsResponse))]
public sealed partial class AudienceJsonContext : JsonSerializerContext;

public sealed class AudienceUnavailableException(string message) : Exception(message);

public sealed class ReapDeepAudience : IDisposable
{
    public const int MaxMembers = 100_000;
    const int PageSize = 200;
    const string CustomerPrefix = "customer-";

    readonly HttpClient? _http;

    public ReapDeepAudience(IConfiguration cfg)
    {
        var url = cfg["ReapDeep:ApiUrl"];
        var key = cfg["ReapDeep:ReadKey"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public bool IsConfigured => _http is not null;

    public static long? CustomerId(string distinctId) =>
        distinctId.StartsWith(CustomerPrefix, StringComparison.Ordinal)
        && long.TryParse(distinctId.AsSpan(CustomerPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    public static List<long> CustomerIds(IEnumerable<AudiencePerson> persons) =>
        persons.Select(p => p.DistinctIds.Select(CustomerId).FirstOrDefault(id => id is not null)).OfType<long>().Distinct().ToList();

    public async Task<List<AudienceCohort>> CohortsAsync(CancellationToken ct) =>
        (await GetAsync("api/v1/cohorts", AudienceJsonContext.Default.AudienceCohortsResponse, ct)).Cohorts;

    public async Task<List<long>> CustomersInCohortAsync(long cohortId, CancellationToken ct)
    {
        var persons = new List<AudiencePerson>();
        string? cursor = null;
        do
        {
            var page = await GetAsync($"api/v1/cohorts/{cohortId}/persons?limit={PageSize}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"),
                AudienceJsonContext.Default.AudiencePersonsResponse, ct);
            persons.AddRange(page.Persons);
            cursor = page.NextCursor;
        }
        while (cursor is not null && persons.Count < MaxMembers);
        return CustomerIds(persons);
    }

    async Task<T> GetAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken ct)
    {
        if (_http is null) throw new AudienceUnavailableException("ReapDeep is not connected");
        try
        {
            using var res = await _http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode)
                throw new AudienceUnavailableException($"ReapDeep returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
            return await res.Content.ReadFromJsonAsync(type, ct) ?? throw new AudienceUnavailableException("ReapDeep returned an empty response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new AudienceUnavailableException($"ReapDeep is unreachable: {ex.Message}");
        }
    }

    public void Dispose() => _http?.Dispose();
}
