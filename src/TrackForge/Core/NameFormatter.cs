using System.Text;
using System.Text.RegularExpressions;

namespace TrackForge.Core;

/// <summary>Title Case rules and filename building, matching the F:\Music convention.</summary>
public static partial class NameFormatter
{
    // Deliberately small: the library capitalises "Of" and "On", so only true
    // connectors stay lowercase mid-title.
    private static readonly HashSet<string> LowerWords = new(StringComparer.OrdinalIgnoreCase)
        { "a", "an", "the", "and", "or", "nor", "but", "vs", "feat", "ft" };

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1f]")]
    private static partial Regex IllegalChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^A-Za-z]")]
    private static partial Regex NonLetters();

    public static string TitleCase(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? "";
        var words = Whitespace().Replace(input.Replace('_', ' ').Trim(), " ").Split(' ');
        var sb = new StringBuilder();

        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');

            var bare = NonLetters().Replace(w, "");
            // Leave acronyms (B.Y.O.B., ADD) and deliberate inner caps (DDevil, iTunes) alone.
            bool acronym = bare.Length > 0 && bare.ToUpperInvariant() == bare;
            bool innerCaps = bare.Length > 1 && bare[1..] != bare[1..].ToLowerInvariant();
            if (acronym || innerCaps) { sb.Append(w); continue; }

            var lower = w.ToLowerInvariant();
            bool middle = i > 0 && i < words.Length - 1;
            if (middle && LowerWords.Contains(lower.Trim('.', ',', '(', ')')))
                sb.Append(lower);
            else
                sb.Append(char.ToUpperInvariant(lower[0])).Append(lower[1..]);
        }
        return sb.ToString();
    }

    public static string SafeFileName(string? s)
    {
        var cleaned = IllegalChars().Replace(s ?? "", "").Trim().TrimEnd('.');
        cleaned = Whitespace().Replace(cleaned, " ").Trim();
        return cleaned.Length == 0 ? "Untitled" : cleaned;
    }

    /// <summary>
    /// Build a filename from a pattern. Tokens: {track} {tracknum} {title}
    /// {artist} {albumartist} {album} {year}.
    /// </summary>
    public static string BuildFileName(Track t, string pattern, string extension)
    {
        var rawTrack = (t.TrackNumber ?? "").Split('/')[0].Trim();
        bool numeric = int.TryParse(rawTrack, out int trackNo) && trackNo > 0;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{track}"] = numeric ? trackNo.ToString("00") : "",
            ["{tracknum}"] = numeric ? trackNo.ToString() : "",
            ["{title}"] = TitleCase(t.Title),
            ["{artist}"] = TitleCase(t.Artist),
            ["{albumartist}"] = TitleCase(string.IsNullOrWhiteSpace(t.AlbumArtist) ? t.Artist : t.AlbumArtist),
            ["{album}"] = TitleCase(t.Album),
            ["{year}"] = t.Year ?? "",
        };

        var name = pattern;
        foreach (var (token, value) in map)
            name = Regex.Replace(name, Regex.Escape(token), value.Replace("$", "$$"),
                                 RegexOptions.IgnoreCase);

        name = Whitespace().Replace(name, " ").Trim(' ', '-', '_');
        if (name.Length == 0) name = TitleCase(t.Title);
        if (!extension.StartsWith('.')) extension = "." + extension;
        return SafeFileName(name) + extension;
    }

    /// <summary>Adds " (2)", " (3)"... until the path is free.</summary>
    public static string UniquePath(string desired)
    {
        if (!File.Exists(desired)) return desired;
        var dir = Path.GetDirectoryName(desired) ?? "";
        var stem = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        for (int n = 2; n < 1000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }
}
