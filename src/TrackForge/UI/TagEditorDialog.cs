using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Full tag editor for one library file, with online lookup and art picking.</summary>
public sealed class TagEditorDialog : Form
{
    private readonly ForgeService _forge;
    private readonly Track _track;

    private readonly PictureBox _art = new();
    private readonly ComboBox _matches = new();
    private readonly Label _status = new();
    private readonly CheckBox _rename = new();
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly Panel _body = new();

    private List<MatchCandidate> _candidates = new();
    private byte[]? _newArt;
    private bool _suppressMatchEvent;

    public TagEditorDialog(ForgeService forge, Track track)
    {
        _forge = forge;
        _track = track;

        Text = "Edit tags  -  " + track.FileName;
        Size = new Size(880, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UI;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        BuildLayout();
        LoadExistingArt();
        PushToFields();
    }

    private void BuildLayout()
    {
        _body.Dock = DockStyle.Fill;
        _body.BackColor = Theme.Background;
        _body.Padding = new Padding(18);

        _art.Size = new Size(210, 210);
        _art.Location = new Point(20, 20);
        _art.SizeMode = PictureBoxSizeMode.Zoom;
        _art.BackColor = Theme.SurfaceAlt;

        var artButton = new FlatButton { Text = "Find cover art", Size = new Size(210, 30), Location = new Point(20, 238) };
        artButton.Click += async (_, _) => await PickArtAsync();

        var analyzeButton = new FlatButton { Text = "Analyse BPM + key", Size = new Size(210, 30), Location = new Point(20, 274) };
        analyzeButton.Click += async (_, _) => await AnalyzeAsync();

        var fileInfo = new Label
        {
            Location = new Point(20, 314),
            Size = new Size(210, 120),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
            Text = $"{_track.DurationText}\n{_track.Bitrate} kbps\n" +
                   $"{_track.SizeBytes / 1024.0 / 1024.0:0.0} MB\n\n{_track.FileName}",
        };

        int col1 = 254, col2 = 560;
        AddField("Title", "title", col1, 20, 290);
        AddField("Artist", "artist", col1, 76, 290);
        AddField("Album", "album", col1, 132, 290);
        AddField("Album artist", "albumartist", col1, 188, 290);
        AddField("Composer", "composer", col1, 244, 290);
        AddField("Comment", "comment", col1, 300, 290);

        AddField("Year", "year", col2, 20, 78);
        AddField("Genre", "genre", col2, 76, 190);
        AddField("Track", "track", col2, 132, 78);
        AddField("Disc", "disc", col2 + 96, 132, 78);
        AddField("BPM", "bpm", col2, 188, 78);
        AddField("Key", "key", col2 + 96, 188, 94);
        AddField("ISRC", "isrc", col2, 244, 190);
        AddField("Publisher", "publisher", col2, 300, 190);

        _matches.Location = new Point(col1, 366);
        _matches.Size = new Size(496, 24);
        _matches.DropDownStyle = ComboBoxStyle.DropDownList;
        _matches.FlatStyle = FlatStyle.Flat;
        _matches.BackColor = Theme.SurfaceAlt;
        _matches.ForeColor = Theme.Text;
        _matches.Items.Add("No lookup yet");
        _matches.SelectedIndex = 0;
        _matches.SelectedIndexChanged += (_, _) => ApplySelectedMatch();

        var lookupButton = new FlatButton { Text = "Look up online", Size = new Size(126, 30), Location = new Point(col1, 398) };
        lookupButton.Click += async (_, _) => await LookupAsync();

        _status.Location = new Point(col1 + 136, 404);
        _status.Size = new Size(360, 18);
        _status.Font = Theme.Small;
        _status.ForeColor = Theme.TextDim;
        _status.AutoEllipsis = true;

        _body.Controls.AddRange(new Control[]
            { _art, artButton, analyzeButton, fileInfo, _matches, lookupButton, _status });

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Surface };
        footer.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
        };

        _rename.Text = "Rename the file to match the pattern";
        _rename.Location = new Point(18, 19);
        _rename.Size = new Size(280, 20);
        _rename.ForeColor = Theme.TextDim;
        _rename.FlatStyle = FlatStyle.Flat;

