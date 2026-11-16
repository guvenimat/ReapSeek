namespace ReapSeek.Catalog.Contracts;

public sealed record ArtColor(string Slug, string Name, string Hex);

public static class CatalogArt
{
    public static readonly IReadOnlyList<ArtColor> Colors =
    [
        new("black", "Black", "#2b2d33"),
        new("white", "White", "#f4f3ef"),
        new("grey", "Grey", "#9aa0a6"),
        new("navy", "Navy", "#1f3a68"),
        new("blue", "Blue", "#2f6fdb"),
        new("red", "Red", "#d6453d"),
        new("green", "Green", "#2f8f5b"),
        new("beige", "Beige", "#d9c3a0"),
        new("pink", "Pink", "#e78fb3"),
        new("brown", "Brown", "#8b5a3c"),
        new("silver", "Silver", "#c0c4cc"),
        new("gold", "Gold", "#d4af37"),
        new("purple", "Purple", "#7e57c2"),
        new("orange", "Orange", "#f08a24"),
        new("yellow", "Yellow", "#f2c230"),
    ];

    public static ArtColor? FindColor(string slug) => Colors.FirstOrDefault(c => c.Slug == slug);

    public static ArtColor FindColorByName(string? name) =>
        Colors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) ?? ColorFor(name?.Length ?? 0);

    public static ArtColor ColorFor(long seed) => Colors[(int)(Math.Abs(seed) % Colors.Count)];

    public static string Url(string kind, string colorSlug, long seed) => $"/media/catalog/{kind}/{colorSlug}/{seed}.svg";
}
