namespace TrackForge.Core;

/// <summary>
/// The application core. Owns config, the job queue and every service, and
/// exposes the operations the UI actually needs.
/// </summary>
public sealed class ForgeService : IDisposable
{
    public AppConfig Config { get; }
    public JobQueue Jobs { get; }
    public LibraryScanner Library { get; }
    public MetadataClient Metadata { get; }
    public YtDlp Downloader { get; }

    public event Action? LibraryChanged;

    public ForgeService()
    {
        Config = AppConfig.Load();
        Jobs = new JobQueue(Config.MaxConcurrentJobs);
        Library = new LibraryScanner();
        Metadata = new MetadataClient { Country = Config.ItunesCountry };
        Downloader = new YtDlp(Config);
    }

    public void SaveConfig()
    {
        Config.Save();
        Metadata.Country = Config.ItunesCountry;
    }

    // ------------------------------------------------------------- library

    public async Task RescanLibraryAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await Library.ScanAsync(Config.LibraryFolder, Config.ImportDjayData, progress, ct)
            .ConfigureAwait(false);
        LibraryChanged?.Invoke();
    }

    public void RaiseLibraryChanged() => LibraryChanged?.Invoke();

    /// <summary>True if a track with this artist + title is already in the library.</summary>
    public bool AlreadyHave(string artist, string title)
    {
        static string N(string? s) =>
            new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        var a = N(artist);
        var t = N(title);
        if (t.Length == 0) return false;

        return Library.Tracks.Any(x =>
            N(x.Title) == t && (a.Length == 0 || N(x.Artist).Contains(a) || a.Contains(N(x.Artist))));
    }

    // ------------------------------------------------------------ download

    public sealed record GrabRequest(string Url, Track Meta, string? ArtUrl, byte[]? ArtBytes, string? OutputFolder);

    /// <summary>Download, tag, name and file a single track.</summary>
    public Job EnqueueGrab(GrabRequest request)
    {
        var label = $"{request.Meta.Artist} - {request.Meta.Title}".Trim(' ', '-');
        if (label.Length == 0) label = request.Url;

        return Jobs.Enqueue("grab", label, async (job, ct) =>
        {
            string? tempDir = null;
            try
            {
                Jobs.Report(job, 2, "Fetching audio");
                var progress = new Progress<double>(p =>
                    Jobs.Report(job, 2 + p * 0.55, $"Downloading {p:0}%"));

                var (file, dir) = await Downloader.DownloadAsync(request.Url, progress, ct)
                    .ConfigureAwait(false);
                tempDir = dir;

                var meta = request.Meta.Clone();
                meta.Path = file;

                if (Config.ForceTitleCase)
                {
                    meta.Title = NameFormatter.TitleCase(meta.Title);
                    meta.Artist = NameFormatter.TitleCase(meta.Artist);
                    meta.Album = NameFormatter.TitleCase(meta.Album);
                    meta.AlbumArtist = NameFormatter.TitleCase(meta.AlbumArtist);
                }

                if (Config.AnalyzeBpmAndKey && string.IsNullOrWhiteSpace(meta.Bpm))
                {
                    Jobs.Report(job, 64, "Analysing tempo and key");
                    var analysis = await AudioAnalyzer
                        .AnalyzeAsync(file, Downloader.FfmpegPath, ct).ConfigureAwait(false);
                    if (analysis.Bpm is > 0) meta.Bpm = Math.Round(analysis.Bpm.Value).ToString();
                    if (!string.IsNullOrWhiteSpace(analysis.Key) && string.IsNullOrWhiteSpace(meta.MusicalKey))
                    {
                        meta.MusicalKey = analysis.Key!;
                        meta.Camelot = analysis.Camelot ?? "";
                    }
                }

                var art = request.ArtBytes;
                if (art is null && !string.IsNullOrWhiteSpace(request.ArtUrl))
                {
                    Jobs.Report(job, 74, "Fetching cover art");
                    art = await Metadata.DownloadArtAsync(request.ArtUrl, ct).ConfigureAwait(false);
                }

                if (Config.WriteSourceUrl) meta.SourceUrl = request.Url;

                Jobs.Report(job, 84, "Writing tags");
                TagService.Write(meta, art);

                var outputFolder = string.IsNullOrWhiteSpace(request.OutputFolder)
                    ? Config.OutputFolder : request.OutputFolder!;
                Directory.CreateDirectory(outputFolder);

                var fileName = NameFormatter.BuildFileName(
                    meta, Config.FilenamePattern, Path.GetExtension(file));
                var destination = NameFormatter.UniquePath(Path.Combine(outputFolder, fileName));

                File.Move(file, destination);
                meta.Path = destination;
                meta.HasArt = art is { Length: > 0 };

                job.Result = meta;
                Jobs.Report(job, 100, "Saved  " + Path.GetFileName(destination));
                RaiseLibraryChanged();
            }
            finally
            {
                if (tempDir is not null) YtDlp.SafeDelete(tempDir);
            }
        });
    }

    // -------------------------------------------------------------- enrich

    public sealed record EnrichOptions(
        bool Overwrite, bool FetchArt, bool AnalyzeAudio, bool RenameFiles,
        IReadOnlyList<string> Fields);

    /// <summary>Fill in missing tags on library files from online sources.</summary>
    public Job EnqueueEnrich(IReadOnlyList<Track> tracks, EnrichOptions options)
    {
        return Jobs.Enqueue("enrich", $"Fill tags on {tracks.Count} track(s)", async (job, ct) =>
        {
            int updated = 0, skipped = 0;

            for (int i = 0; i < tracks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var track = tracks[i];
                Jobs.Report(job, (double)i / tracks.Count * 100,
                    $"{i + 1}/{tracks.Count}  {track.Title}");

                bool changed = false;
                byte[]? art = null;

                var candidates = await Metadata
                    .LookupAsync(track.Artist, track.Title, track.DurationSeconds, ct: ct)
                    .ConfigureAwait(false);
                // Merge across sources so one pass fills everything available, rather
                // than leaving gaps only the next source down could have covered.
                var best = MetadataClient.Merge(candidates);

                if (best is { Score: >= 45 })
                {
                    best.ApplyTo(track, options.Overwrite, Config.ForceTitleCase, options.Fields);
                    changed = true;

                    if (options.FetchArt && !track.HasArt && best.ArtUrl.Length > 0)
                        art = await Metadata.DownloadArtAsync(best.ArtUrl, ct).ConfigureAwait(false);
                }

                if (options.AnalyzeAudio && string.IsNullOrWhiteSpace(track.DisplayBpm))
                {
                    var analysis = await AudioAnalyzer
                        .AnalyzeAsync(track.Path, Downloader.FfmpegPath, ct).ConfigureAwait(false);
                    if (analysis.Bpm is > 0)
                    {
                        track.Bpm = Math.Round(analysis.Bpm.Value).ToString();
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(analysis.Key) && string.IsNullOrWhiteSpace(track.MusicalKey))
                    {
                        track.MusicalKey = analysis.Key!;
                        track.Camelot = analysis.Camelot ?? "";
                        changed = true;
                    }
                }

                if (!changed && art is null) { skipped++; continue; }

                try
                {
                    TagService.Write(track, art);
                    if (options.RenameFiles) RenameToPattern(track);
                    updated++;
                }
                catch { skipped++; }
            }

            Jobs.Report(job, 100, $"Updated {updated}, skipped {skipped}");
            RaiseLibraryChanged();
        });
    }

    /// <summary>Analyse BPM and key for library files, no network involved.</summary>
    public Job EnqueueAnalyze(IReadOnlyList<Track> tracks, bool write = true)
    {
        return Jobs.Enqueue("analyze", $"Analyse {tracks.Count} track(s)", async (job, ct) =>
        {
            int done = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var track = tracks[i];
                Jobs.Report(job, (double)i / tracks.Count * 100,
                    $"{i + 1}/{tracks.Count}  {track.Title}");

                var analysis = await AudioAnalyzer
                    .AnalyzeAsync(track.Path, Downloader.FfmpegPath, ct).ConfigureAwait(false);
                if (analysis.Bpm is null) continue;

                track.Bpm = Math.Round(analysis.Bpm.Value).ToString();
                if (!string.IsNullOrWhiteSpace(analysis.Key))
                {
                    track.MusicalKey = analysis.Key!;
                    track.Camelot = analysis.Camelot ?? "";
                }

                if (write)
                {
                    try { TagService.Write(track); done++; } catch { }
                }
            }

            Jobs.Report(job, 100, $"Analysed {done} of {tracks.Count}");
            RaiseLibraryChanged();
        });
    }

    /// <summary>Renames a file to match the configured pattern. Updates track.Path.</summary>
    public bool RenameToPattern(Track track)
    {
        try
        {
            var dir = Path.GetDirectoryName(track.Path);
            if (string.IsNullOrEmpty(dir)) return false;

            var wanted = NameFormatter.BuildFileName(
                track, Config.FilenamePattern, Path.GetExtension(track.Path));
            var target = Path.Combine(dir, wanted);

            if (string.Equals(target, track.Path, StringComparison.OrdinalIgnoreCase)) return false;

            target = NameFormatter.UniquePath(target);
            File.Move(track.Path, target);
            track.Path = target;
            return true;
        }
        catch { return false; }
    }

    public void Dispose() => Jobs.Dispose();
}