        var save = new FlatButton { Text = "Save tags", Primary = true, Size = new Size(110, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        save.Click += (_, _) => Save();

        var cancel = new FlatButton { Text = "Cancel", Size = new Size(88, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        footer.Controls.AddRange(new Control[] { _rename, save, cancel });
        footer.Resize += (_, _) =>
        {
            cancel.Location = new Point(footer.Width - cancel.Width - 18, 12);
            save.Location = new Point(cancel.Left - save.Width - 8, 12);
        };

        Controls.Add(_body);
        Controls.Add(footer);
        AcceptButton = save;
        CancelButton = cancel;
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
        var box = new FlatTextBox { Location = new Point(x, y + 16), Size = new Size(width, 30) };

        _fields[key] = box.Inner;
        _body.Controls.Add(label);
        _body.Controls.Add(box);
    }

    private void LoadExistingArt()
    {
        var bytes = TagService.ReadArt(_track.Path);
        if (bytes is null) return;
        _art.Image = TagService.ImageFromBytes(bytes);
    }

    private void PushToFields()
    {
        void Set(string key, string value)
        {
            if (_fields.TryGetValue(key, out var box)) box.Text = value ?? "";
        }

        Set("title", _track.Title);
        Set("artist", _track.Artist);
        Set("album", _track.Album);
        Set("albumartist", _track.AlbumArtist);
        Set("composer", _track.Composer);
        Set("comment", _track.Comment);
        Set("year", _track.Year);
        Set("genre", _track.Genre);
        Set("track", _track.TrackNumber);
        Set("disc", _track.DiscNumber);
        Set("bpm", _track.DisplayBpm);
        Set("key", _track.MusicalKey);
        Set("isrc", _track.Isrc);
        Set("publisher", _track.Publisher);
    }

    private void PullFromFields()
    {
        string Get(string key) => _fields.TryGetValue(key, out var b) ? b.Text.Trim() : "";

        _track.Title = Get("title");
        _track.Artist = Get("artist");
        _track.Album = Get("album");
        _track.AlbumArtist = Get("albumartist");
        _track.Composer = Get("composer");
        _track.Comment = Get("comment");
        _track.Year = Get("year");
        _track.Genre = Get("genre");
        _track.TrackNumber = Get("track");
        _track.DiscNumber = Get("disc");
        _track.Bpm = Get("bpm");
        _track.MusicalKey = Get("key");
        _track.Isrc = Get("isrc");
        _track.Publisher = Get("publisher");
    }

    private async Task LookupAsync()
    {
        PullFromFields();
        _status.Text = "Looking up...";
        _status.ForeColor = Theme.TextDim;

        _candidates = await _forge.Metadata.LookupAsync(
            _track.Artist, _track.Title, _track.DurationSeconds, deep: true);

        _suppressMatchEvent = true;
        _matches.Items.Clear();

        if (_candidates.Count == 0)
        {
            _matches.Items.Add("No matches found");
            _matches.SelectedIndex = 0;
            _suppressMatchEvent = false;
            _status.Text = "Nothing found online.";
            _status.ForeColor = Theme.Warn;
            return;
        }

        foreach (var c in _candidates) _matches.Items.Add(c.Display);
        _matches.SelectedIndex = 0;
        _suppressMatchEvent = false;
        ApplySelectedMatch();
    }

    private async void ApplySelectedMatch()
    {
        if (_suppressMatchEvent) return;
        if (_matches.SelectedIndex < 0 || _matches.SelectedIndex >= _candidates.Count) return;

        var chosen = _candidates[_matches.SelectedIndex];
        PullFromFields();
        chosen.ApplyTo(_track, overwrite: true, _forge.Config.ForceTitleCase);
        PushToFields();

        _status.Text = $"Applied {chosen.Source} match ({chosen.Score:0}). Nothing is written until you save.";
        _status.ForeColor = Theme.Good;

        if (_forge.Config.AutoArt && chosen.ArtUrl.Length > 0 && !_track.HasArt)
        {
            var bytes = await _forge.Metadata.DownloadArtAsync(chosen.ArtUrl);
            if (bytes is not null && !IsDisposed)
            {
                _newArt = bytes;
                _art.Image?.Dispose();
                _art.Image = TagService.ImageFromBytes(bytes);
            }
        }
    }

    private async Task PickArtAsync()
    {
        PullFromFields();
        var artist = _track.AlbumArtist.Length > 0 ? _track.AlbumArtist : _track.Artist;
        var album = _track.Album.Length > 0 ? _track.Album : _track.Title;

        _status.Text = "Searching for cover art...";
        var options = await _forge.Metadata.FindArtAsync(artist, album);

        if (options.Count == 0)
        {
            _status.Text = "No cover art found.";
            _status.ForeColor = Theme.Warn;
            return;
        }

        using var dialog = new ArtPickerDialog(_forge, options);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedBytes is null) return;

        _newArt = dialog.SelectedBytes;
        _art.Image?.Dispose();
        _art.Image = TagService.ImageFromBytes(_newArt);
        _status.Text = "Cover art ready. Save to write it in.";
        _status.ForeColor = Theme.Good;
    }

    private async Task AnalyzeAsync()
    {
        _status.Text = "Analysing audio...";
        _status.ForeColor = Theme.TextDim;

        var analysis = await AudioAnalyzer.AnalyzeAsync(_track.Path, _forge.Downloader.FfmpegPath);
        if (analysis.Bpm is null)
        {
            _status.Text = "Could not analyse that file. Is ffmpeg installed?";
            _status.ForeColor = Theme.Warn;
            return;
        }

        if (_fields.TryGetValue("bpm", out var bpmBox))
            bpmBox.Text = Math.Round(analysis.Bpm.Value).ToString();
        if (_fields.TryGetValue("key", out var keyBox) && analysis.Key is not null)
            keyBox.Text = analysis.Key;

        _track.Camelot = analysis.Camelot ?? "";
        _status.Text = $"{analysis.Bpm:0} BPM, key {analysis.Key} ({analysis.Camelot})";
        _status.ForeColor = Theme.Good;
    }

    private void Save()
    {
        PullFromFields();
        try
        {
            TagService.Write(_track, _newArt);
            if (_rename.Checked) _forge.RenameToPattern(_track);

            var fresh = TagService.Read(_track.Path);
            fresh.DjayBpm = _track.DjayBpm;
            fresh.RelativePath = _track.RelativePath;
            CopyInto(fresh, _track);

            _forge.RaiseLibraryChanged();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not write tags",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void CopyInto(Track from, Track into)
    {
        into.Title = from.Title; into.Artist = from.Artist; into.Album = from.Album;
        into.AlbumArtist = from.AlbumArtist; into.Genre = from.Genre; into.Year = from.Year;
        into.TrackNumber = from.TrackNumber; into.DiscNumber = from.DiscNumber;
        into.Bpm = from.Bpm; into.MusicalKey = from.MusicalKey; into.Camelot = from.Camelot;
        into.Isrc = from.Isrc; into.Publisher = from.Publisher; into.Composer = from.Composer;
        into.Comment = from.Comment; into.HasArt = from.HasArt; into.Path = from.Path;
    }
}
