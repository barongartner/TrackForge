using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>
/// One pending download. Deliberately dense: field placeholders instead of separate
/// labels, so the whole card fits in half the height a labelled grid would need.
/// </summary>
public sealed class GrabCard : CardPanel
{
    private const int CardHeight = 132;
    private const int ArtSize = 96;

    private readonly ForgeService _forge;
    private readonly VideoEntry _entry;

    private readonly PictureBox _art = new();
    private readonly Label _source = new();
    private readonly ComboBox _matches = new();
    private readonly FlatButton _lookup = new();
    private readonly FlatButton _artButton = new();
    private readonly FlatButton _grab = new();
    private readonly FlatButton _remove = new();
    private readonly Label _status = new();
    private readonly FlatProgress _progress = new();

    private readonly Dictionary<string, FlatTextBox> _fields = new(StringComparer.OrdinalIgnoreCase);
    private List<MatchCandidate> _candidates = new();
    private MatchCandidate? _merged;
    private byte[]? _artBytes;
    private string? _artUrl;
    private bool _suppressMatchEvent;

    public Track Meta { get; } = new();
    public event Action<GrabCard>? RemoveRequested;

    public GrabCard(ForgeService forge, VideoEntry entry)
    {
        _forge = forge;
        _entry = entry;

        var (artist, title) = entry.Guess();
        Meta.Title = artist.Length > 0 ? title : entry.RawTitle;
        Meta.Artist = artist;
        Meta.Album = entry.YtAlbum;
        Meta.Year = entry.YtYear;
        Meta.DurationSeconds = entry.DurationSeconds;

        Height = CardHeight;
        Margin = new Padding(0, 0, 0, Theme.Gap);
        BackColor = Theme.Surface;

        Build();
        PushMetaToFields();
        LoadThumbnailAsync();
    }

    private void Build()
    {
        _art.Size = new Size(ArtSize, ArtSize);
        _art.Location = new Point(Theme.Pad, Theme.Pad);
        _art.SizeMode = PictureBoxSizeMode.Zoom;
        _art.BackColor = Theme.SurfaceAlt;
        _art.Cursor = Cursors.Hand;
        _art.Click += async (_, _) => await PickArtAsync();

        _artButton.Text = "Art";
        _artButton.Size = new Size(ArtSize, 20);
        _artButton.Font = Theme.Small;
        _artButton.Location = new Point(Theme.Pad, Theme.Pad + ArtSize + 4);
        _artButton.Click += async (_, _) => await PickArtAsync();

        _source.Font = Theme.Small;
        _source.ForeColor = Theme.TextFaint;
        _source.AutoEllipsis = true;
        _source.AutoSize = false;
        _source.Height = 14;
        _source.Text = $"{_entry.RawTitle}   {_entry.Uploader}   {_entry.DurationText}";

        AddField("title", "Title");
        AddField("artist", "Artist");
        AddField("album", "Album");
        AddField("albumartist", "Album artist");
        AddField("year", "Year");
        AddField("genre", "Genre");
        AddField("track", "#");
        AddField("disc", "Disc");
        AddField("bpm", "BPM");
        AddField("key", "Key");

        _matches.DropDownStyle = ComboBoxStyle.DropDownList;
        _matches.FlatStyle = FlatStyle.Flat;
        _matches.BackColor = Theme.SurfaceAlt;
        _matches.ForeColor = Theme.Text;
        _matches.Font = Theme.UI;
        _matches.Height = 22;
        _matches.Items.Add("No lookup yet");
        _matches.SelectedIndex = 0;
        _matches.SelectedIndexChanged += (_, _) => ApplySelectedMatch();

        _lookup.Text = "Look up";
        _lookup.Size = new Size(66, 22);
        _lookup.Font = Theme.Small;
        _lookup.Click += async (_, _) => await LookupAsync();

        _status.Font = Theme.Small;
        _status.ForeColor = Theme.TextDim;
        _status.AutoEllipsis = true;
        _status.AutoSize = false;
        _status.Height = 14;

        _progress.Height = 3;
        _progress.Visible = false;

        _grab.Text = "Grab";
        _grab.Primary = true;
        _grab.Size = new Size(72, 24);
        _grab.Click += (_, _) => Grab();

        _remove.Text = "Remove";
        _remove.Size = new Size(72, 20);
        _remove.Font = Theme.Small;
        _remove.Click += (_, _) => RemoveRequested?.Invoke(this);

        Controls.AddRange(new Control[]
            { _art, _artButton, _source, _matches, _lookup, _status, _progress, _grab, _remove });

        Resize += (_, _) => Relayout();
        Relayout();
    }

