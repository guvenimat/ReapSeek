using System.Text;

namespace ReapSeek.Shared;

public static class Slugs
{
    public const int MaxLength = 80;

    public static string Slugify(string text)
    {
        var sb = new StringBuilder(text.Length);
        var dash = false;
        foreach (var raw in text.Trim().ToLowerInvariant())
        {
            var ch = raw switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
                _ => raw,
            };
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
            if (sb.Length >= MaxLength) break;
        }
        return sb.ToString().Trim('-');
    }
}
