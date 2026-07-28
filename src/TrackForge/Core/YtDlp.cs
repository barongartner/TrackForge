using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrackForge.Core;

/// <summary>One entry returned by a probe or a search.</summary>
public sealed class VideoEntry
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string RawTitle { get; set; } = "";
    public string Uploader { get; set; } = "";
    public int DurationSeconds { get; set; }
    public long ViewCount { get; set; }
    public string ThumbnailUrl { get; set; } = "";

    // YouTube Music entries carry real tags; use them when they exist.
    public string YtTrack { get; set; } = "";
    public string YtArtist { get; set; } = "";
    public string YtAlbum { get; set; } = "";
    public string YtYear { get; set; } = "";

    public string DurationText => DurationSeconds <= 0
        ? ""
        : TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    /// <summary>Best guess at artist and title, before we look anything up.</summary>
    public (string artist, string title) Guess()
    {
        if (!string.IsNullOrWhiteSpace(YtArtist) && !string.IsNullOrWhiteSpace(YtTrack))
            return (YtArtist, YtTrack);

        var (a, t) = MetadataClient.SplitVideoTitle(RawTitle);
        if (string.IsNullOrWhiteSpace(a))
            a = Regex.Replace(Uploader, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(t)) t = RawTitle;
        return (a, t);
    }
}

/// <summary>Drives the yt-dlp and ffmpeg executables.</summary>
public sealed partial class YtDlp
{
    private readonly AppConfig _cfg;

    public YtDlp(AppConfig cfg) => _cfg = cfg;

    public string YtDlpPath => Resolve(_cfg.YtDlpPath, ToolInstaller.YtDlpExe, "yt-dlp");
    public string FfmpegPath => Resolve(_cfg.FfmpegPath, ToolInstaller.FfmpegExe, "ffmpeg");

