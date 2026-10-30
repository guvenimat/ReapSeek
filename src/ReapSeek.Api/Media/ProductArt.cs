using System.Globalization;
using System.Text;

namespace ReapSeek.Api.Media;

public sealed record ArtColor(string Slug, string Name, string Hex);

public static class ProductArt
{
    public const int Width = 400;
    public const int Height = 500;
    const string Ink = "#1d2130";

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

    static readonly Dictionary<string, Func<string, string, string>> Shapes = new(StringComparer.Ordinal)
    {
        ["dress"] = (c, d) =>
            $"""<path d="M168 112 L182 92 M232 112 L218 92" stroke="{d}" stroke-width="6" stroke-linecap="round"/><path d="M160 112 L240 112 L252 168 L232 192 L298 418 Q200 440 102 418 L168 192 L148 168 Z" fill="{c}" stroke="{d}" stroke-width="3"/><path d="M168 192 L232 192" stroke="{d}" stroke-width="5" opacity=".55"/>""",
        ["shirt"] = (c, d) =>
            $"""<path d="M142 128 L182 110 Q200 134 218 110 L258 128 L312 178 L280 214 L258 196 L258 404 L142 404 L142 196 L120 214 L88 178 Z" fill="{c}" stroke="{d}" stroke-width="3" stroke-linejoin="round"/><path d="M182 110 Q200 134 218 110" fill="none" stroke="{d}" stroke-width="4"/>""",
        ["pants"] = (c, d) =>
            $"""<path d="M138 104 L262 104 L274 410 L216 410 L200 196 L184 410 L126 410 Z" fill="{c}" stroke="{d}" stroke-width="3" stroke-linejoin="round"/><rect x="138" y="104" width="124" height="22" fill="{d}" opacity=".35"/><path d="M200 126 L200 170" stroke="{d}" stroke-width="3" opacity=".5"/>""",
        ["shoe"] = (c, d) =>
            $"""<path d="M84 318 Q92 262 146 254 L196 248 Q226 236 248 272 Q300 292 330 304 Q348 318 334 334 L92 334 Q80 328 84 318 Z" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="84" y="330" width="254" height="18" rx="8" fill="#f4f3ef" stroke="{d}" stroke-width="2"/><path d="M200 262 L214 286 M216 256 L230 280 M232 262 L244 282" stroke="{d}" stroke-width="3" opacity=".6"/>""",
        ["bag"] = (c, d) =>
            $"""<path d="M150 200 Q150 120 200 120 Q250 120 250 200" fill="none" stroke="{d}" stroke-width="10" stroke-linecap="round"/><rect x="108" y="190" width="184" height="210" rx="22" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="180" y="236" width="40" height="18" rx="4" fill="{d}" opacity=".45"/>""",
        ["watch"] = (c, d) =>
            $"""<rect x="170" y="90" width="60" height="320" rx="18" fill="{c}" stroke="{d}" stroke-width="3"/><circle cx="200" cy="250" r="72" fill="{d}"/><circle cx="200" cy="250" r="58" fill="#f4f3ef"/><path d="M200 250 L200 212 M200 250 L226 262" stroke="{Ink}" stroke-width="5" stroke-linecap="round"/>""",
        ["phone"] = (c, d) =>
            $"""<rect x="128" y="96" width="144" height="300" rx="26" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="140" y="112" width="120" height="262" rx="16" fill="{Ink}"/><rect x="180" y="122" width="40" height="10" rx="5" fill="{c}" opacity=".7"/><path d="M152 300 Q200 240 248 292" stroke="{c}" stroke-width="10" fill="none" opacity=".45"/>""",
        ["laptop"] = (c, d) =>
            $"""<rect x="108" y="148" width="184" height="128" rx="10" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="120" y="160" width="160" height="104" rx="4" fill="{Ink}"/><path d="M80 290 L320 290 L304 318 L96 318 Z" fill="{c}" stroke="{d}" stroke-width="3" stroke-linejoin="round"/>""",
        ["headphones"] = (c, d) =>
            $"""<path d="M118 280 Q118 140 200 140 Q282 140 282 280" fill="none" stroke="{d}" stroke-width="16" stroke-linecap="round"/><rect x="96" y="250" width="56" height="96" rx="24" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="248" y="250" width="56" height="96" rx="24" fill="{c}" stroke="{d}" stroke-width="3"/>""",
        ["sofa"] = (c, d) =>
            $"""<rect x="96" y="196" width="208" height="96" rx="20" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="72" y="236" width="56" height="112" rx="18" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="272" y="236" width="56" height="112" rx="18" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="116" y="272" width="168" height="66" rx="12" fill="{c}" stroke="{d}" stroke-width="3"/><path d="M110 348 L104 372 M290 348 L296 372" stroke="{d}" stroke-width="8" stroke-linecap="round"/>""",
        ["lamp"] = (c, d) =>
            $"""<path d="M150 120 L250 120 L282 226 L118 226 Z" fill="{c}" stroke="{d}" stroke-width="3" stroke-linejoin="round"/><rect x="194" y="226" width="12" height="150" fill="{d}"/><ellipse cx="200" cy="384" rx="66" ry="16" fill="{d}"/>""",
        ["mug"] = (c, d) =>
            $"""<path d="M262 206 Q318 206 318 262 Q318 318 262 318" fill="none" stroke="{d}" stroke-width="14"/><rect x="118" y="180" width="152" height="186" rx="18" fill="{c}" stroke="{d}" stroke-width="3"/><path d="M160 150 Q150 130 162 112 M196 150 Q186 130 198 112 M232 150 Q222 130 234 112" stroke="{d}" stroke-width="4" fill="none" opacity=".4" stroke-linecap="round"/>""",
        ["bottle"] = (c, d) =>
            $"""<rect x="176" y="96" width="48" height="48" rx="8" fill="{d}"/><rect x="186" y="140" width="28" height="30" fill="{d}" opacity=".7"/><rect x="140" y="166" width="120" height="236" rx="30" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="158" y="236" width="84" height="80" rx="8" fill="#ffffff" opacity=".75"/><path d="M174 262 L226 262 M174 282 L212 282" stroke="{Ink}" stroke-width="5" opacity=".35" stroke-linecap="round"/>""",
        ["dumbbell"] = (c, d) =>
            $"""<rect x="108" y="244" width="184" height="20" rx="8" fill="#9aa0a6" stroke="{d}" stroke-width="2"/><rect x="84" y="196" width="44" height="116" rx="12" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="272" y="196" width="44" height="116" rx="12" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="60" y="214" width="30" height="80" rx="10" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="310" y="214" width="30" height="80" rx="10" fill="{c}" stroke="{d}" stroke-width="3"/>""",
        ["book"] = (c, d) =>
            $"""<rect x="132" y="104" width="160" height="296" rx="6" fill="#f4f3ef" stroke="{d}" stroke-width="3"/><rect x="120" y="96" width="156" height="296" rx="6" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="120" y="96" width="22" height="296" fill="{d}" opacity=".35"/><rect x="164" y="160" width="88" height="10" rx="5" fill="#ffffff" opacity=".8"/><rect x="164" y="182" width="64" height="8" rx="4" fill="#ffffff" opacity=".6"/>""",
        ["toy"] = (c, d) =>
            $"""<rect x="108" y="270" width="92" height="92" rx="10" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="200" y="270" width="92" height="92" rx="10" fill="#f2c230" stroke="{d}" stroke-width="3"/><rect x="154" y="178" width="92" height="92" rx="10" fill="#2f6fdb" stroke="{d}" stroke-width="3"/><circle cx="154" cy="316" r="16" fill="#ffffff" opacity=".7"/><circle cx="246" cy="316" r="16" fill="#ffffff" opacity=".7"/><circle cx="200" cy="224" r="16" fill="#ffffff" opacity=".7"/>""",
        ["jar"] = (c, d) =>
            $"""<rect x="138" y="132" width="124" height="34" rx="8" fill="{d}"/><path d="M128 176 Q128 162 144 162 L256 162 Q272 162 272 176 L272 388 Q272 404 256 404 L144 404 Q128 404 128 388 Z" fill="{c}" stroke="{d}" stroke-width="3"/><rect x="146" y="236" width="108" height="92" rx="10" fill="#ffffff" opacity=".78"/><circle cx="200" cy="282" r="22" fill="{d}" opacity=".45"/>""",
        ["box"] = (c, d) =>
            $"""<path d="M200 128 L304 182 L200 236 L96 182 Z" fill="{c}" stroke="{d}" stroke-width="3" stroke-linejoin="round"/><path d="M96 182 L200 236 L200 372 L96 318 Z" fill="{c}" stroke="{d}" stroke-width="3" stroke-linejoin="round"/><path d="M304 182 L200 236 L200 372 L304 318 Z" fill="{d}" opacity=".5" stroke="{d}" stroke-width="3" stroke-linejoin="round"/>""",
    };

