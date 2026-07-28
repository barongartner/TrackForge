namespace TrackForge.Core;

/// <summary>
/// End-to-end check of the actual grab pipeline: probe a link, look the track up,
/// download it, tag it, and confirm the file on disk really carries the tags.
///
/// Exists because "downloading doesn't work" is not something to verify by eye.
/// Run: TrackForge.exe --grabtest [url]
/// </summary>
public static class GrabTest
{
    private const string DefaultUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    public static async Task<int> RunAsync(string[] args)
    {
        var url = args.SkipWhile(a => a != "--grabtest").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("--")) url = DefaultUrl;

        Console.WriteLine("TrackForge grab test");
        Console.WriteLine(new string('-', 70));
        Console.WriteLine($"url        {url}\n");

        using var forge = new ForgeService();
        int failures = 0;

        // ---------------------------------------------------------- tools
        var (yt, ff) = await forge.Downloader.CheckToolsAsync();
        Console.WriteLine($"[tools]    yt-dlp {yt ?? "NOT FOUND"}   ffmpeg {(ff is null ? "NOT FOUND" : "ok")}");
        Console.WriteLine($"           using  {forge.Downloader.YtDlpPath}");
        if (yt is null || ff is null) { Console.WriteLine("\nFAIL  tools missing"); return 1; }

        // ---------------------------------------------------------- probe
        Console.WriteLine("\n[probe]");
        List<VideoEntry> entries;
        try
        {
            var probeTask = forge.Downloader.ProbeAsync(url);
            // A probe that never returns is the deadlock this test exists to catch.
            if (await Task.WhenAny(probeTask, Task.Delay(TimeSpan.FromSeconds(90))) != probeTask)
            {
                Console.WriteLine("           FAIL  probe never returned (deadlock)");
                return 1;
            }
            (entries, _) = await probeTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"           FAIL  {ex.Message}");
            return 1;
        }

        if (entries.Count == 0) { Console.WriteLine("           FAIL  no entries"); return 1; }

        var entry = entries[0];
        var (artist, title) = entry.Guess();
        Console.WriteLine($"           title  {entry.RawTitle}");
        Console.WriteLine($"           guess  artist=\"{artist}\"  title=\"{title}\"  {entry.DurationText}");

        // --------------------------------------------------------- lookup
        Console.WriteLine("\n[lookup]");
        var candidates = await forge.Metadata.LookupAsync(artist, title, entry.DurationSeconds, deep: true);
        var merged = MetadataClient.Merge(candidates);
        Console.WriteLine($"           {candidates.Count} candidate(s)");
        if (merged is not null)
            Console.WriteLine($"           merged {merged.SourceLabel} ({merged.Score:0})  " +
                              $"{merged.Artist} - {merged.Title}  [{merged.Album} {merged.Year}]  {merged.Genre}");

        // ------------------------------------------------------- download
        Console.WriteLine("\n[download]");
        var meta = new Track { Title = title, Artist = artist, DurationSeconds = entry.DurationSeconds };
        merged?.ApplyTo(meta, overwrite: true, forge.Config.ForceTitleCase);

        var outDir = Path.Combine(Path.GetTempPath(), "TrackForge-grabtest");
        Directory.CreateDirectory(outDir);
        foreach (var stale in Directory.GetFiles(outDir)) { try { File.Delete(stale); } catch { } }

        var job = forge.EnqueueGrab(new ForgeService.GrabRequest(
            entry.Url, meta, merged?.ArtUrl, null, outDir));

        string lastMessage = "";
        var deadline = DateTime.UtcNow.AddMinutes(6);
        while (job.State is JobState.Queued or JobState.Running && DateTime.UtcNow < deadline)
        {
            if (job.Message != lastMessage)
            {
                lastMessage = job.Message;
                Console.WriteLine($"           {job.Progress,5:0}%  {lastMessage}");
            }
            await Task.Delay(400);
        }

        if (job.State != JobState.Done)
        {
            Console.WriteLine($"           FAIL  {job.State}: {job.Message}");
            if (job.Error is not null) Console.WriteLine(job.Error[..Math.Min(600, job.Error.Length)]);
            return 1;
        }

        // --------------------------------------------------------- verify
        Console.WriteLine("\n[verify]");
        var produced = Directory.GetFiles(outDir);
        if (produced.Length == 0) { Console.WriteLine("           FAIL  no file produced"); return 1; }

        var path = produced[0];
        var info = new FileInfo(path);
        Console.WriteLine($"           file   {info.Name}  ({info.Length / 1048576.0:0.0} MB)");

        // Read it back off disk: writing tags and having them stick are different things.
        var written = TagService.Read(path);
        var checks = new (string name, bool ok, string value)[]
        {
            ("title", !string.IsNullOrWhiteSpace(written.Title), written.Title),
            ("artist", !string.IsNullOrWhiteSpace(written.Artist), written.Artist),
            ("album", !string.IsNullOrWhiteSpace(written.Album), written.Album),
            ("year", !string.IsNullOrWhiteSpace(written.Year), written.Year),
            ("genre", !string.IsNullOrWhiteSpace(written.Genre), written.Genre),
            ("bpm", !string.IsNullOrWhiteSpace(written.Bpm), written.Bpm),
            ("key", !string.IsNullOrWhiteSpace(written.MusicalKey), $"{written.MusicalKey} {written.Camelot}"),
            ("art", written.HasArt, written.HasArt ? "embedded" : "none"),
            ("audio", written.DurationSeconds > 1, $"{written.DurationText} @ {written.Bitrate}kbps"),
        };

        foreach (var (name, ok, value) in checks)
        {
            Console.WriteLine($"           {(ok ? "ok  " : "MISS")}  {name,-7} {value}");
            // Title, artist and actual audio are the ones that make it a real result.
            if (!ok && name is "title" or "artist" or "audio") failures++;
        }

        Console.WriteLine($"\n           kept at {path}");
        Console.WriteLine(new string('-', 70));
        Console.WriteLine(failures == 0 ? "PASS  download and tagging work end to end" : $"FAIL  {failures} problem(s)");
        return failures == 0 ? 0 : 1;
    }
}
