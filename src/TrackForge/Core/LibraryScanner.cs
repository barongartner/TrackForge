namespace TrackForge.Core;

/// <summary>Walks the library folder and reads tags off every audio file it finds.</summary>
public sealed class LibraryScanner
{
    private static readonly string[] SkipFolders = { "djay", "Backups", ".trackforge", "$RECYCLE.BIN" };

    public IReadOnlyList<Track> Tracks { get; private set; } = Array.Empty<Track>();
    public DateTime LastScan { get; private set; }
    public string ScannedRoot { get; private set; } = "";

    public async Task<IReadOnlyList<Track>> ScanAsync(
        string root, bool importDjay, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var found = await Task.Run(() =>
        {
            var djay = importDjay
                ? DjayImporter.Load(root)
                : new Dictionary<string, double>();

            var list = new List<Track>();
            if (!Directory.Exists(root)) return list;

            int count = 0;
            foreach (var file in EnumerateAudio(root))
            {
                ct.ThrowIfCancellationRequested();
                var track = TagService.Read(file);
                track.RelativePath = Path.GetRelativePath(root, file);
                if (djay.TryGetValue(Path.GetFileName(file), out var bpm)) track.DjayBpm = bpm;
                list.Add(track);

                if (++count % 25 == 0) progress?.Report($"Scanned {count} files...");
            }

            list.Sort(CompareForDisplay);
            return list;
        }, ct).ConfigureAwait(false);

        Tracks = found;
        LastScan = DateTime.Now;
        ScannedRoot = root;
        return found;
    }

    private static IEnumerable<string> EnumerateAudio(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (SkipFolders.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                stack.Push(sub);
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var f in files)
                if (TagService.IsAudio(f)) yield return f;
        }
    }

    /// <summary>Album artist, then album, then track number - the way a library reads.</summary>
    private static int CompareForDisplay(Track a, Track b)
    {
        int c = string.Compare(SortArtist(a), SortArtist(b), StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        c = string.Compare(a.Album, b.Album, StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        return TrackNo(a).CompareTo(TrackNo(b));
    }

    private static string SortArtist(Track t) =>
        string.IsNullOrWhiteSpace(t.AlbumArtist)
            ? (string.IsNullOrWhiteSpace(t.Artist) ? "~" : t.Artist)
            : t.AlbumArtist;

    private static int TrackNo(Track t) =>
        int.TryParse((t.TrackNumber ?? "").Split('/')[0], out var n) ? n : 0;
}
