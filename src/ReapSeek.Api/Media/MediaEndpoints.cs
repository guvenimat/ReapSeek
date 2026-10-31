namespace ReapSeek.Api.Media;

public static class MediaEndpoints
{
    public static void MapMedia(this WebApplication app)
    {
        app.MapGet("/media/catalog/{kind}/{color}/{seed}.svg", (string kind, string color, long seed, HttpContext ctx) =>
        {
            if (!ProductArt.IsKind(kind) || ProductArt.FindColor(color) is not { } c) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "public, max-age=604800, immutable";
            return Results.Text(ProductArt.Render(kind, c, seed), "image/svg+xml");
        });
        app.MapGet("/media/uploads/{name}", (string name, UploadStore store, HttpContext ctx) =>
        {
            if (store.PathOf(name) is not { } path) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(path, ImageRules.ContentType(name));
        });
    }
}
