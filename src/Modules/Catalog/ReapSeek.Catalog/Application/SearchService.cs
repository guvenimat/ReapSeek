using Microsoft.AspNetCore.Http;
using ReapSeek.Catalog.Api;
using ReapSeek.Catalog.Contracts;
using ReapSeek.Catalog.Infrastructure;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Search;

namespace ReapSeek.Catalog.Application;

internal sealed class SearchService(OpenSearchClient search, CategoryCache categories) : IProductCards
{
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var (_, names) = await categories.GetAsync(ct);
        var result = await search.PostAsync($"{ProductIndex.Alias}/_search", SearchQuery.Build(request), ct)
            ?? throw new OpenSearchException(502, "Empty search response");
        return SearchQuery.ParseResults(result, request, names);
    }

    public async Task<SuggestResponse> SuggestAsync(string q, CancellationToken ct)
    {
        var (_, names) = await categories.GetAsync(ct);
        var result = await search.PostAsync($"{ProductIndex.Alias}/_search", SearchQuery.BuildSuggest(q), ct)
            ?? throw new OpenSearchException(502, "Empty search response");
        return SearchQuery.ParseSuggest(result, q, names);
    }

    public Task<List<ProductCard>> InStockCardsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) => CardsAsync(ids, true, ct);

    public async Task<List<ProductCard>> CardsAsync(IReadOnlyCollection<long> ids, bool inStockOnly, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var result = await search.PostAsync($"{ProductIndex.Alias}/_search", SearchQuery.BuildByIds(ids, inStockOnly), ct)
            ?? throw new OpenSearchException(502, "Empty search response");
        var byId = SearchQuery.ParseCards(result).ToDictionary(c => c.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<List<ProductCard>> FallbackCardsAsync(string? categoryPath, IReadOnlyCollection<long> exclude, int size, CancellationToken ct)
    {
        var result = await search.PostAsync($"{ProductIndex.Alias}/_search", SearchQuery.BuildFallback(categoryPath, exclude, size), ct)
            ?? throw new OpenSearchException(502, "Empty search response");
        return SearchQuery.ParseCards(result);
    }

    public async Task<ProductCategory?> CategoryOfAsync(long productId, CancellationToken ct)
    {
        try
        {
            var source = (await search.GetAsync($"{ProductIndex.Alias}/_doc/{productId}", ct))?["_source"];
            return source?["category_path"] is { } path ? new ProductCategory(path.GetValue<string>(), source["category_name"]?.GetValue<string>() ?? "") : null;
        }
        catch (OpenSearchException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }
}
