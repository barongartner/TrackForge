namespace TrackForge.Core;

/// <summary>One possible metadata match returned by an online source.</summary>
public sealed class MatchCandidate
{
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Year { get; set; } = "";
    public string Genre { get; set; } = "";
    public string TrackNumber { get; set; } = "";
    public string TrackCount { get; set; } = "";
    public string DiscNumber { get; set; } = "";
    public string Isrc { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Bpm { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string ArtUrl { get; set; } = "";
    public string ArtThumbUrl { get; set; } = "";
    public string? AlbumId { get; set; }
    public double Score { get; set; }

    public string Display =>
        $"{Artist} - {Title}" +
        (string.IsNullOrWhiteSpace(Album) ? "" : $"  [{Album}") +
        (string.IsNullOrWhiteSpace(Year) ? "" : $" {Year}") +
        (string.IsNullOrWhiteSpace(Album) ? "" : "]") +
        $"  ({Source} {Score:0})";

    /// <summary>Copy the populated fields onto a track.</summary>
    public void ApplyTo(Track t, bool overwrite, bool titleCase, IEnumerable<string>? only = null)
    {
        var allow = only is null ? null : new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);
        bool Want(string field, string current, string incoming) =>
            !string.IsNullOrWhiteSpace(incoming)
            && (allow is null || allow.Contains(field))
            && (overwrite || string.IsNullOrWhiteSpace(current));

        string Case(string s) => titleCase ? NameFormatter.TitleCase(s) : s;

        if (Want("title", t.Title, Title)) t.Title = Case(Title);
        if (Want("artist", t.Artist, Artist)) t.Artist = Case(Artist);
        if (Want("albumartist", t.AlbumArtist, AlbumArtist)) t.AlbumArtist = Case(AlbumArtist);
        if (Want("album", t.Album, Album)) t.Album = Case(Album);
        if (Want("year", t.Year, Year)) t.Year = Year;
        if (Want("genre", t.Genre, Genre)) t.Genre = Genre;
        if (Want("track", t.TrackNumber, TrackNumber)) t.TrackNumber = TrackNumber;
        if (Want("trackcount", t.TrackCount, TrackCount)) t.TrackCount = TrackCount;
        if (Want("disc", t.DiscNumber, DiscNumber)) t.DiscNumber = DiscNumber;
        if (Want("isrc", t.Isrc, Isrc)) t.Isrc = Isrc;
        if (Want("publisher", t.Publisher, Publisher)) t.Publisher = Publisher;
        if (Want("bpm", t.Bpm, Bpm)) t.Bpm = Bpm;
    }
}
