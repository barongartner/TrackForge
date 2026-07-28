namespace TrackForge.Core;

/// <summary>
/// Rewrites the tags already on disk in the format Windows actually reads.
///
/// Files written before the ID3v2.3 switch carry v2.4 frames, which the Windows shell
/// ignores in favour of the ID3v1 tag - so genre shows as a bare number and cover art
/// renders black. Nothing is fetched: every value is read from the file and written
/// straight back, so this is purely a format repair.
///
/// Run: TrackForge.exe --retag [folder]
/// </summary>
public static class RetagTool
{
    public static Task<int> RunAsync(string[] args)
    {
        var cfg = AppConfig.Load();
        var folder = args.SkipWhile(a => a != "--retag").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith("--")) folder = cfg.LibraryFolder;

        Console.WriteLine("TrackForge tag repair");
        Console.WriteLine(new string('-', 70));
        Console.WriteLine($"folder     {folder}\n");

        if (!Directory.Exists(folder))
        {
            Console.WriteLine("FAIL  folder not found");
            return Task.FromResult(1);
        }

        var files = Directory.EnumerateFiles(folder, "*.mp3", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\djay\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"{files.Count} mp3 file(s)\n");

        int repaired = 0, unchanged = 0, failed = 0;

        foreach (var file in files)
        {
            try
            {
                var track = TagService.Read(file);
                var art = TagService.ReadArt(file);

                // Re-titlecase album names too, so "(deluxe Edition)" gets corrected.
                if (cfg.ForceTitleCase && !string.IsNullOrWhiteSpace(track.Album))
                    track.Album = NameFormatter.TitleCase(track.Album);

                TagService.Write(track, art);
                repaired++;

                if (repaired % 25 == 0) Console.WriteLine($"           {repaired}...");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"           FAIL  {Path.GetFileName(file)}  {ex.Message}");
            }
        }

        // Same cleanup the in-app repair does: Windows Media Player's black
        // Folder.jpg placeholders keep showing in its library list after a tag fix.
        int blankArt = 0;
        foreach (var dir in files.Select(Path.GetDirectoryName)
                                 .Where(d => !string.IsNullOrEmpty(d))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            blankArt += TagService.RemoveBlankFolderArt(dir!);
        }
        blankArt += TagService.RemoveBlankMediaPlayerCache();
        if (blankArt > 0) Console.WriteLine($"\n           cleared {blankArt} blank cover image(s)");

        Console.WriteLine($"\n           {repaired} rewritten, {unchanged} skipped, {failed} failed");
        Console.WriteLine(new string('-', 70));
        Console.WriteLine(failed == 0 ? "PASS  tags rewritten as ID3v2.3" : $"FAIL  {failed} file(s) could not be written");
        return Task.FromResult(failed == 0 ? 0 : 1);
    }
}
