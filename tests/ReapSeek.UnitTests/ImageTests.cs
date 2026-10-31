using ReapSeek.Api.Media;
using ReapSeek.Shared;

namespace ReapSeek.UnitTests;

public class ImageTests
{
    static byte[] Png(int w, int h)
    {
        var b = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    static byte[] Jpeg(int w, int h) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
        0xFF, 0xC4, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1,
        0xFF, 0xD9,
    ];

    static byte[] Webp(string chunk, Action<byte[]> fill)
    {
        var b = new byte[40];
        "RIFF"u8.ToArray().CopyTo(b, 0);
        "WEBP"u8.ToArray().CopyTo(b, 8);
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(b, 12);
        fill(b);
        return b;
    }

    [Fact]
    public void Reads_png_size()
    {
        var info = ImageProbe.Inspect(Png(1200, 800));
        Assert.Equal(("png", "image/png", 1200, 800), (info!.Format, info.ContentType, info.Width, info.Height));
    }

    [Fact]
    public void Reads_jpeg_size_from_the_frame_header_after_other_segments()
    {
        var info = ImageProbe.Inspect(Jpeg(640, 480));
        Assert.Equal(("jpeg", "jpg", 640, 480), (info!.Format, info.Extension, info.Width, info.Height));
    }

    [Fact]
    public void Reads_all_three_webp_flavours()
    {
        var vp8x = Webp("VP8X", b => { b[24] = 0xFF; b[25] = 0x03; b[27] = 0x57; b[28] = 0x02; });
        Assert.Equal((1024, 600), (ImageProbe.Inspect(vp8x)!.Width, ImageProbe.Inspect(vp8x)!.Height));
        var lossy = Webp("VP8 ", b => { b[23] = 0x9D; b[24] = 0x01; b[25] = 0x2A; b[26] = 0x20; b[27] = 0x03; b[28] = 0x58; b[29] = 0x02; });
        Assert.Equal((800, 600), (ImageProbe.Inspect(lossy)!.Width, ImageProbe.Inspect(lossy)!.Height));
        var lossless = Webp("VP8L", b =>
        {
            b[20] = 0x2F;
            var bits = (uint)(300 - 1) | (uint)(200 - 1) << 14;
            BitConverter.GetBytes(bits).CopyTo(b, 21);
        });
        Assert.Equal((300, 200), (ImageProbe.Inspect(lossless)!.Width, ImageProbe.Inspect(lossless)!.Height));
    }

    [Theory]
    [InlineData("GIF89a and more bytes here to be long enough")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("")]
    public void Rejects_other_formats(string content)
    {
        Assert.Null(ImageProbe.Inspect(System.Text.Encoding.ASCII.GetBytes(content)));
        Assert.Throws<ValidationException>(() => ImageRules.Validate(System.Text.Encoding.ASCII.GetBytes(content)));
    }

    [Fact]
    public void Rejects_a_jpeg_signature_without_a_frame_header() =>
        Assert.Null(ImageProbe.Inspect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xD9, 0, 0, 0]));

    [Theory]
    [InlineData(199, 800, false)]
    [InlineData(200, 200, true)]
    [InlineData(8000, 8000, true)]
    [InlineData(8001, 500, false)]
    public void Enforces_the_size_limits(int w, int h, bool ok)
    {
        if (ok) Assert.Equal("png", ImageRules.Validate(Png(w, h)).Format);
        else Assert.Throws<ValidationException>(() => ImageRules.Validate(Png(w, h)));
    }

    [Fact]
    public void Rejects_files_over_five_megabytes()
    {
        var big = new byte[ImageRules.MaxBytes + 1];
        Png(1000, 1000).CopyTo(big, 0);
        Assert.Throws<ValidationException>(() => ImageRules.Validate(big));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef.png", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    public void Only_generated_upload_names_are_served(string name, bool ok) => Assert.Equal(ok, ImageRules.IsUploadName(name));
}
