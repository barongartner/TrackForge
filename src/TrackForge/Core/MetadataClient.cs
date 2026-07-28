using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrackForge.Core;

/// <summary>
/// Metadata and cover art lookup. No API keys, no accounts.
///
/// iTunes Search    - best album / year / genre / track number, 1000px+ artwork
/// Deezer           - solid fallback, carries ISRC and BPM
/// MusicBrainz      - canonical release data, artwork via Cover Art Archive
/// </summary>
public sealed partial class MetadataClient
{
    private const string UserAgent = "TrackForge/1.0 (https://github.com/barongartner/TrackForge)";

    private readonly HttpClient _http;
    private readonly Dictionary<string, byte[]> _artCache = new();
    private DateTime _lastMusicBrainzCall = DateTime.MinValue;
    private readonly SemaphoreSlim _mbGate = new(1, 1);

    public string Country { get; set; } = "CA";

    public MetadataClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    // ------------------------------------------------------------ cleaning

    [GeneratedRegex(@"\((?:official|lyric|audio|video|music|hd|4k|visuali[sz]er|full)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ParenNoise();

    [GeneratedRegex(@"\[(?:official|lyric|audio|video|music|hd|4k|visuali[sz]er|full)[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketNoise();

    [GeneratedRegex(@"\b(official (music )?video|official audio|lyric video|lyrics|audio only|hq|hd|4k|free download)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PhraseNoise();

    [GeneratedRegex(@"\s*-\s*Topic$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffix();

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlnum();

    /// <summary>Strips the YouTube furniture that wrecks a search.</summary>
    public static (string artist, string title) Clean(string? artist, string? title)
    {
        var t = title ?? "";
        t = ParenNoise().Replace(t, "");
        t = BracketNoise().Replace(t, "");
        t = PhraseNoise().Replace(t, "");
        t = Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '–', '—', '|');

        var a = TopicSuffix().Replace(artist ?? "", "").Trim();
        return (a, t);
    }

    /// <summary>"Artist - Title (Official Video)" -> ("Artist", "Title"). Best effort.</summary>
    public static (string artist, string title) SplitVideoTitle(string? raw)
    {
        raw ??= "";
        foreach (var sep in new[] { " - ", " – ", " — ", " -- ", ": " })
        {
            int i = raw.IndexOf(sep, StringComparison.Ordinal);
            if (i <= 0) continue;
            var left = raw[..i].Trim();
            var right = raw[(i + sep.Length)..].Trim();
            if (left.Length is > 1 and < 60) return Clean(left, right);
        }
        return Clean("", raw);
    }

    // ------------------------------------------------------------- sources

    private static string Q(string s) => Uri.EscapeDataString(s ?? "");

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonDocument.Parse(text);
        }
        catch { return null; }
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Num(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetRawText() : "";

    public async Task<List<MatchCandidate>> ITunesAsync(
        string artist, string title, int limit = 8, CancellationToken ct = default)
    {
        var (a, t) = Clean(artist, title);
        var term = $"{a} {t}".Trim();
        var results = new List<MatchCandidate>();
        if (term.Length == 0) return results;

        var doc = await GetJsonAsync(
            $"https://itunes.apple.com/search?term={Q(term)}&entity=song&limit={limit}&country={Country}",
            ct).ConfigureAwait(false);
        if (doc is null) return results;

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("results", out var arr)) return results;
            foreach (var r in arr.EnumerateArray())
            {
                var art = Str(r, "artworkUrl100");
                results.Add(new MatchCandidate
                {
                    Source = "iTunes",
                    Title = Str(r, "trackName"),
                    Artist = Str(r, "artistName"),
                    Album = Str(r, "collectionName"),
                    AlbumArtist = Str(r, "collectionArtistName") is { Length: > 0 } ca ? ca : Str(r, "artistName"),
                    Year = Take4(Str(r, "releaseDate")),
                    Genre = Str(r, "primaryGenreName"),
                    TrackNumber = Num(r, "trackNumber"),
                    TrackCount = Num(r, "trackCount"),
                    DiscNumber = Num(r, "discNumber"),
                    DurationSeconds = int.TryParse(Num(r, "trackTimeMillis"), out var ms) ? ms / 1000 : 0,
                    ArtUrl = Regex.Replace(art, @"/\d+x\d+bb\.jpg$", "/1000x1000bb.jpg"),
                    ArtThumbUrl = art,
                });
            }
        }
        return results;
    }

    public async Task<List<MatchCandidate>> DeezerAsync(
        string artist, string title, int limit = 6, CancellationToken ct = default)
    {
        var (a, t) = Clean(artist, title);
        var term = $"{a} {t}".Trim();
        var results = new List<MatchCandidate>();
        if (term.Length == 0) return results;

        var doc = await GetJsonAsync($"https://api.deezer.com/search?q={Q(term)}&limit={limit}", ct)
            .ConfigureAwait(false);
        if (doc is null) return results;

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var arr)) return results;
            foreach (var r in arr.EnumerateArray())
            {
                var album = r.TryGetProperty("album", out var al) ? al : default;
                var performer = r.TryGetProperty("artist", out var ar) ? Str(ar, "name") : "";
                results.Add(new MatchCandidate
                {
                    Source = "Deezer",
                    Title = Str(r, "title"),
                    Artist = performer,
                    AlbumArtist = performer,
                    Album = album.ValueKind == JsonValueKind.Object ? Str(album, "title") : "",
                    Year = Take4(Str(r, "release_date")),
                    TrackNumber = Num(r, "track_position"),
                    DiscNumber = Num(r, "disk_number"),
                    Isrc = Str(r, "isrc"),
                    Bpm = double.TryParse(Num(r, "bpm"), out var b) && b > 0
                        ? Math.Round(b).ToString() : "",
                    DurationSeconds = int.TryParse(Num(r, "duration"), out var d) ? d : 0,
                    ArtUrl = album.ValueKind == JsonValueKind.Object
                        ? (Str(album, "cover_xl") is { Length: > 0 } xl ? xl : Str(album, "cover_big")) : "",
                    ArtThumbUrl = album.ValueKind == JsonValueKind.Object ? Str(album, "cover_small") : "",
                    AlbumId = album.ValueKind == JsonValueKind.Object ? Num(album, "id") : null,
                });
            }
        }
        return results;
    }

    /// <summary>Deezer's track search omits genre and label; the album endpoint has them.</summary>
    public async Task EnrichFromDeezerAlbumAsync(MatchCandidate c, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(c.AlbumId)) return;
        var doc = await GetJsonAsync($"https://api.deezer.com/album/{c.AlbumId}", ct).ConfigureAwait(false);
        if (doc is null) return;

        using (doc)
        {
            var root = doc.RootElement;
            if (string.IsNullOrWhiteSpace(c.Publisher)) c.Publisher = Str(root, "label");
            if (string.IsNullOrWhiteSpace(c.Year)) c.Year = Take4(Str(root, "release_date"));
            if (string.IsNullOrWhiteSpace(c.TrackCount)) c.TrackCount = Num(root, "nb_tracks");
            if (string.IsNullOrWhiteSpace(c.Genre)
                && root.TryGetProperty("genres", out var genres)
                && genres.TryGetProperty("data", out var gdata)
                && gdata.ValueKind == JsonValueKind.Array
                && gdata.GetArrayLength() > 0)
            {
                c.Genre = Str(gdata[0], "name");
            }
        }
    }

    public async Task<List<MatchCandidate>> MusicBrainzAsync(
        string artist, string title, int limit = 5, CancellationToken ct = default)
    {
        var (a, t) = Clean(artist, title);
        var results = new List<MatchCandidate>();
        if (t.Length == 0) return results;

        // MusicBrainz asks for no more than one request per second.
        await _mbGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var since = DateTime.UtcNow - _lastMusicBrainzCall;
            if (since < TimeSpan.FromSeconds(1.1))
                await Task.Delay(TimeSpan.FromSeconds(1.1) - since, ct).ConfigureAwait(false);
            _lastMusicBrainzCall = DateTime.UtcNow;
        }
        finally { _mbGate.Release(); }

        var query = $"recording:\"{t}\"" + (a.Length > 0 ? $" AND artist:\"{a}\"" : "");
        var doc = await GetJsonAsync(
            $"https://musicbrainz.org/ws/2/recording/?query={Q(query)}&fmt=json&limit={limit}", ct)
            .ConfigureAwait(false);
        if (doc is null) return results;

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("recordings", out var arr)) return results;
            foreach (var r in arr.EnumerateArray())
            {
                var release = r.TryGetProperty("releases", out var rels)
                              && rels.ValueKind == JsonValueKind.Array && rels.GetArrayLength() > 0
                    ? rels[0] : default;
                var releaseId = release.ValueKind == JsonValueKind.Object ? Str(release, "id") : "";

                var credit = "";
                if (r.TryGetProperty("artist-credit", out var ac) && ac.ValueKind == JsonValueKind.Array)
                    foreach (var c in ac.EnumerateArray())
                        credit += Str(c, "name") + Str(c, "joinphrase");

                results.Add(new MatchCandidate
                {
                    Source = "MusicBrainz",
                    Title = Str(r, "title"),
                    Artist = credit,
                    AlbumArtist = credit,
                    Album = release.ValueKind == JsonValueKind.Object ? Str(release, "title") : "",
                    Year = Take4(release.ValueKind == JsonValueKind.Object && Str(release, "date").Length > 0
                        ? Str(release, "date") : Str(r, "first-release-date")),
                    Isrc = r.TryGetProperty("isrcs", out var isrcs)
                           && isrcs.ValueKind == JsonValueKind.Array && isrcs.GetArrayLength() > 0
                        ? isrcs[0].GetString() ?? "" : "",
                    DurationSeconds = int.TryParse(Num(r, "length"), out var len) ? len / 1000 : 0,
                    ArtUrl = releaseId.Length > 0
                        ? $"https://coverartarchive.org/release/{releaseId}/front-1200" : "",
                    ArtThumbUrl = releaseId.Length > 0
                        ? $"https://coverartarchive.org/release/{releaseId}/front-250" : "",
                });
            }
        }
        return results;
    }

    // ------------------------------------------------------------ combined

    /// <summary>Every source, merged, deduped and scored. Best match first.</summary>
    public async Task<List<MatchCandidate>> LookupAsync(
        string artist, string title, double durationSeconds = 0,
        bool deep = false, CancellationToken ct = default)
    {
        var all = new List<MatchCandidate>();

        var itunes = ITunesAsync(artist, title, ct: ct);
        var deezer = DeezerAsync(artist, title, ct: ct);
        await Task.WhenAll(itunes, deezer).ConfigureAwait(false);
        all.AddRange(itunes.Result);
        all.AddRange(deezer.Result);

        if (deep || all.Count < 3)
            all.AddRange(await MusicBrainzAsync(artist, title, ct: ct).ConfigureAwait(false));

        var seen = new HashSet<string>();
        var unique = new List<MatchCandidate>();
        foreach (var c in all)
        {
            var key = $"{Norm(c.Title)}|{Norm(c.Album)}|{c.Source}";
            if (!seen.Add(key)) continue;
            c.Score = Math.Round(ScoreMatch(c, artist, title, durationSeconds), 1);
            unique.Add(c);
        }

        unique.Sort((x, y) => y.Score.CompareTo(x.Score));

        // Fill in genre/label for the best few Deezer hits, which the search omits.
        foreach (var c in unique.Take(4).Where(c => c.Source == "Deezer" && c.Genre.Length == 0))
            await EnrichFromDeezerAlbumAsync(c, ct).ConfigureAwait(false);

        return unique;
    }

    private static string Norm(string? s) => NonAlnum().Replace((s ?? "").ToLowerInvariant(), "");

    private static string Take4(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Length >= 4 ? s[..4] : s;

    private static readonly string[] CompilationWords =
        { "greatest hits", "best of", "compilation", "now that", "karaoke", "tribute", "made popular" };

    /// <summary>How well does a candidate match what we actually asked for?</summary>
    private static double ScoreMatch(MatchCandidate c, string artist, string title, double duration)
    {
        string ct = Norm(c.Title), ca = Norm(c.Artist);
        string wt = Norm(title), wa = Norm(artist);
        double score = 0;

        if (wt.Length > 0 && ct.Length > 0)
        {
            if (ct == wt) score += 50;
            else if (ct.Contains(wt) || wt.Contains(ct)) score += 32;
            else score += 10.0 * wt.Distinct().Count(ch => ct.Contains(ch)) / Math.Max(wt.Distinct().Count(), 1);
        }

        if (wa.Length > 0 && ca.Length > 0)
        {
            if (ca == wa) score += 30;
            else if (ca.Contains(wa) || wa.Contains(ca)) score += 20;
        }

        if (duration > 0 && c.DurationSeconds > 0)
        {
            var diff = Math.Abs(duration - c.DurationSeconds);
            score += diff switch { <= 2 => 20, <= 5 => 12, <= 12 => 4, _ => -15 };
        }

        if (c.ArtUrl.Length > 0) score += 5;
        if (c.Year.Length > 0) score += 3;
        if (c.Source == "iTunes") score += 4;

        var album = (c.Album ?? "").ToLowerInvariant();
        if (CompilationWords.Any(album.Contains)) score -= 12;
        if ((c.Artist ?? "").Contains("karaoke", StringComparison.OrdinalIgnoreCase)) score -= 40;

        return score;
    }

    // ----------------------------------------------------------- cover art

    public sealed record ArtOption(string Url, string ThumbUrl, string Source, string Label);

    public async Task<List<ArtOption>> FindArtAsync(
        string artist, string album, int limit = 8, CancellationToken ct = default)
    {
        var options = new List<ArtOption>();
        var seen = new HashSet<string>();
        var term = $"{artist} {album}".Trim();
        if (term.Length == 0) return options;

        var itunes = await GetJsonAsync(
            $"https://itunes.apple.com/search?term={Q(term)}&entity=album&limit={limit}&country={Country}",
            ct).ConfigureAwait(false);
        if (itunes is not null)
            using (itunes)
                if (itunes.RootElement.TryGetProperty("results", out var arr))
                    foreach (var r in arr.EnumerateArray())
                    {
                        var thumb = Str(r, "artworkUrl100");
                        var full = Regex.Replace(thumb, @"/\d+x\d+bb\.jpg$", "/1500x1500bb.jpg");
                        if (full.Length > 0 && seen.Add(full))
                            options.Add(new ArtOption(full, thumb, "iTunes",
                                $"{Str(r, "collectionName")} ({Take4(Str(r, "releaseDate"))})"));
                    }

        var deezer = await GetJsonAsync(
            $"https://api.deezer.com/search/album?q={Q(term)}&limit={limit}", ct).ConfigureAwait(false);
        if (deezer is not null)
            using (deezer)
                if (deezer.RootElement.TryGetProperty("data", out var arr))
                    foreach (var r in arr.EnumerateArray())
                    {
                        var full = Str(r, "cover_xl");
                        if (full.Length > 0 && seen.Add(full))
                            options.Add(new ArtOption(full, Str(r, "cover_small"), "Deezer", Str(r, "title")));
                    }

        return options;
    }

    public async Task<byte[]?> DownloadArtAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        lock (_artCache)
            if (_artCache.TryGetValue(url, out var cached)) return cached;

        try
        {
            var bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (bytes.Length < 512) return null;
            lock (_artCache)
                if (_artCache.Count < 300) _artCache[url] = bytes;
            return bytes;
        }
        catch { return null; }
    }
}
