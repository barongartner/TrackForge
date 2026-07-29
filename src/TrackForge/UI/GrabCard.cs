using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>
/// One pending download. Three columns: artwork, the editable tag grid, and the two
/// actions. Field placeholders stand in for labels so the whole card stays compact.
/// </summary>
public sealed class GrabCard : CardPanel
{
    private const int CardHeight = 136;
    private const int ArtColumn = Theme.GrabArtSize;   // 88
    private const int ActionColumn = 76;

    private readonly ForgeService _forge;
    private readonly VideoEntry _entry;

    private readonly WaveMark _art = new();
    private readonly FlatButton _artButton = new();
    private readonly Label _source = new();
    private readonly ComboBox _matches = new();
    private readonly FlatButton _lookup = new();
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
    private bool _pushingFields;

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
        _art.Size = new Size(ArtColumn, ArtColumn);
        _art.Location = new Point(Theme.Pad, Theme.Pad);
        _art.Cursor = Cursors.Hand;
        _art.Click += async (_, _) => await PickArtAsync();

        _artButton.Text = "Change art";
        _artButton.Font = Theme.Secondary;
        _artButton.Size = new Size(ArtColumn, 20);
        _artButton.Location = new Point(Theme.Pad, Theme.Pad + ArtColumn + 4);
        _artButton.Click += async (_, _) => await PickArtAsync();

        _source.Font = Theme.Secondary;
        _source.ForeColor = Theme.TextFainter;
        _source.AutoEllipsis = true;
        _source.AutoSize = false;
        _source.Height = 14;
        _source.Text = string.Join("  ·  ", new[] { _entry.RawTitle, _entry.Uploader, _entry.DurationText }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        foreach (var (key, placeholder, mono) in new (string, string, bool)[]
        {
            ("title", "Title", false), ("artist", "Artist", false), ("album", "Album", false),
            ("albumartist", "Album artist", false), ("genre", "Genre", false),
            ("year", "Year", true), ("track", "#", true), ("disc", "Disc", true),
            ("bpm", "BPM", true), ("key", "Key", true),
        })
        {
            var box = new FlatTextBox(monospace: mono) { Height = Theme.FieldHeight };
            box.PlaceholderText = placeholder;
            box.Inner.TextChanged += (_, _) => PullFieldsToMeta();
            _fields[key] = box;
            Controls.Add(box);
        }

        _matches.DropDownStyle = ComboBoxStyle.DropDownList;
        _matches.FlatStyle = FlatStyle.Flat;
        _matches.BackColor = Theme.SurfaceAlt;
        _matches.ForeColor = Theme.Text;
        _matches.Font = Theme.Body;
        _matches.Height = 22;
        _matches.Items.Add("No lookup yet");
        _matches.SelectedIndex = 0;
        _matches.SelectedIndexChanged += (_, _) => ApplySelectedMatch();

        _lookup.Text = "Look up";
        _lookup.Font = Theme.Secondary;
        _lookup.Size = new Size(64, 22);
        _lookup.Click += async (_, _) => await LookupAsync();

        _status.Font = Theme.Secondary;
        _status.ForeColor = Theme.TextMuted;
        _status.AutoEllipsis = true;
        _status.AutoSize = false;
        _status.Height = 14;

        _progress.Height = 3;
        _progress.Visible = false;

        _grab.Text = "Grab";
        _grab.Primary = true;
        _grab.Size = new Size(ActionColumn, Theme.PrimaryButtonHeight);
        _grab.Click += (_, _) => Grab();

        _remove.Text = "Remove";
        _remove.Font = Theme.Secondary;
        _remove.Size = new Size(ActionColumn, 20);
        _remove.Click += (_, _) => RemoveRequested?.Invoke(this);

        Controls.AddRange(new Control[]
            { _art, _artButton, _source, _matches, _lookup, _status, _progress, _grab, _remove });

        Resize += (_, _) => Relayout();
        Relayout();
    }

