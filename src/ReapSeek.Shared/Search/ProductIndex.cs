using System.Text.Json.Serialization;

namespace ReapSeek.Shared.Search;

public sealed record ProductDocument(
    long Id, string Slug, string Title, string Description, string Brand, string BrandSlug,
    string Department, string DepartmentName, string CategoryPath, List<string> CategoryPaths, string CategoryName,
    List<string> Colors, List<string> Sizes, List<string> Options,
    decimal? Price, decimal? ListPrice, int Discount, bool InStock, int SellerCount, string? ImageUrl, DateTime CreatedAt,
    Dictionary<string, string> ColorImages, Dictionary<string, string> Attributes, double? Rating = null, int ReviewCount = 0);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ProductDocument))]
public sealed partial class SearchJsonContext : JsonSerializerContext;

public static class ProductIndex
{
    public const string Alias = "products";
    public const string IndexPrefix = "products_";
    public const int Version = 3;

    public static string NewIndexName(DateTime now) => IndexPrefix + now.ToString("yyyyMMddHHmmssfff");

    public static readonly string[] Synonyms =
    [
        "tee, t-shirt, tshirt",
        "sneakers, trainers",
        "laptop, notebook",
        "phone, smartphone, mobile",
        "sofa, couch",
        "headphones, headset",
        "jeans, denim",
        "pants, trousers",
        "bag, handbag, purse",
        "perfume, fragrance, eau de parfum",
        "sweatshirt, hoodie",
    ];

    public static string Definition => $$"""
        {
          "settings": {
            "number_of_shards": 1,
            "number_of_replicas": 0,
            "analysis": {
              "filter": {
                "english_stemmer": { "type": "stemmer", "language": "light_english" },
                "english_possessive": { "type": "stemmer", "language": "possessive_english" },
                "product_synonyms": { "type": "synonym_graph", "synonyms": [{{string.Join(", ", Synonyms.Select(s => $"\"{s}\""))}}] }
              },
              "analyzer": {
                "product_text": { "tokenizer": "standard", "filter": ["lowercase", "asciifolding", "english_possessive", "english_stemmer"] },
                "product_synonyms": { "tokenizer": "standard", "filter": ["lowercase", "asciifolding", "english_possessive", "product_synonyms", "english_stemmer"] },
                "folded": { "tokenizer": "standard", "filter": ["lowercase", "asciifolding"] }
              }
            }
          },
          "mappings": {
            "_meta": { "version": {{Version}} },
            "dynamic": "strict",
            "properties": {
              "id": { "type": "long" },
              "slug": { "type": "keyword", "index": false },
              "title": {
                "type": "text", "analyzer": "product_text", "copy_to": "all_text",
                "fields": { "suggest": { "type": "search_as_you_type", "analyzer": "folded" }, "raw": { "type": "keyword" } }
              },
              "description": { "type": "text", "analyzer": "product_text" },
              "brand": { "type": "keyword", "copy_to": "all_text", "fields": { "text": { "type": "text", "analyzer": "folded" } } },
              "brand_slug": { "type": "keyword" },
              "department": { "type": "keyword" },
              "department_name": { "type": "keyword", "copy_to": "all_text" },
              "category_path": { "type": "keyword" },
              "category_paths": { "type": "keyword" },
              "category_name": { "type": "text", "analyzer": "product_text", "copy_to": "all_text", "fields": { "raw": { "type": "keyword" } } },
              "colors": { "type": "keyword", "copy_to": "all_text" },
              "sizes": { "type": "keyword" },
              "options": { "type": "keyword" },
              "all_text": { "type": "text", "analyzer": "product_text" },
              "price": { "type": "scaled_float", "scaling_factor": 100 },
              "list_price": { "type": "scaled_float", "scaling_factor": 100 },
              "discount": { "type": "integer" },
              "in_stock": { "type": "boolean" },
              "seller_count": { "type": "integer" },
              "image_url": { "type": "keyword", "index": false },
              "created_at": { "type": "date" },
              "rating": { "type": "float" },
              "review_count": { "type": "integer" },
              "color_images": { "type": "object", "enabled": false },
              "attributes": { "type": "object", "enabled": false }
            }
          }
        }
        """;
}