    private void AddField(string key, string placeholder)
    {
        var box = new FlatTextBox { Height = 24 };
        box.PlaceholderText = placeholder;
        box.Inner.TextChanged += (_, _) => PullFieldsToMeta();
        _fields[key] = box;
        Controls.Add(box);
    }

    /// <summary>
    /// Three rows to the right of the artwork. Wide fields share the leftover width
    /// proportionally so the card looks right at any window size.
    /// </summary>
    private void Relayout()
    {
        int left = Theme.Pad + ArtSize + Theme.Gap + 2;
        int rightColumn = 78;
        int right = Width - Theme.Pad - rightColumn - Theme.Gap;
        int available = right - left;
        if (available < 260) available = 260;

        _source.SetBounds(left, 8, available, 14);

        // Row 1: title | artist | album
        int y = 26;
        int unit = available - Theme.Gap * 2;
        int wTitle = (int)(unit * 0.40), wArtist = (int)(unit * 0.30);
        int wAlbum = unit - wTitle - wArtist;
        _fields["title"].SetBounds(left, y, wTitle, 24);
        _fields["artist"].SetBounds(left + wTitle + Theme.Gap, y, wArtist, 24);
        _fields["album"].SetBounds(left + wTitle + wArtist + Theme.Gap * 2, y, wAlbum, 24);

        // Row 2: album artist | year | genre | # | disc | bpm | key
        y = 54;
        int fixedWidth = 46 + 40 + 34 + 40 + 46;         // year, #, disc, bpm, key
        int gaps = Theme.Gap * 6;
        int wAlbumArtist = (int)((available - fixedWidth - gaps) * 0.55);
        int wGenre = available - fixedWidth - gaps - wAlbumArtist;
        if (wAlbumArtist < 70) { wAlbumArtist = 70; wGenre = Math.Max(60, available - fixedWidth - gaps - 70); }

        int x = left;
        void Place(string key, int w) { _fields[key].SetBounds(x, y, w, 24); x += w + Theme.Gap; }
        Place("albumartist", wAlbumArtist);
        Place("genre", wGenre);
        Place("year", 46);
        Place("track", 40);
        Place("disc", 34);
        Place("bpm", 40);
        Place("key", 46);

        // Row 3: match dropdown | look up | status
        y = 84;
        int wMatches = Math.Max(150, (int)(available * 0.46));
        _matches.SetBounds(left, y, wMatches, 22);
        _lookup.Location = new Point(left + wMatches + Theme.Gap, y);
        _status.SetBounds(left, y + 26, available, 14);

        _progress.SetBounds(left, CardHeight - 7, available, 3);

        // Right column: grab over remove
        int rx = Width - Theme.Pad - rightColumn;
        _grab.SetBounds(rx, 26, rightColumn, 24);
        _remove.SetBounds(rx, 54, rightColumn, 20);
    }

    private void PushMetaToFields()
    {
        void Set(string key, string value)
        {
            if (_fields.TryGetValue(key, out var box) && box.Text != value) box.Text = value ?? "";
        }

        Set("title", Meta.Title);
        Set("artist", Meta.Artist);
        Set("album", Meta.Album);
        Set("albumartist", Meta.AlbumArtist);
        Set("year", Meta.Year);
        Set("genre", Meta.Genre);
        Set("track", Meta.TrackNumber);
        Set("disc", Meta.DiscNumber);
        Set("bpm", Meta.Bpm);
        Set("key", Meta.MusicalKey);
    }

