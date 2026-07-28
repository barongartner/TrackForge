using System.Text;

namespace TrackForge.Core;

/// <summary>
/// Diagnostics. Run "TrackForge.exe --selftest" to check that the library scan,
/// tag reader, naming rules, audio analyser and online lookup all still work.
/// Where djay has already analysed a track, its BPM is used as ground truth to
/// score our own detector.
/// </summary>
public static class SelfTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        var report = new StringBuilder();
        void Line(string s = "") { Console.WriteLine(s); report.AppendLine(s); }

        var cfg = AppConfig.Load();
        var ytdlp = new YtDlp(cfg);
        int failures = 0;

        Line("TrackForge self-test");
        Line(new string('-', 70));
        Line($"config     {AppConfig.ConfigPath}");
        Line($"library    {cfg.LibraryFolder}");
        Line();

        // ---------------------------------------------------------- tools
        var (yt, ff) = await ytdlp.CheckToolsAsync();
        Line($"[tools]    yt-dlp  {yt ?? "NOT FOUND"}");
        Line($"[tools]    ffmpeg  {(ff is null ? "NOT FOUND" : "ok")}");
        if (yt is null || ff is null) failures++;
        Line();

        // -------------------------------------------------------- naming
        Line("[naming]");
        var namingCases = new (string input, string expected)[]
        {
            ("vicinity of obscenity", "Vicinity Of Obscenity"),
            ("B.Y.O.B.", "B.Y.O.B."),
            ("kill rock 'n roll", "Kill Rock 'n Roll"),
            ("this_is_a_test", "This Is a Test"),
        };
        foreach (var (input, expected) in namingCases)
        {
            var actual = NameFormatter.TitleCase(input);
            bool ok = actual == expected;
            if (!ok) failures++;
            Line($"           {(ok ? "ok  " : "FAIL")}  \"{input}\" -> \"{actual}\"" +
                 (ok ? "" : $"   expected \"{expected}\""));
        }

        var sample = new Track { Title = "attack", TrackNumber = "1", Artist = "system of a down" };
        Line($"           filename  \"{NameFormatter.BuildFileName(sample, cfg.FilenamePattern, ".mp3")}\"");
        Line();

        // -------------------------------------------------------- library
        Line("[library]");
        var scanner = new LibraryScanner();
        var tracks = await scanner.ScanAsync(cfg.LibraryFolder, cfg.ImportDjayData);
        Line($"           {tracks.Count} audio files");
        if (tracks.Count == 0) failures++;

        var withArt = tracks.Count(t => t.HasArt);
        var withDjay = tracks.Count(t => t.DjayBpm.HasValue);
        var incomplete = tracks.Count(t => !t.IsComplete);
        Line($"           {withArt} with embedded art, {incomplete} missing something");
        Line($"           {withDjay} with BPM imported from djay");
        Line();

        foreach (var t in tracks.Take(5))
            Line($"           {t.TrackNumber,3}  {Trim(t.Artist, 22)}  {Trim(t.Album, 22)}  " +
                 $"{t.Year,4}  {Trim(t.Genre, 10)}  {Trim(t.Title, 26)}");
        Line();

        // ------------------------------------------------------- analyser
        Line("[analyser]   our detection vs the BPM djay already worked out");
        var ground = tracks.Where(t => t.DjayBpm is > 0).Take(6).ToList();
        if (ground.Count == 0)
        {
            Line("           no djay reference data found, analysing anything instead");
            ground = tracks.Take(3).ToList();
        }

        int exact = 0, octave = 0, off = 0;
        foreach (var t in ground)
        {
            var result = await AudioAnalyzer.AnalyzeAsync(t.Path, ytdlp.FfmpegPath);
            if (result.Bpm is null)
            {
                Line($"           FAIL  {Trim(t.Title, 30)}  could not analyse");
                failures++;
                continue;
            }

            if (t.DjayBpm is null)
            {
                Line($"           ----  {Trim(t.Title, 30)}  {result.Bpm,6:0.0} BPM  " +
                     $"key {result.Key} ({result.Camelot})");
                continue;
            }

            var ours = result.Bpm.Value;
            var theirs = t.DjayBpm.Value;

            // Only a direct hit or a clean half/double counts. A 1.5x "match"
            // is not the same pulse, and scoring it as one just flatters us.
            string verdict;
            if (Math.Abs(ours - theirs) <= 2.0) { verdict = "hit "; exact++; }
            else if (Math.Abs(ours / 2 - theirs) <= 2.0 || Math.Abs(ours * 2 - theirs) <= 2.0)
            { verdict = "oct "; octave++; }
            else { verdict = "MISS"; off++; }

            Line($"           {verdict}  {Trim(t.Title, 30)}  " +
                 $"ours {ours,6:0.0}  djay {theirs,6:0.0}  key {result.Key} ({result.Camelot})");
        }

        if (exact + octave + off > 0)
        {
            Line($"           {exact} exact, {octave} half/double, {off} wrong");
            // Octave errors are tolerable - a DJ reads 174 and 87 the same way.
            // Genuine misses are not, so they are the only thing that fails a run.
            if (off > (exact + octave + off) / 2) failures++;
        }
        Line();

        // -------------------------------------------------------- lookup
        Line("[lookup]");
        var metadata = new MetadataClient { Country = cfg.ItunesCountry };
        var probe = tracks.FirstOrDefault(t => t.Title.Length > 0 && t.Artist.Length > 0);
        if (probe is null)
        {
            Line("           no tagged track to test with");
        }
        else
        {
            var candidates = await metadata.LookupAsync(probe.Artist, probe.Title, probe.DurationSeconds, deep: true);
            Line($"           query   {probe.Artist} - {probe.Title}");
            Line($"           {candidates.Count} candidate(s)");
            if (candidates.Count == 0) failures++;
            foreach (var c in candidates.Take(4))
                Line($"           {c.Score,6:0.0}  {c.Source,-12} {Trim(c.Artist, 20)}  " +
                     $"{Trim(c.Album, 24)}  {c.Year}  {Trim(c.Genre, 14)}  art:{(c.ArtUrl.Length > 0 ? "yes" : "no")}");

            // One lookup has to fill every field it can, not just what the top
            // source happens to carry. Prove the merge closes the gaps.
            static int Filled(MatchCandidate c) => new[]
            {
                c.Title, c.Artist, c.Album, c.AlbumArtist, c.Year,
                c.Genre, c.TrackNumber, c.DiscNumber, c.Isrc,
            }.Count(v => !string.IsNullOrWhiteSpace(v));

            var merged = MetadataClient.Merge(candidates);
            if (merged is not null)
            {
                int before = Filled(candidates[0]);
                int after = Filled(merged);
                Line($"           merge   top source alone {before}/9 fields -> merged {after}/9");
                Line($"           from    {merged.SourceLabel}");
                if (after < before) failures++;
            }

            var top = candidates.FirstOrDefault(c => c.ArtUrl.Length > 0);
            if (top is not null)
            {
                var art = await metadata.DownloadArtAsync(top.ArtUrl);
                if (art is null) { Line("           FAIL  cover art download"); failures++; }
                else
                {
                    var normalised = TagService.NormaliseArt(art);
                    using var image = TagService.ImageFromBytes(normalised);
                    Line($"           art     {art.Length / 1024} KB -> {normalised.Length / 1024} KB " +
                         $"({image?.Width}x{image?.Height})");
                    if (image is null || image.Width != image.Height) failures++;
                }
            }
        }
        Line();

        // ------------------------------------------------------- yt search
        if (yt is not null && args.Contains("--online"))
        {
            Line("[youtube]");
            var hits = await ytdlp.SearchAsync("system of a down toxicity", 3);
            Line($"           {hits.Count} result(s)");
            foreach (var h in hits)
                Line($"           {h.DurationText,6}  {Trim(h.RawTitle, 46)}  {h.Url}");
            if (hits.Count == 0) failures++;
            Line();
        }

        Line(new string('-', 70));
        Line(failures == 0 ? "PASS  everything checks out" : $"FAIL  {failures} problem(s)");

        try
        {
            Directory.CreateDirectory(AppConfig.ConfigDirectory);
            File.WriteAllText(Path.Combine(AppConfig.ConfigDirectory, "selftest.log"), report.ToString());
        }
        catch { }

        return failures == 0 ? 0 : 1;
    }

    private static string Trim(string? s, int width)
    {
        s ??= "";
        if (s.Length > width) s = s[..(width - 1)] + "~";
        return s.PadRight(width);
    }
}
