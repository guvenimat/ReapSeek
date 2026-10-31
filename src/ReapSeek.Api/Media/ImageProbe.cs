using System.Buffers.Binary;

namespace ReapSeek.Api.Media;

public sealed record ImageInfo(string Format, string Extension, string ContentType, int Width, int Height);

public static class ImageProbe
{
    public static readonly ImageInfo Jpeg = new("jpeg", "jpg", "image/jpeg", 0, 0);
    public static readonly ImageInfo Png = new("png", "png", "image/png", 0, 0);
    public static readonly ImageInfo Webp = new("webp", "webp", "image/webp", 0, 0);

    public static ImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) && data[12..16].SequenceEqual("IHDR"u8))
            return Png with { Width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]), Height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[20..]) };
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return JpegSize(data) is var (w, h) ? Jpeg with { Width = w, Height = h } : null;
        if (data.Length >= 30 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return WebpSize(data) is var (w, h) ? Webp with { Width = w, Height = h } : null;
        return null;
    }

    static (int, int)? JpegSize(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF) return null;
            var marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
            {
                i += 2;
                continue;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
            if (length < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
            i += 2 + length;
        }
        return null;
    }

    static (int, int)? WebpSize(ReadOnlySpan<byte> data)
    {
        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
            return (1 + ReadUInt24(data[24..]), 1 + ReadUInt24(data[27..]));
        if (chunk.SequenceEqual("VP8 "u8) && data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A)
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
        if (chunk.SequenceEqual("VP8L"u8) && data[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        return null;
    }

    static int ReadUInt24(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16;
}