    private void PullFieldsToMeta()
    {
        string Get(string key) => _fields.TryGetValue(key, out var b) ? b.Text.Trim() : "";

        Meta.Title = Get("title");
        Meta.Artist = Get("artist");
        Meta.Album = Get("album");
        Meta.AlbumArtist = Get("albumartist");
        Meta.Year = Get("year");
        Meta.Genre = Get("genre");
        Meta.TrackNumber = Get("track");
        Meta.DiscNumber = Get("disc");
        Meta.Bpm = Get("bpm");
        Meta.MusicalKey = Get("key");
    }

    // ------------------------------------------------------------- lookup

    private async void LoadThumbnailAsync()
    {
        if (string.IsNullOrWhiteSpace(_entry.ThumbnailUrl)) return;
        var bytes = await _forge.Metadata.DownloadArtAsync(_entry.ThumbnailUrl);
        if (bytes is null || IsDisposed) return;
        SetArt(bytes, null, keepAsFallback: true);
    }

    public async Task LookupAsync()
    {
        _lookup.Enabled = false;
        SetStatus("Looking up...", Theme.TextDim);
        try
        {
            PullFieldsToMeta();
            _candidates = await _forge.Metadata.LookupAsync(
                Meta.Artist, Meta.Title, _entry.DurationSeconds, deep: true);

            _suppressMatchEvent = true;
            _matches.Items.Clear();
            if (_candidates.Count == 0)
            {
                _matches.Items.Add("No matches found");
                _matches.SelectedIndex = 0;
                _suppressMatchEvent = false;
                _merged = null;
                SetStatus("Nothing found online. Type the tags in by hand.", Theme.Warn);
                return;
            }

            // The merged result is the default: one press fills every field it can.
            // The individual sources stay in the list underneath for overriding.
            _merged = MetadataClient.Merge(_candidates);

            _matches.Items.Add(_merged is null
                ? "Best match"
                : $"Best of all sources ({_merged.SourceLabel})");
            foreach (var c in _candidates) _matches.Items.Add(c.Display);
            _matches.SelectedIndex = 0;
            _suppressMatchEvent = false;
            ApplySelectedMatch();
        }
        catch (Exception ex)
        {
            SetStatus("Lookup failed: " + ex.Message, Theme.Bad);
        }
        finally { _lookup.Enabled = true; }
    }

    private async void ApplySelectedMatch()
    {
        if (_suppressMatchEvent || _matches.SelectedIndex < 0) return;

        // Index 0 is the merged result; the rest map onto _candidates.
        MatchCandidate? chosen;
        if (_matches.SelectedIndex == 0)
        {
            chosen = _merged;
        }
        else
        {
            int i = _matches.SelectedIndex - 1;
            if (i >= _candidates.Count) return;
            chosen = _candidates[i];
        }
        if (chosen is null) return;

        chosen.ApplyTo(Meta, overwrite: true, _forge.Config.ForceTitleCase);

        // Picking a specific source shouldn't cost you the fields it happens to lack,
        // so top up anything still blank from the merged result.
        if (_merged is not null && !ReferenceEquals(chosen, _merged))
            _merged.ApplyTo(Meta, overwrite: false, _forge.Config.ForceTitleCase);

        PushMetaToFields();

        var filled = CountFilled();
        bool duplicate = _forge.AlreadyHave(Meta.Artist, Meta.Title);
        SetStatus(duplicate
                ? $"{chosen.SourceLabel} ({chosen.Score:0}) - {filled}/8 fields - already in your library"
                : $"{chosen.SourceLabel} ({chosen.Score:0}) - {filled}/8 fields filled",
            duplicate ? Theme.Warn : Theme.Good);

        var artUrl = chosen.ArtUrl.Length > 0 ? chosen.ArtUrl : _merged?.ArtUrl ?? "";
        if (_forge.Config.AutoArt && artUrl.Length > 0)
        {
            var bytes = await _forge.Metadata.DownloadArtAsync(artUrl);
            if (bytes is not null && !IsDisposed) SetArt(bytes, artUrl);
        }
    }

