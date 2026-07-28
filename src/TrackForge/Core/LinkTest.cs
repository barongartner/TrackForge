namespace TrackForge.Core;

/// <summary>
/// Checks how links are interpreted before anything is downloaded.
///
/// A YouTube sidebar link carries a radio mix (list=RD..., start_radio=1). Expanding
/// that queues an endless generated stream instead of the one song the user wanted,
/// which is exactly how "it isn't downloading" looks from the outside.
///
/// Run: TrackForge.exe --linktest [urlsFile]
/// </summary>
public static class LinkTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        var file = args.SkipWhile(a => a != "--linktest").Skip(1).FirstOrDefault();
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(file) && !file.StartsWith("--") && File.Exists(file))
            urls.AddRange(File.ReadAllLines(file));

        if (urls.Count == 0)
        {
            Console.WriteLine("No urls file given. Pass a file with one link per line.");
            return 1;
        }

        urls = urls.Select(u => u.Trim()).Where(u => u.StartsWith("http")).ToList();

        Console.WriteLine("TrackForge link test");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{urls.Count} link(s)\n");

        using var forge = new ForgeService();
        int failures = 0;

        // ---------------------------------------------------- normalisation
        Console.WriteLine("[normalise]");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();

        foreach (var url in urls)
        {
            var (normalised, single) = YtDlp.NormalizeForProbe(url);
            bool isNew = seen.Add(normalised);
            if (isNew) unique.Add(url);

            var kind = single ? "radio -> single" : "playlist/video";
            var dup = isNew ? "" : "   DUPLICATE, skipped";
            Console.WriteLine($"           {kind,-15} {Short(normalised),-46}{dup}");
        }
        Console.WriteLine($"\n           {urls.Count} in, {unique.Count} unique to fetch");

        // ------------------------------------------------------------ probe
        Console.WriteLine("\n[probe]   what each link actually resolves to");
        int totalTracks = 0;

        foreach (var url in unique)
        {
            var started = Environment.TickCount64;
            try
            {
                var probe = forge.Downloader.ProbeAsync(url);
                if (await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(60))) != probe)
                {
                    Console.WriteLine($"           FAIL  timed out   {Short(url)}");
                    failures++;
                    continue;
                }

                var (entries, playlist) = await probe;
                var seconds = (Environment.TickCount64 - started) / 1000.0;
                totalTracks += entries.Count;

                var (artist, title) = entries.Count > 0 ? entries[0].Guess() : ("", "");
                var label = playlist is null ? $"{artist} - {title}" : $"playlist \"{playlist}\"";

                // One entry is the healthy answer for a radio link. Dozens means the
                // mix got expanded and the user is about to get a queue of junk.
                bool suspicious = entries.Count > 25;
                if (suspicious) failures++;

                Console.WriteLine($"           {(suspicious ? "FAIL" : "ok  ")}  " +
                                  $"{entries.Count,3} track(s)  {seconds,5:0.0}s   {Trim(label, 44)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"           FAIL  {Trim(ex.Message, 60)}");
                failures++;
            }
        }

        Console.WriteLine($"\n           {totalTracks} track(s) would be queued from {unique.Count} link(s)");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine(failures == 0
            ? "PASS  every link resolved to what the user actually asked for"
            : $"FAIL  {failures} link(s) misbehaved");
        return failures == 0 ? 0 : 1;
    }

    private static string Short(string url) =>
        url.Replace("https://www.youtube.com/", "").Replace("https://youtu.be/", "youtu.be/");

    private static string Trim(string? s, int width)
    {
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length > width ? s[..(width - 1)] + "~" : s;
    }
}
