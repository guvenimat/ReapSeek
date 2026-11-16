using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using ReapSeek.Catalog.Application;

namespace ReapSeek.Catalog.Infrastructure;

internal sealed class UploadStore(IConfiguration cfg)
{
    public string Root { get; } = cfg["Media:UploadPath"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");

    public async Task<string> SaveAsync(byte[] data, ImageInfo info, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var name = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}.{info.Extension}";
        await File.WriteAllBytesAsync(Path.Combine(Root, name), data, ct);
        return ImageRules.UploadPrefix + name;
    }

    public void Delete(string url)
    {
        if (!url.StartsWith(ImageRules.UploadPrefix, StringComparison.Ordinal)) return;
        var name = url[ImageRules.UploadPrefix.Length..];
        if (ImageRules.IsUploadName(name)) File.Delete(Path.Combine(Root, name));
    }

    public string? PathOf(string name) => ImageRules.IsUploadName(name) && File.Exists(Path.Combine(Root, name)) ? Path.Combine(Root, name) : null;
}