    /// <summary>
    /// Two proportional field rows to the right of the artwork, matching the spec's
    /// grid: 2fr/1.35fr/1.35fr, then 1.5fr/1.1fr and fixed numeric columns.
    /// </summary>
    private void Relayout()
    {
        int left = Theme.Pad + ArtColumn + 8;
        int right = Width - Theme.Pad - ActionColumn - 8;
        int available = Math.Max(320, right - left);

        _source.SetBounds(left, Theme.Pad, available, 14);

        // Row 1: Title | Artist | Album
        int y = 30;
        int span = available - Theme.Gap * 2;
        int wTitle = (int)(span * 2.0 / 4.7);
        int wArtist = (int)(span * 1.35 / 4.7);
        int wAlbum = span - wTitle - wArtist;
        _fields["title"].SetBounds(left, y, wTitle, Theme.FieldHeight);
        _fields["artist"].SetBounds(left + wTitle + Theme.Gap, y, wArtist, Theme.FieldHeight);
        _fields["album"].SetBounds(left + wTitle + wArtist + Theme.Gap * 2, y, wAlbum, Theme.FieldHeight);

        // Row 2: Album artist | Genre | Year | # | Disc | BPM | Key
        y = 60;
        const int wYear = 52, wTrack = 40, wDisc = 40, wBpm = 48, wKey = 54;
        int fixedWidth = wYear + wTrack + wDisc + wBpm + wKey;
        int flexible = available - fixedWidth - Theme.Gap * 6;
        int wAlbumArtist = Math.Max(80, (int)(flexible * 1.5 / 2.6));
        int wGenre = Math.Max(64, flexible - wAlbumArtist);

        int x = left;
        void Place(string key, int w)
        {
            _fields[key].SetBounds(x, y, w, Theme.FieldHeight);
            x += w + Theme.Gap;
        }
        Place("albumartist", wAlbumArtist);
        Place("genre", wGenre);
        Place("year", wYear);
        Place("track", wTrack);
        Place("disc", wDisc);
        Place("bpm", wBpm);
        Place("key", wKey);

        // Match row, then the status line under it.
        y = 90;
        int wMatches = Math.Max(180, (int)(available * 0.42));
        _matches.SetBounds(left, y, wMatches, 22);
        _lookup.Location = new Point(left + wMatches + Theme.Gap, y);
        _status.SetBounds(_lookup.Right + 8, y + 4, Math.Max(80, right - _lookup.Right - 8), 14);

        _progress.SetBounds(left, CardHeight - 7, available, 3);

        int rx = Width - Theme.Pad - ActionColumn;
        _grab.SetBounds(rx, Theme.Pad, ActionColumn, Theme.PrimaryButtonHeight);
        _remove.SetBounds(rx, Theme.Pad + Theme.PrimaryButtonHeight + 4, ActionColumn, 20);
    }

    /// <summary>
    /// Writing a box fires TextChanged, which pulls every box back into Meta. Without
    /// the guard the first assignment reads the still-empty boxes and wipes the values
    /// the lookup just produced, so only one field per press ever survived.
    /// </summary>
    private void PushMetaToFields()
    {
        _pushingFields = true;
        try
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
        finally { _pushingFields = false; }
    }

    private void PullFieldsToMeta()
    {
        if (_pushingFields) return;

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
        SetStatus("Looking up...", Theme.TextMuted);
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

        MatchCandidate? chosen;
        if (_matches.SelectedIndex == 0) chosen = _merged;
        else
        {
            int i = _matches.SelectedIndex - 1;
            if (i >= _candidates.Count) return;
            chosen = _candidates[i];
        }
        if (chosen is null) return;

        chosen.ApplyTo(Meta, overwrite: true, _forge.Config.ForceTitleCase);
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
        SetStatus("Searching for cover art...", Theme.TextMuted);
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
            else SetStatus("", Theme.TextMuted);
        }
        finally { _artButton.Enabled = true; }
    }

    private void SetArt(byte[] bytes, string? url, bool keepAsFallback = false)
    {
        if (keepAsFallback && _artUrl is not null) return;

        _artBytes = bytes;
        _artUrl = url;
        var image = TagService.ImageFromBytes(bytes);
        if (image is null) return;
        _art.Image?.Dispose();
        _art.Image = image;
        _art.Invalidate();
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
                        _ => Theme.TextMuted
                    });

                    if (changed.State is JobState.Done)
                    {
                        _grab.Text = "Done";
                        BorderColour = Theme.DoneBorder;
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

    internal Dictionary<string, string> FieldValuesForTesting =>
        _fields.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim());

    internal string StatusForTesting => _status.Text;
    internal string GrabButtonForTesting => _grab.Text;
    internal double ProgressForTesting => _progress.Value;
}