    /// <summary>
    /// An explicit setting wins, then our own tools folder, then whatever is on PATH.
    /// The bundled copy taking priority over PATH means a stale system yt-dlp can't
    /// break downloads once we've installed a current one.
    /// </summary>
    private static string Resolve(string configured, string bundled, string onPath)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        if (File.Exists(bundled)) return bundled;
        return onPath;
    }

    [GeneratedRegex(@"\[download\]\s+([\d.]+)%")]
    private static partial Regex ProgressLine();

    // -------------------------------------------------------------- checks

    public async Task<(string? ytDlp, string? ffmpeg)> CheckToolsAsync(CancellationToken ct = default)
    {
        var yt = await FirstLineAsync(YtDlpPath, new[] { "--version" }, ct).ConfigureAwait(false);
        var ff = await FirstLineAsync(FfmpegPath, new[] { "-version" }, ct).ConfigureAwait(false);
        return (yt, ff);
    }

    private static async Task<string?> FirstLineAsync(string exe, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = NewPsi(exe, args);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var text = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0) return null;
            return text.Split('\n').FirstOrDefault()?.Trim();
        }
        catch { return null; }
    }

    private static ProcessStartInfo NewPsi(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    // --------------------------------------------------------------- probe

    /// <summary>Reads metadata for a link without downloading. Playlists expand.</summary>
    public async Task<(List<VideoEntry> entries, string? playlistTitle)> ProbeAsync(
        string url, CancellationToken ct = default)
    {
        var psi = NewPsi(YtDlpPath, new[]
            { "-J", "--no-warnings", "--flat-playlist", "--ignore-config", url });

        using var p = Process.Start(psi) ?? throw new InvalidOperationException(
            "Could not start yt-dlp. Install it with:  pip install -U yt-dlp");

        var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(LastError(stderr) ?? "yt-dlp could not read that link.");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var entries = new List<VideoEntry>();
        string? playlistTitle = null;

        if (Str(root, "_type") == "playlist" && root.TryGetProperty("entries", out var arr))
        {
            playlistTitle = Str(root, "title");
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object) entries.Add(ParseEntry(e, url));
        }
        else
        {
            entries.Add(ParseEntry(root, url));
        }

        return (entries, playlistTitle);
    }

    /// <summary>Finds a track on YouTube by name.</summary>
    public async Task<List<VideoEntry>> SearchAsync(string query, int limit = 5, CancellationToken ct = default)
    {
        var results = new List<VideoEntry>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        try
        {
            var psi = NewPsi(YtDlpPath, new[]
                { "-J", "--no-warnings", "--flat-playlist", "--ignore-config", $"ytsearch{limit}:{query}" });
            using var p = Process.Start(psi);
            if (p is null) return results;

            var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return results;

            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("entries", out var arr))
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object) results.Add(ParseEntry(e, ""));
        }
        catch { /* a failed search is just an empty list */ }

        return results;
    }

    private static VideoEntry ParseEntry(JsonElement e, string fallbackUrl)
    {
        var id = Str(e, "id");
        var entry = new VideoEntry
        {
            Id = id,
            RawTitle = Str(e, "title"),
            Uploader = Str(e, "uploader") is { Length: > 0 } u ? u : Str(e, "channel"),
            DurationSeconds = (int)NumD(e, "duration"),
            ViewCount = (long)NumD(e, "view_count"),
            YtTrack = Str(e, "track"),
            YtArtist = Str(e, "artist"),
            YtAlbum = Str(e, "album"),
            YtYear = Str(e, "release_year"),
            ThumbnailUrl = Str(e, "thumbnail"),
        };

        entry.Url = Str(e, "webpage_url") is { Length: > 0 } w ? w
                  : id.Length > 0 ? $"https://www.youtube.com/watch?v={id}"
                  : fallbackUrl;

        if (entry.ThumbnailUrl.Length == 0 && id.Length > 0)
            entry.ThumbnailUrl = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";

        if (entry.YtYear.Length == 0 && e.TryGetProperty("release_year", out var ry)
            && ry.ValueKind == JsonValueKind.Number)
            entry.YtYear = ry.GetRawText();

        return entry;
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double NumD(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string? LastError(string stderr)
    {
        var lines = stderr.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var err = lines.LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase));
        var msg = err ?? lines.LastOrDefault();
        return msg is null ? null : msg.Length > 300 ? msg[..300] : msg;
    }

    // ------------------------------------------------------------ download

    /// <summary>
    /// Downloads bestaudio and transcodes it. Returns the path to a file inside a
    /// temp folder - the caller moves it into place and deletes the folder.
    /// </summary>
    public async Task<(string file, string tempDir)> DownloadAsync(
        string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TrackForge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var args = new List<string>
        {
            "-f", "bestaudio/best",
            "-x", "--audio-format", _cfg.Format,
            "--audio-quality", _cfg.Format == "mp3" ? _cfg.Bitrate + "K" : "0",
            "--no-playlist", "--no-warnings", "--newline", "--ignore-config",
            "--no-embed-metadata", "--no-embed-thumbnail",
            "-o", Path.Combine(tempDir, "track.%(ext)s"),
        };

        var ffmpegDir = Path.GetDirectoryName(FfmpegPath);
        if (!string.IsNullOrWhiteSpace(ffmpegDir)) { args.Add("--ffmpeg-location"); args.Add(ffmpegDir); }
        if (!string.IsNullOrWhiteSpace(_cfg.CookiesFromBrowser))
        {
            args.Add("--cookies-from-browser");
            args.Add(_cfg.CookiesFromBrowser);
        }
        args.Add(url);

        var tail = new Queue<string>();
        try
        {
            using var p = Process.Start(NewPsi(YtDlpPath, args))
                ?? throw new InvalidOperationException(
                    "Could not start yt-dlp. Install it with:  pip install -U yt-dlp");

            var readErr = p.StandardError.ReadToEndAsync(ct);

            while (await p.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                tail.Enqueue(line);
                while (tail.Count > 25) tail.Dequeue();

                var m = ProgressLine().Match(line);
                if (m.Success && double.TryParse(m.Groups[1].Value, out var pct))
                    progress?.Report(pct);
                else if (line.Contains("ExtractAudio", StringComparison.Ordinal))
                    progress?.Report(99);
            }

            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            var stderr = await readErr.ConfigureAwait(false);

            var files = Directory.GetFiles(tempDir).Where(f => !f.EndsWith(".part")).ToList();
            if (p.ExitCode != 0 || files.Count == 0)
            {
                SafeDelete(tempDir);
                throw new InvalidOperationException(
                    LastError(stderr) ??
                    tail.LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) ??
                    "Download failed.");
            }

            // Prefer the transcoded file over any leftover source stream.
            var wanted = "." + _cfg.Format;
            var chosen = files
                .OrderBy(f => Path.GetExtension(f).Equals(wanted, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(f => new FileInfo(f).Length)
                .First();

            return (chosen, tempDir);
        }
        catch
        {
            SafeDelete(tempDir);
            throw;
        }
    }

    public static void SafeDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
