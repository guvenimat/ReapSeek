namespace ReapSeek.Api.Media;

public static class MediaEndpoints
{
    public static void MapMedia(this WebApplication app) =>
        app.MapGet("/media/catalog/{kind}/{color}/{seed}.svg", (string kind, string color, long seed, HttpContext ctx) =>
        {
            if (!ProductArt.IsKind(kind) || ProductArt.FindColor(color) is not { } c) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "public, max-age=604800, immutable";
            return Results.Text(ProductArt.Render(kind, c, seed), "image/svg+xml");
        });
}