    public static IReadOnlyCollection<string> Kinds => Shapes.Keys;

    public static bool IsKind(string kind) => Shapes.ContainsKey(kind);

    public static ArtColor? FindColor(string slug) => Colors.FirstOrDefault(c => c.Slug == slug);

    public static ArtColor FindColorByName(string? name) =>
        Colors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) ?? ColorFor(name?.Length ?? 0);

    public static ArtColor ColorFor(long seed) => Colors[(int)(Math.Abs(seed) % Colors.Count)];

    public static string Url(string kind, string colorSlug, long seed) => $"/media/catalog/{kind}/{colorSlug}/{seed}.svg";

    public static string Render(string kind, ArtColor color, long seed)
    {
        var shape = Shapes[kind];
        var hue = (int)(Math.Abs(seed * 47) % 360);
        var tilt = (int)(Math.Abs(seed * 13) % 9) - 4;
        var dark = Shade(color.Hex, color.Slug is "black" or "navy" ? 1.6 : 0.62);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(2048);
        sb.Append($"""<svg xmlns="http://www.w3.org/2000/svg" width="{Width}" height="{Height}" viewBox="0 0 {Width} {Height}">""");
        sb.Append($"""<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl({hue},45%,96%)"/><stop offset="1" stop-color="hsl({(hue + 40) % 360},40%,89%)"/></linearGradient></defs>""");
        sb.Append($"""<rect width="{Width}" height="{Height}" fill="url(#bg)"/>""");
        sb.Append($"""<circle cx="{(seed % 2 == 0 ? 318 : 82)}" cy="96" r="54" fill="hsl({hue},60%,90%)"/>""");
        sb.Append($"""<ellipse cx="200" cy="432" rx="128" ry="16" fill="{Ink}" opacity=".08"/>""");
        sb.Append(string.Create(inv, $"""<g transform="rotate({tilt} 200 260)">"""));
        sb.Append(shape(color.Hex, dark));
        sb.Append("</g></svg>");
        return sb.ToString();
    }

    static string Shade(string hex, double factor)
    {
        var r = Convert.ToInt32(hex[1..3], 16);
        var g = Convert.ToInt32(hex[3..5], 16);
        var b = Convert.ToInt32(hex[5..7], 16);
        int Mix(int v) => factor < 1 ? (int)(v * factor) : (int)Math.Min(255, v + (255 - v) * (factor - 1));
        return $"#{Mix(r):x2}{Mix(g):x2}{Mix(b):x2}";
    }
}
