using System.Drawing.Imaging;
using TagLib;
using File = System.IO.File;

namespace TrackForge.Core;

/// <summary>Reads and writes tags + embedded artwork via TagLib#.</summary>
public static class TagService
{
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".m4a", ".opus", ".ogg", ".wav", ".aac", ".wma", ".aiff" };

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    public static Track Read(string path)
    {
        var t = new Track { Path = path };
        try { t.SizeBytes = new FileInfo(path).Length; } catch { }

        try
        {
            using var f = TagLib.File.Create(path);
            var tag = f.Tag;

            t.Title = tag.Title ?? "";
            t.Artist = tag.Performers is { Length: > 0 } p ? string.Join("; ", p) : "";
            t.AlbumArtist = tag.AlbumArtists is { Length: > 0 } aa ? string.Join("; ", aa) : "";
            t.Album = tag.Album ?? "";
            t.Genre = tag.Genres is { Length: > 0 } g ? g[0] : "";
            t.Year = tag.Year > 0 ? tag.Year.ToString() : "";
            t.TrackNumber = tag.Track > 0 ? tag.Track.ToString() : "";
            t.TrackCount = tag.TrackCount > 0 ? tag.TrackCount.ToString() : "";
            t.DiscNumber = tag.Disc > 0 ? tag.Disc.ToString() : "";
            t.Bpm = tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : "";
            t.Composer = tag.Composers is { Length: > 0 } c ? c[0] : "";
            t.Publisher = tag.Publisher ?? "";
            t.Comment = tag.Comment ?? "";
            t.HasArt = tag.Pictures is { Length: > 0 };

            t.DurationSeconds = f.Properties?.Duration.TotalSeconds ?? 0;
            t.Bitrate = f.Properties?.AudioBitrate ?? 0;

            // ID3v2-only fields TagLib's generic Tag doesn't surface.
            if (f.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                t.MusicalKey = TextFrame(id3, "TKEY");
                t.Isrc = TextFrame(id3, "TSRC");
                t.Camelot = UserFrame(id3, "CAMELOT");
                t.SourceUrl = UrlFrame(id3, "WOAS");
                t.Rating = ReadRating(id3);
            }
        }
        catch { /* unreadable or exotic file: return what we have */ }

        return t;
    }

    private static string TextFrame(TagLib.Id3v2.Tag tag, string id)
    {
        var f = TagLib.Id3v2.TextInformationFrame.Get(tag, id, false);
        return f?.Text is { Length: > 0 } txt ? txt[0] : "";
    }

    private static string UserFrame(TagLib.Id3v2.Tag tag, string desc)
    {
        var f = TagLib.Id3v2.UserTextInformationFrame.Get(tag, desc, false);
        return f?.Text is { Length: > 0 } txt ? txt[0] : "";
    }

    private static string UrlFrame(TagLib.Id3v2.Tag tag, string id)
    {
        foreach (var frame in tag.GetFrames<TagLib.Id3v2.UrlLinkFrame>())
            if (frame.FrameId.ToString() == id) return frame.Text is { Length: > 0 } u ? u[0] : "";
        return "";
    }

    private static int ReadRating(TagLib.Id3v2.Tag tag)
    {
        foreach (var f in tag.GetFrames<TagLib.Id3v2.PopularimeterFrame>())
            return f.Rating switch
            {
                0 => 0, <= 1 => 1, <= 64 => 2, <= 128 => 3, <= 196 => 4, _ => 5
            };
        return 0;
    }

    private static readonly byte[] PopmScale = { 0, 1, 64, 128, 196, 255 };

    /// <summary>Writes every populated field. Blank fields are left untouched.</summary>
    public static void Write(Track t, byte[]? art = null)
    {
        using var f = TagLib.File.Create(t.Path);
        var tag = f.Tag;

        if (!string.IsNullOrWhiteSpace(t.Title)) tag.Title = t.Title;
        if (!string.IsNullOrWhiteSpace(t.Artist)) tag.Performers = SplitArtists(t.Artist);
        if (!string.IsNullOrWhiteSpace(t.AlbumArtist)) tag.AlbumArtists = SplitArtists(t.AlbumArtist);
        if (!string.IsNullOrWhiteSpace(t.Album)) tag.Album = t.Album;
        if (!string.IsNullOrWhiteSpace(t.Genre)) tag.Genres = new[] { t.Genre };
        if (!string.IsNullOrWhiteSpace(t.Composer)) tag.Composers = new[] { t.Composer };
        if (!string.IsNullOrWhiteSpace(t.Publisher)) tag.Publisher = t.Publisher;
        if (!string.IsNullOrWhiteSpace(t.Comment)) tag.Comment = t.Comment;

        if (uint.TryParse(t.Year, out var year) && year > 0) tag.Year = year;
        if (uint.TryParse((t.TrackNumber ?? "").Split('/')[0], out var trk) && trk > 0) tag.Track = trk;
        if (uint.TryParse(t.TrackCount, out var trkTotal) && trkTotal > 0) tag.TrackCount = trkTotal;
        if (uint.TryParse(t.DiscNumber, out var disc) && disc > 0) tag.Disc = disc;
        if (uint.TryParse(t.Bpm, out var bpm) && bpm > 0) tag.BeatsPerMinute = bpm;

        if (f.GetTag(TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3)
        {
            if (!string.IsNullOrWhiteSpace(t.MusicalKey)) SetText(id3, "TKEY", t.MusicalKey);
            if (!string.IsNullOrWhiteSpace(t.Isrc)) SetText(id3, "TSRC", t.Isrc);
            if (!string.IsNullOrWhiteSpace(t.Camelot))
                TagLib.Id3v2.UserTextInformationFrame.Get(id3, "CAMELOT", true).Text = new[] { t.Camelot };
            if (!string.IsNullOrWhiteSpace(t.SourceUrl)) SetUrl(id3, t.SourceUrl);
            if (t.Rating > 0)
            {
                foreach (var old in id3.GetFrames<TagLib.Id3v2.PopularimeterFrame>().ToList())
                    id3.RemoveFrame(old);
                var popm = TagLib.Id3v2.PopularimeterFrame.Get(id3, "Windows Media Player 9 Series", true);
                popm.Rating = PopmScale[Math.Clamp(t.Rating, 0, 5)];
            }
        }

        if (art is { Length: > 0 })
        {
            var normalised = NormaliseArt(art);
            tag.Pictures = new IPicture[]
            {
                new Picture(new ByteVector(normalised))
                {
                    Type = PictureType.FrontCover,
                    MimeType = "image/jpeg",
                    Description = "Cover"
                }
            };
            t.HasArt = true;
        }

        f.Save();
    }

    private static void SetText(TagLib.Id3v2.Tag tag, string id, string value)
        => TagLib.Id3v2.TextInformationFrame.Get(tag, id, true).Text = new[] { value };

    private static void SetUrl(TagLib.Id3v2.Tag tag, string url)
    {
        foreach (var old in tag.GetFrames<TagLib.Id3v2.UrlLinkFrame>()
                               .Where(x => x.FrameId.ToString() == "WOAS").ToList())
            tag.RemoveFrame(old);
        var frame = new TagLib.Id3v2.UrlLinkFrame("WOAS") { Text = new[] { url } };
        tag.AddFrame(frame);
    }

    private static string[] SplitArtists(string s) =>
        s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static byte[]? ReadArt(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var pics = f.Tag.Pictures;
            if (pics is { Length: > 0 }) return pics[0].Data.Data;
        }
        catch { }
        return null;
    }

    /// <summary>Square-crop, cap at 1000px, re-encode JPEG so every cover matches.</summary>
    public static byte[] NormaliseArt(byte[] data, int maxSize = 1000)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var src = Image.FromStream(input);

            int side = Math.Min(src.Width, src.Height);
            int target = Math.Min(side, maxSize);
            var cropX = (src.Width - side) / 2;
            var cropY = (src.Height - side) / 2;

            using var square = new Bitmap(target, target, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(square))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, target, target),
                            new Rectangle(cropX, cropY, side, side), GraphicsUnit.Pixel);
            }

            var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
            using var outStream = new MemoryStream();
            square.Save(outStream, jpeg, p);
            return outStream.ToArray();
        }
        catch
        {
            return data;
        }
    }

    public static Image? ImageFromBytes(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;
        try { return Image.FromStream(new MemoryStream(data)); }
        catch { return null; }
    }
}
