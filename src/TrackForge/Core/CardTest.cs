namespace TrackForge.Core;

/// <summary>
/// Drives a real GrabCard the way a user does: probe a link, press Look up once,
/// then read the actual text boxes.
///
/// Exists because a lookup that populated Meta correctly still showed almost nothing
/// on screen - writing one box fired TextChanged, which pulled the still-empty boxes
/// back over the fresh values. Checking Meta would have passed; only reading the
/// controls catches it.
///
/// Run: TrackForge.exe --cardtest [url]
/// </summary>
public static class CardTest
{
    private const string DefaultUrl =
        "https://www.youtube.com/watch?v=ATyrsQYQaJQ&list=RDATyrsQYQaJQ&start_radio=1";

    public static async Task<int> RunAsync(string[] args)
    {
        var url = args.SkipWhile(a => a != "--cardtest").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("--")) url = DefaultUrl;

        Console.WriteLine("TrackForge card test  (one press of Look up)");
        Console.WriteLine(new string('-', 70));

        using var forge = new ForgeService();

        var (entries, _) = await forge.Downloader.ProbeAsync(url);
        if (entries.Count == 0) { Console.WriteLine("FAIL  nothing probed"); return 1; }

        var entry = entries[0];
        Console.WriteLine($"video      {entry.RawTitle}\n");

        using var card = new UI.GrabCard(forge, entry);

        var before = card.FieldValuesForTesting.Count(kv => kv.Value.Length > 0);
        Console.WriteLine($"[before]   {before} field(s) filled from the video title alone");

        // Exactly one press. This is the whole point of the test.
        await card.LookupAsync();
        Application.DoEvents();

        var after = card.FieldValuesForTesting;
        Console.WriteLine("\n[after one press]");
        foreach (var (key, value) in after.OrderBy(kv => kv.Key))
            Console.WriteLine($"           {(value.Length > 0 ? "ok  " : "----")}  {key,-12} {value}");

        int filled = after.Count(kv => kv.Value.Length > 0);
        Console.WriteLine($"\n           {filled}/{after.Count} boxes populated");
        Console.WriteLine($"           status: {card.StatusForTesting}");

        // Title, artist, album, year and genre are the ones a single press must land.
        var required = new[] { "title", "artist", "album", "year", "genre" };
        var missing = required.Where(k => after.TryGetValue(k, out var v) && v.Length == 0).ToList();

        Console.WriteLine(new string('-', 70));
        if (missing.Count > 0)
        {
            Console.WriteLine($"FAIL  one press left these empty: {string.Join(", ", missing)}");
            return 1;
        }

        Console.WriteLine("PASS  one press of Look up fills every field it can");
        return 0;
    }
}
