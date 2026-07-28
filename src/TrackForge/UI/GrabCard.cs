using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>One pending download: editable tags, cover art, and a Grab button.</summary>
public sealed class GrabCard : CardPanel
{
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

    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);
    private List<MatchCandidate> _candidates = new();
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

        Height = 232;
        Margin = new Padding(0, 0, 0, 12);
        BackColor = Theme.Surface;

        BuildLayout();
        LoadThumbnailAsync();
    }

    private void BuildLayout()
    {
        _art.Size = new Size(150, 150);
        _art.Location = new Point(16, 16);
        _art.SizeMode = PictureBoxSizeMode.Zoom;
        _art.BackColor = Theme.SurfaceAlt;

        _artButton.Text = "Change art";
        _artButton.Size = new Size(150, 26);
        _artButton.Location = new Point(16, 172);
        _artButton.Click += async (_, _) => await PickArtAsync();

        _source.Location = new Point(180, 14);
        _source.Size = new Size(700, 16);
        _source.Font = Theme.Small;
        _source.ForeColor = Theme.TextFaint;
        _source.AutoEllipsis = true;
        _source.Text = $"{_entry.RawTitle}   |   {_entry.Uploader}   |   {_entry.DurationText}";

        // Two rows of editable tag fields.
        AddField("Title", "title", 180, 36, 300);
        AddField("Artist", "artist", 492, 36, 240);
        AddField("Album", "album", 744, 36, 240);

        AddField("Album artist", "albumartist", 180, 88, 190);
        AddField("Year", "year", 382, 88, 70);
        AddField("Genre", "genre", 464, 88, 140);
        AddField("Track", "track", 616, 88, 60);
        AddField("Disc", "disc", 688, 88, 56);
        AddField("BPM", "bpm", 756, 88, 62);
        AddField("Key", "key", 830, 88, 62);

        _matches.Location = new Point(180, 146);
        _matches.Size = new Size(560, 24);
        _matches.DropDownStyle = ComboBoxStyle.DropDownList;
        _matches.FlatStyle = FlatStyle.Flat;
        _matches.BackColor = Theme.SurfaceAlt;
        _matches.ForeColor = Theme.Text;
        _matches.Font = Theme.UI;
        _matches.Items.Add("No lookup yet");
        _matches.SelectedIndex = 0;
        _matches.SelectedIndexChanged += (_, _) => ApplySelectedMatch();

        _lookup.Text = "Look up";
        _lookup.Size = new Size(88, 26);
        _lookup.Location = new Point(748, 145);
        _lookup.Click += async (_, _) => await LookupAsync();

        _status.Location = new Point(180, 182);
        _status.Size = new Size(560, 16);
        _status.Font = Theme.Small;
        _status.ForeColor = Theme.TextDim;
        _status.AutoEllipsis = true;

        _progress.Location = new Point(180, 202);
        _progress.Size = new Size(560, 4);
        _progress.Visible = false;

        _grab.Text = "Grab";
        _grab.Primary = true;
        _grab.Size = new Size(96, 30);
        _grab.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _grab.Click += (_, _) => Grab();

        _remove.Text = "Remove";
        _remove.Size = new Size(96, 26);
        _remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _remove.Click += (_, _) => RemoveRequested?.Invoke(this);

        Controls.AddRange(new Control[]
            { _art, _artButton, _source, _matches, _lookup, _status, _progress, _grab, _remove });

        Resize += (_, _) => LayoutRightButtons();
        LayoutRightButtons();
        PushMetaToFields();
    }

    private void LayoutRightButtons()
    {
        _grab.Location = new Point(Width - _grab.Width - 16, 168);
        _remove.Location = new Point(Width - _remove.Width - 16, 202);

        int rightEdge = _grab.Left - 16;
        if (_matches.Left + 560 > rightEdge - 100)
        {
            _matches.Width = Math.Max(220, rightEdge - 100 - _matches.Left);
            _lookup.Left = _matches.Right + 8;
            _status.Width = _matches.Width + 96;
            _progress.Width = _matches.Width + 96;
        }
    }

    private void AddField(string caption, string key, int x, int y, int width)
    {
        var label = new Label
        {
            Text = caption,
            Location = new Point(x, y),
            Size = new Size(width, 14),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
        };

        var box = new FlatTextBox { Location = new Point(x, y + 16), Size = new Size(width, 28) };
        box.Inner.TextChanged += (_, _) => PullFieldsToMeta();

        _fields[key] = box.Inner;
        Controls.Add(label);
        Controls.Add(box);
    }

    private void PushMetaToFields()
    {
        void Set(string key, string value)
        {
            if (_fields.TryGetValue(key, out var box) && box.Text != value) box.Text = value;
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
        SetStatus("Looking up metadata...", Theme.TextDim);
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
                SetStatus("Nothing found online. Type the tags in by hand.", Theme.Warn);
                return;
            }

            foreach (var c in _candidates) _matches.Items.Add(c.Display);
            _matches.SelectedIndex = 0;
            _suppressMatchEvent = false;

            ApplySelectedMatch();
        }
        catch (Exception ex)
        {
            SetStatus("Lookup failed: " + ex.Message, Theme.Bad);
        }
        finally
        {
            _lookup.Enabled = true;
        }
    }

    private async void ApplySelectedMatch()
    {
        if (_suppressMatchEvent) return;
        if (_matches.SelectedIndex < 0 || _matches.SelectedIndex >= _candidates.Count) return;

        var chosen = _candidates[_matches.SelectedIndex];
        chosen.ApplyTo(Meta, overwrite: true, _forge.Config.ForceTitleCase);
        PushMetaToFields();

        var duplicate = _forge.AlreadyHave(Meta.Artist, Meta.Title);
        SetStatus(duplicate
            ? $"Matched {chosen.Source} ({chosen.Score:0}) - you already have this in your library"
            : $"Matched {chosen.Source} ({chosen.Score:0})",
            duplicate ? Theme.Warn : Theme.Good);

        if (_forge.Config.AutoArt && chosen.ArtUrl.Length > 0)
        {
            var bytes = await _forge.Metadata.DownloadArtAsync(chosen.ArtUrl);
            if (bytes is not null && !IsDisposed) SetArt(bytes, chosen.ArtUrl);
        }
    }

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
            else
            {
                SetStatus("", Theme.TextDim);
            }
        }
        finally { _artButton.Enabled = true; }
    }

    private void SetArt(byte[] bytes, string? url, bool keepAsFallback = false)
    {
        // A real cover already chosen should not be replaced by a video thumbnail.
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
                        _grab.Text = "Grabbed";
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

    public bool IsGrabbed => !_grab.Enabled && _grab.Text == "Grabbed";
}
