using ReapSeek.Shared;

namespace ReapSeek.Catalog.Application;

internal static class ImageRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxUploadsPerProduct = 8;
    public const int MinSide = 200;
    public const int MaxSide = 8000;
    public const string UploadPrefix = "/media/uploads/";

    public static ImageInfo Validate(ReadOnlySpan<byte> data)
    {
        Ensure.That(data.Length > 0, "The file is empty");
        Ensure.That(data.Length <= MaxBytes, "Images can be at most 5 MB");
        var info = ImageProbe.Inspect(data);
        Ensure.That(info is not null, "Only JPEG, PNG and WebP images are accepted");
        Ensure.That(info.Width >= MinSide && info.Height >= MinSide, $"Images must be at least {MinSide} x {MinSide} pixels");
        Ensure.That(info.Width <= MaxSide && info.Height <= MaxSide, $"Images can be at most {MaxSide} x {MaxSide} pixels");
        return info;
    }

    public static bool IsUploadName(string name) =>
        name.Length is > 33 and < 40 && name[..32].All(char.IsAsciiHexDigitLower) && name[32] == '.' && name[33..] is "jpg" or "png" or "webp";

    public static string ContentType(string name) => name[33..] switch
    {
        "jpg" => "image/jpeg",
        "png" => "image/png",
        _ => "image/webp",
    };
}
