namespace TrackForge.Core;

/// <summary>One audio file, plus everything we know or want to know about it.</summary>
public sealed class Track
{
    public string Path { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(Path);
    public string RelativePath { get; set; } = "";

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Year { get; set; } = "";
    public string TrackNumber { get; set; } = "";
    public string TrackCount { get; set; } = "";
    public string DiscNumber { get; set; } = "";
    public string Bpm { get; set; } = "";
    public string MusicalKey { get; set; } = "";
    public string Camelot { get; set; } = "";
    public string Isrc { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Comment { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public int Rating { get; set; }

    public bool HasArt { get; set; }
    public double DurationSeconds { get; set; }
    public int Bitrate { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>BPM that the djay app already worked out, if we could read it.</summary>
    public double? DjayBpm { get; set; }

    public byte[]? PendingArt { get; set; }
    public string? PendingArtUrl { get; set; }

    public string DurationText => DurationSeconds <= 0
        ? ""
        : TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    public string DisplayBpm => !string.IsNullOrWhiteSpace(Bpm)
        ? Bpm
        : DjayBpm.HasValue ? Math.Round(DjayBpm.Value).ToString() : "";

    /// <summary>Tag fields this file is missing, for the Library "Missing" column.</summary>
    public IEnumerable<string> MissingFields()
    {
        if (string.IsNullOrWhiteSpace(Title)) yield return "title";
        if (string.IsNullOrWhiteSpace(Artist)) yield return "artist";
        if (string.IsNullOrWhiteSpace(Album)) yield return "album";
        if (string.IsNullOrWhiteSpace(Year)) yield return "year";
        if (string.IsNullOrWhiteSpace(Genre)) yield return "genre";
        if (string.IsNullOrWhiteSpace(TrackNumber) || TrackNumber == "0") yield return "track";
        if (string.IsNullOrWhiteSpace(DisplayBpm)) yield return "bpm";
        if (!HasArt) yield return "art";
    }

    public string MissingText => string.Join(", ", MissingFields());
    public bool IsComplete => !MissingFields().Any();

    public Track Clone() => (Track)MemberwiseClone();

    public string SearchBlob => string.Join(' ',
        Title, Artist, AlbumArtist, Album, Genre, Year, FileName).ToLowerInvariant();
}
