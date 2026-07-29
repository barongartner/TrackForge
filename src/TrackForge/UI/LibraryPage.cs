using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Everything already on disk: what's tagged, what isn't, and fixing it.</summary>
public sealed class LibraryPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _search = new();
    private readonly DarkListView _list = new();
    private readonly Label _count = new();
    private readonly Label _status = new();
    private readonly FlatButton _rescan = new();
    private readonly FlatButton _fillAll = new();
    private readonly FlatButton _enrich = new();
    private readonly FlatButton _repair = new();
    private readonly FlatButton _analyze = new();
    private readonly FlatButton _find = new();
    private readonly List<FlatButton> _chips = new();
    private readonly List<FlatButton> _actions = new();

    private List<Track> _all = new();
    private List<Track> _shown = new();
    private string _filter = "all";
    private string _sortColumn = "";
    private bool _sortAscending = true;

    public event Action<IReadOnlyList<Track>>? SendToFindRequested;

    public LibraryPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(Theme.Pad);

        var bar = BuildToolbar();
        BuildList();

        var spacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Background };

        Controls.Add(_list);
        Controls.Add(spacer);
        Controls.Add(bar);
    }

    /// <summary>Two rows: search + chips + count + rescan, then status + bulk actions.</summary>
    private Control BuildToolbar()
    {
        var bar = new CardPanel { Dock = DockStyle.Top, Height = 72 };

        _search.Location = new Point(Theme.Pad, 8);
        _search.Size = new Size(210, Theme.FieldHeight);
        _search.PlaceholderText = "Search";
        _search.Inner.TextChanged += (_, _) => ApplyFilter();

        var chipDefs = new (string key, string label)[]
        {
            ("all", "All"), ("art", "No art"), ("year", "No year"), ("genre", "No genre"),
            ("album", "No album"), ("bpm", "No BPM"), ("incomplete", "Incomplete"),
        };

        int x = _search.Right + 8;
        foreach (var (key, label) in chipDefs)
        {
            var chip = new FlatButton
            {
                Text = label,
                Font = Theme.Secondary,
                Chip = true,
                Primary = key == "all",
                Size = new Size(TextRenderer.MeasureText(label, Theme.Secondary).Width + 18, Theme.FieldHeight),
                Location = new Point(x, 8),
            };
            chip.Click += (_, _) =>
            {
                _filter = key;
                foreach (var c in _chips) { c.Primary = ReferenceEquals(c, chip); c.Invalidate(); }
                ApplyFilter();
            };
            _chips.Add(chip);
            bar.Controls.Add(chip);
            x += chip.Width + 3;
        }

        _count.Font = Theme.NumericSmall;
        _count.ForeColor = Theme.TextCount;
        _count.AutoSize = false;
        _count.TextAlign = ContentAlignment.MiddleRight;
        _count.Size = new Size(230, 16);
        _count.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _rescan.Text = "Rescan";
        _rescan.Size = new Size(62, Theme.FieldHeight);
        _rescan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _rescan.Click += async (_, _) => await RescanAsync();

        _status.Location = new Point(Theme.Pad, 42);
        _status.Size = new Size(360, 16);
        _status.Font = Theme.Secondary;
        _status.ForeColor = Theme.TextMuted;
        _status.AutoEllipsis = true;

        var actionDefs = new (FlatButton b, string text, int w, bool primary, Action click)[]
        {
            (_fillAll, "Fill every track", 98, true,  RunFillAll),
            (_enrich,  "Fill selected",    82, false, RunEnrich),
            (_repair,  "Repair tags",      74, false, RunRepair),
            (_analyze, "BPM + key",        68, false, RunAnalyze),
            (_find,    "Find on YouTube",  94, false, RunFind),
        };

        foreach (var (b, text, w, primary, click) in actionDefs)
        {
            b.Text = text;
            b.Font = primary ? Theme.Emphasis : Theme.Secondary;
            b.Size = new Size(w, Theme.FieldHeight);
            b.Primary = primary;
            b.Enabled = ReferenceEquals(b, _fillAll) || ReferenceEquals(b, _repair);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            b.Click += (_, _) => click();
            _actions.Add(b);
            bar.Controls.Add(b);
        }

        bar.Controls.AddRange(new Control[] { _search, _count, _rescan, _status });
        bar.Resize += (_, _) =>
        {
            _rescan.Location = new Point(bar.Width - _rescan.Width - Theme.Pad, 8);
            _count.Location = new Point(_rescan.Left - _count.Width - 8, 12);

            int right = bar.Width - Theme.Pad;
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                right -= _actions[i].Width;
                _actions[i].Location = new Point(right, 40);
                right -= Theme.Gap;
            }
        };
        return bar;
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;

        var columns = new (string title, int width)[]
        {
            ("Title", 230), ("Artist", 155), ("Album", 175), ("Year", 52),
            ("Genre", 110), ("#", 38), ("BPM", 52), ("Key", 68),
            ("Len", 52), ("Missing", 160),
        };
        foreach (var (title, width) in columns) _list.Columns.Add(title, width);

        // Consolas on every numeric column - this is what lines the table up.
        foreach (var i in new[] { 3, 5, 6, 7, 8 }) _list.NumericColumns.Add(i);
        _list.DimColumns.Add(3);
        _list.DimColumns.Add(8);

        _list.ColourFor = (item, column) =>
        {
            var track = item.Tag as Track;
            return column switch
            {
                1 => Theme.TextStrong,
                2 => string.IsNullOrWhiteSpace(track?.Album) ? Theme.TextFainter : Theme.Text,
                6 when track is not null && string.IsNullOrWhiteSpace(track.Bpm) && track.DjayBpm.HasValue
                    => Theme.TextFaint,   // came from djay, not this file's tags
                9 => item.SubItems[9].Text == "complete" ? Theme.Good : Theme.Warn,
                _ => null,
            };
        };

        _list.SelectedIndexChanged += (_, _) => UpdateSelectionState();
        _list.DoubleClick += (_, _) => OpenEditor();
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenEditor(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                foreach (ListViewItem item in _list.Items) item.Selected = true;
                e.Handled = true;
            }
        };
    }

    // -------------------------------------------------------------- data

    public void FocusSearch() => _search.Inner.Focus();

    internal void StressForTesting()
    {
        foreach (var filter in new[] { "art", "year", "incomplete", "all" })
        {
            _filter = filter;
            ApplyFilter();
        }

        _search.Text = "a";
        ApplyFilter();
        _search.Text = "";
        ApplyFilter();

        if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
            if (_list.Items.Count > 1) _list.Items[^1].Selected = true;
            var track = _list.Items[0].Tag as Track;
            if (track is not null)
            {
                var bytes = TagService.ReadArt(track.Path);
                using var image = TagService.ImageFromBytes(bytes);
            }
            _list.SelectedItems.Clear();
        }
    }

    public async Task RescanAsync()
    {
        _rescan.Enabled = false;
        _rescan.Text = "...";
        _status.Text = "Scanning " + _forge.Config.LibraryFolder;
        _status.ForeColor = Theme.TextMuted;

        try
        {
            await _forge.RescanLibraryAsync(new Progress<string>(s => _status.Text = s));
            RefreshFromService();

            _status.Text = _all.Count == 0
                ? (Directory.Exists(_forge.Config.LibraryFolder)
                    ? "No audio files there. Check the path in Settings."
                    : "Folder not found: " + _forge.Config.LibraryFolder)
                : "Scanned " + _forge.Config.LibraryFolder;
        }
        catch (Exception ex)
        {
            _status.Text = "Scan failed: " + ex.Message;
            _status.ForeColor = Theme.Bad;
        }
        finally
        {
            _rescan.Enabled = true;
            _rescan.Text = "Rescan";
        }
    }

    public void RefreshFromService()
    {
        _all = _forge.Library.Tracks.ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _search.Text.Trim().ToLowerInvariant();

        IEnumerable<Track> filtered = _filter switch
        {
            "art" => _all.Where(t => !t.HasArt),
            "year" => _all.Where(t => string.IsNullOrWhiteSpace(t.Year)),
            "genre" => _all.Where(t => string.IsNullOrWhiteSpace(t.Genre)),
            "album" => _all.Where(t => string.IsNullOrWhiteSpace(t.Album)),
            "bpm" => _all.Where(t => string.IsNullOrWhiteSpace(t.DisplayBpm)),
            "incomplete" => _all.Where(t => !t.IsComplete),
            _ => _all,
        };

        if (query.Length > 0)
        {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filtered = filtered.Where(t => terms.All(term => t.SearchBlob.Contains(term)));
        }

        _shown = filtered.ToList();
        if (_sortColumn.Length > 0) SortShown();
        Render();
    }

    private static string OrDash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private void Render()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var t in _shown)
        {
            _list.Items.Add(new ListViewItem(new[]
            {
                t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path),
                OrDash(t.Artist),
                OrDash(t.Album),
                OrDash(t.Year),
                OrDash(t.Genre),
                PadTrack(t.TrackNumber),
                OrDash(t.DisplayBpm),
                t.Camelot.Length > 0 ? $"{t.MusicalKey} {t.Camelot}" : OrDash(t.MusicalKey),
                OrDash(t.DurationText),
                t.IsComplete ? "complete" : t.MissingText,
            })
            { Tag = t });
        }

        _list.EndUpdate();

        int incomplete = _all.Count(t => !t.IsComplete);
        _count.Text = $"{_shown.Count} shown / {_all.Count} total / {incomplete} need work";
        UpdateSelectionState();
    }

    /// <summary>Zero-padded so the column reads as a column, not ragged text.</summary>
    private static string PadTrack(string? raw)
    {
        var first = (raw ?? "").Split('/')[0].Trim();
        return int.TryParse(first, out var n) && n > 0 ? n.ToString("00") : "—";
    }

    private void SortBy(int column)
    {
        var keys = new[] { "title", "artist", "album", "year", "genre", "track", "bpm", "key", "length", "missing" };
        if (column < 0 || column >= keys.Length) return;

        _sortAscending = keys[column] != _sortColumn || !_sortAscending;
        _sortColumn = keys[column];
        _list.SortColumn = column;
        _list.SortAscending = _sortAscending;
        SortShown();
        Render();
    }

    private void SortShown()
    {
        Func<Track, object> selector = _sortColumn switch
        {
            "artist" => t => t.Artist ?? "",
            "album" => t => t.Album ?? "",
            "year" => t => int.TryParse(t.Year, out var y) ? y : 0,
            "genre" => t => t.Genre ?? "",
            "track" => t => int.TryParse((t.TrackNumber ?? "").Split('/')[0], out var n) ? n : 0,
            "bpm" => t => double.TryParse(t.DisplayBpm, out var b) ? b : 0,
            "key" => t => t.Camelot ?? "",
            "length" => t => t.DurationSeconds,
            "missing" => t => t.MissingFields().Count(),
            _ => t => t.Title ?? "",
        };

        var comparer = Comparer<object>.Create(static (a, b) => (a, b) switch
        {
            (string x, string y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase),
            (IComparable x, _) => x.CompareTo(b),
            _ => 0,
        });

        _shown = (_sortAscending
            ? _shown.OrderBy(selector, comparer)
            : _shown.OrderByDescending(selector, comparer)).ToList();
    }

    private List<Track> Selected() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Track>().ToList();

    private void UpdateSelectionState()
    {
        int n = _list.SelectedItems.Count;
        foreach (var b in new[] { _enrich, _analyze, _find }) { b.Enabled = n > 0; b.Invalidate(); }

        _enrich.Text = n > 0 ? $"Fill {n} selected" : "Fill selected";
        _enrich.Invalidate();

        _fillAll.Enabled = _shown.Count > 0;
        _fillAll.Text = _shown.Count == _all.Count ? "Fill every track" : $"Fill all {_shown.Count} shown";
        _fillAll.Invalidate();

        _repair.Enabled = _shown.Count > 0 || n > 0;
        _repair.Text = n > 0 ? $"Repair {n}" : "Repair tags";
        _repair.Invalidate();

        if (n == 1)
        {
            var t = Selected().FirstOrDefault();
            _status.Text = $"1 selected · {t?.Title}";
        }
        else if (n > 1) _status.Text = $"{n} selected";
    }

    // ------------------------------------------------------------ actions

    private void RunEnrich() => Enrich(Selected());

    private void RunFillAll()
    {
        if (_shown.Count == 0)
        {
            _status.Text = "Nothing shown to fill.";
            _status.ForeColor = Theme.Warn;
            return;
        }
        Enrich(_shown.ToList());
    }

    private void Enrich(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;

        using var dialog = new EnrichOptionsDialog(tracks.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _forge.EnqueueEnrich(tracks, dialog.Options);
        _status.Text = $"Filling tags on {tracks.Count} - see Jobs";
        _status.ForeColor = Theme.TextMuted;
    }

    private void RunRepair()
    {
        var tracks = Selected();
        if (tracks.Count == 0) tracks = _shown.ToList();
        if (tracks.Count == 0)
        {
            _status.Text = "Nothing to repair.";
            _status.ForeColor = Theme.Warn;
            return;
        }

        var answer = MessageBox.Show(this,
            $"Rewrite tags on {tracks.Count} file(s) as ID3v2.3?\n\n" +
            "This fixes genres showing as numbers and cover art showing black in " +
            "Windows. Nothing is downloaded and no values change - only the tag " +
            "format is rewritten.\n\n" +
            "Files open in another player will be skipped and reported.",
            "Repair tags", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (answer != DialogResult.OK) return;

        _forge.EnqueueRetag(tracks);
        _status.Text = $"Repairing {tracks.Count} - see Jobs";
        _status.ForeColor = Theme.TextMuted;
    }

    private void RunAnalyze()
    {
        var tracks = Selected();
        if (tracks.Count == 0) return;
        _forge.EnqueueAnalyze(tracks);
        _status.Text = $"Analysing {tracks.Count} - see Jobs";
        _status.ForeColor = Theme.TextMuted;
    }

    private void RunFind()
    {
        var tracks = Selected();
        if (tracks.Count > 0) SendToFindRequested?.Invoke(tracks);
    }

    private void OpenEditor()
    {
        var tracks = Selected();
        if (tracks.Count != 1) return;

        using var editor = new TagEditorDialog(_forge, tracks[0]);
        if (editor.ShowDialog(this) == DialogResult.OK) ApplyFilter();
    }
}