    private int CountFilled() => new[]
    {
        Meta.Title, Meta.Artist, Meta.Album, Meta.AlbumArtist,
        Meta.Year, Meta.Genre, Meta.TrackNumber, Meta.DiscNumber,
    }.Count(v => !string.IsNullOrWhiteSpace(v));

    private async Task PickArtAsync()
    {
        PullFieldsToMeta();
        _artButton.Enabled = false;
        SetStatus("Searching for cover art...", Theme.TextDim);
        try
        {
            var artist = Meta.AlbumArtist.Length > 0 ? Meta.AlbumArtist : Meta.Artist;
            var options = await _forge.Metadata.FindArtAsync(
                artist, Meta.Album.Length > 0 ? Meta.Album : Meta.Title);

            if (options.Count == 0)
            {
                SetStatus("No cover art found for that album.", Theme.Warn);
                return;
            }

            using var dialog = new ArtPickerDialog(_forge, options);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedBytes is not null)
            {
                SetArt(dialog.SelectedBytes, dialog.SelectedUrl);
                SetStatus("Cover art set.", Theme.Good);
            }
            else SetStatus("", Theme.TextDim);
        }
        finally { _artButton.Enabled = true; }
    }

    private void SetArt(byte[] bytes, string? url, bool keepAsFallback = false)
    {
        // Don't let a late video thumbnail overwrite a real cover already chosen.
        if (keepAsFallback && _artUrl is not null) return;

        _artBytes = bytes;
        _artUrl = url;
        var image = TagService.ImageFromBytes(bytes);
        if (image is null) return;
        _art.Image?.Dispose();
        _art.Image = image;
    }

    private void SetStatus(string text, Color colour)
    {
        _status.Text = text;
        _status.ForeColor = colour;
    }

    // --------------------------------------------------------------- grab

    public void Grab()
    {
        PullFieldsToMeta();
        if (string.IsNullOrWhiteSpace(Meta.Title))
        {
            SetStatus("Give it a title first.", Theme.Bad);
            return;
        }

        _grab.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;

        var request = new ForgeService.GrabRequest(_entry.Url, Meta.Clone(), _artUrl, _artBytes, null);
        var job = _forge.EnqueueGrab(request);

        void OnChanged(Job changed)
        {
            if (changed.Id != job.Id || IsDisposed) return;
            try
            {
                BeginInvoke(() =>
                {
                    _progress.Value = changed.Progress;
                    _progress.BarColour = changed.State switch
                    {
                        JobState.Done => Theme.Good,
                        JobState.Failed => Theme.Bad,
                        _ => Theme.Accent
                    };
                    SetStatus(changed.Message, changed.State switch
                    {
                        JobState.Done => Theme.Good,
                        JobState.Failed => Theme.Bad,
                        _ => Theme.TextDim
                    });

                    if (changed.State is JobState.Done)
                    {
                        _grab.Text = "Done";
                        BorderColour = Theme.Good;
                        Invalidate();
                        _forge.Jobs.JobChanged -= OnChanged;
                    }
                    else if (changed.State is JobState.Failed or JobState.Cancelled)
                    {
                        _grab.Enabled = true;
                        _grab.Text = "Retry";
                        BorderColour = Theme.Bad;
                        Invalidate();
                        _forge.Jobs.JobChanged -= OnChanged;
                    }
                });
            }
            catch (ObjectDisposedException) { _forge.Jobs.JobChanged -= OnChanged; }
            catch (InvalidOperationException) { }
        }

        _forge.Jobs.JobChanged += OnChanged;
    }

    public bool IsGrabbed => !_grab.Enabled && _grab.Text == "Done";

    internal string StatusForTesting => _status.Text;
    internal string GrabButtonForTesting => _grab.Text;
    internal double ProgressForTesting => _progress.Value;
}
