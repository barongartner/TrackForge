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
    private readonly FlatButton _enrich = new();
    private readonly FlatButton _analyze = new();
    private readonly FlatButton _find = new();
    private readonly FlatButton _edit = new();
    private readonly FlatButton _reveal = new();
    private readonly List<FlatButton> _filterChips = new();

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
        Padding = new Padding(18, 16, 18, 16);

        var toolbar = BuildToolbar();
        var actions = BuildActionBar();
        BuildList();

        Controls.Add(_list);
        Controls.Add(actions);
        Controls.Add(toolbar);
    }

    // ------------------------------------------------------------ chrome

    private Control BuildToolbar()
    {
        var bar = new CardPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14) };

        _search.Location = new Point(14, 14);
        _search.Size = new Size(360, 30);
        _search.PlaceholderText = "Search title, artist, album, genre...";
        _search.Inner.TextChanged += (_, _) => ApplyFilter();

        _count.Location = new Point(388, 20);
        _count.Size = new Size(260, 18);
        _count.ForeColor = Theme.TextDim;

        _rescan.Text = "Rescan";
        _rescan.Size = new Size(90, 30);
        _rescan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _rescan.Click += async (_, _) => await RescanAsync();

        var chipNames = new (string key, string label)[]
        {
            ("all", "All"), ("art", "No art"), ("year", "No year"), ("genre", "No genre"),
            ("album", "No album"), ("bpm", "No BPM"), ("incomplete", "Incomplete"),
        };

        int x = 14;
        foreach (var (key, label) in chipNames)
        {
            var chip = new FlatButton
            {
                Text = label,
                Size = new Size(TextRenderer.MeasureText(label, Theme.UI).Width + 26, 26),
                Location = new Point(x, 52),
                Primary = key == "all",
                Tag = key,
            };
            chip.Click += (_, _) =>
            {
                _filter = key;
                foreach (var c in _filterChips) { c.Primary = ReferenceEquals(c, chip); c.Invalidate(); }
                ApplyFilter();
            };
            _filterChips.Add(chip);
            bar.Controls.Add(chip);
            x += chip.Width + 6;
        }

        bar.Controls.AddRange(new Control[] { _search, _count, _rescan });
        bar.Resize += (_, _) => _rescan.Location = new Point(bar.Width - _rescan.Width - 14, 14);
        return bar;
    }

    private Control BuildActionBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Background };

        _status.Location = new Point(2, 16);
        _status.Size = new Size(420, 18);
        _status.ForeColor = Theme.TextDim;
        _status.AutoEllipsis = true;

        var buttons = new (FlatButton button, string text, int width, bool primary, Action click)[]
        {
            (_enrich,  "Fill tags from online", 156, true,  RunEnrich),
            (_analyze, "Analyse BPM + key",     144, false, RunAnalyze),
            (_find,    "Find on YouTube",       130, false, RunFind),
            (_edit,    "Edit tags",              94, false, OpenEditor),
            (_reveal,  "Show in folder",        116, false, RevealSelected),
        };

        foreach (var (button, text, width, primary, click) in buttons)
        {
            button.Text = text;
            button.Size = new Size(width, 30);
            button.Primary = primary;
            button.Enabled = false;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Click += (_, _) => click();
            bar.Controls.Add(button);
        }

        bar.Controls.Add(_status);
        bar.Resize += (_, _) =>
        {
            int right = bar.Width;
            foreach (var (button, _, _, _, _) in buttons.Reverse())
            {
                right -= button.Width + 8;
                button.Location = new Point(right, 10);
            }
        };
        return bar;
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;
        _list.CheckBoxes = false;
        _list.VirtualMode = false;

        var columns = new (string title, int width)[]
        {
            ("Title", 250), ("Artist", 170), ("Album", 180), ("Year", 52),
            ("Genre", 100), ("#", 40), ("BPM", 56), ("Key", 50),
            ("Length", 62), ("Missing", 190),
        };
        foreach (var (title, width) in columns) _list.Columns.Add(title, width);

        _list.DrawSubItem += DrawSubItem;
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

    private void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var track = e.Item?.Tag as Track;
        bool selected = e.Item?.Selected == true;

        var background = selected ? Theme.Selection
            : (e.ItemIndex % 2 == 0 ? Theme.Surface : Theme.SurfaceAlt);
        using (var b = new SolidBrush(background)) e.Graphics.FillRectangle(b, e.Bounds);

        if (selected)
            using (var b = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.X, e.Bounds.Y,
                    e.ColumnIndex == 0 ? 2 : 0, e.Bounds.Height));

        var text = e.SubItem?.Text ?? "";
        var colour = Theme.Text;

        if (e.ColumnIndex == 9)                        // Missing
            colour = text.Length == 0 ? Theme.Good : Theme.Warn;
        else if (e.ColumnIndex is 3 or 5 or 8)         // Year, #, Length
            colour = Theme.TextDim;
        else if (e.ColumnIndex is 6 or 7 && track is not null
                 && string.IsNullOrWhiteSpace(track.Bpm) && track.DjayBpm.HasValue)
            colour = Theme.TextFaint;                  // BPM came from djay, not our tags

        if (e.ColumnIndex == 9 && text.Length == 0) text = "complete";

        var bounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Theme.UI, bounds, colour,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // -------------------------------------------------------------- data

    public void FocusSearch() => _search.Inner.Focus();

    public async Task RescanAsync()
    {
        _rescan.Enabled = false;
        _rescan.Text = "Scanning";
        _status.Text = $"Scanning {_forge.Config.LibraryFolder}...";
        _status.ForeColor = Theme.TextDim;

        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            await _forge.RescanLibraryAsync(progress);
            RefreshFromService();

            if (_all.Count == 0)
                _status.Text = Directory.Exists(_forge.Config.LibraryFolder)
                    ? "No audio files in that folder. Check the path in Settings."
                    : $"Folder not found: {_forge.Config.LibraryFolder}";
            else
                _status.Text = $"Scanned {_all.Count} files from {_forge.Config.LibraryFolder}";
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

        IEnumerable<Track> filtered = _all;

        filtered = _filter switch
        {
            "art" => filtered.Where(t => !t.HasArt),
            "year" => filtered.Where(t => string.IsNullOrWhiteSpace(t.Year)),
            "genre" => filtered.Where(t => string.IsNullOrWhiteSpace(t.Genre)),
            "album" => filtered.Where(t => string.IsNullOrWhiteSpace(t.Album)),
            "bpm" => filtered.Where(t => string.IsNullOrWhiteSpace(t.DisplayBpm)),
            "incomplete" => filtered.Where(t => !t.IsComplete),
            _ => filtered,
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

    private void Render()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var t in _shown)
        {
            var item = new ListViewItem(new[]
            {
                t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path),
                t.Artist,
                t.Album,
                t.Year,
                t.Genre,
                (t.TrackNumber ?? "").Split('/')[0],
                t.DisplayBpm,
                t.Camelot.Length > 0 ? $"{t.MusicalKey} {t.Camelot}" : t.MusicalKey,
                t.DurationText,
                t.MissingText,
            })
            { Tag = t };
            _list.Items.Add(item);
        }

        _list.EndUpdate();

        int incomplete = _all.Count(t => !t.IsComplete);
        _count.Text = $"{_shown.Count} shown  |  {_all.Count} total  |  {incomplete} need work";
        UpdateSelectionState();
    }

    private void SortBy(int column)
    {
        var keys = new[] { "title", "artist", "album", "year", "genre", "track", "bpm", "key", "length", "missing" };
        if (column < 0 || column >= keys.Length) return;

        var key = keys[column];
        _sortAscending = key == _sortColumn ? !_sortAscending : true;
        _sortColumn = key;
        SortShown();
        Render();
    }

    private void SortShown()
    {
        Func<Track, object> selector = _sortColumn switch
        {
            "artist" => t => t.Artist ?? "",
            "album" => t => t.Album ?? "",
            "year" => t => t.Year ?? "",
            "genre" => t => t.Genre ?? "",
            "track" => t => int.TryParse((t.TrackNumber ?? "").Split('/')[0], out var n) ? n : 0,
            "bpm" => t => double.TryParse(t.DisplayBpm, out var b) ? b : 0,
            "key" => t => t.Camelot ?? "",
            "length" => t => t.DurationSeconds,
            "missing" => t => t.MissingFields().Count(),
            _ => t => t.Title ?? "",
        };

        _shown = (_sortAscending
            ? _shown.OrderBy(selector, Comparer<object>.Create(Compare))
            : _shown.OrderByDescending(selector, Comparer<object>.Create(Compare))).ToList();

        static int Compare(object? a, object? b) => (a, b) switch
        {
            (string x, string y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase),
            (IComparable x, _) => x.CompareTo(b),
            _ => 0,
        };
    }

    private List<Track> Selected() =>
        _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag).OfType<Track>().ToList();

    private void UpdateSelectionState()
    {
        int n = _list.SelectedItems.Count;
        foreach (var b in new[] { _enrich, _analyze, _find, _reveal })
        {
            b.Enabled = n > 0;
            b.Invalidate();
        }
        _edit.Enabled = n == 1;
        _edit.Invalidate();

        if (n > 0) _status.Text = $"{n} selected";
    }

    // ------------------------------------------------------------ actions

    private void RunEnrich()
    {
        var tracks = Selected();
        if (tracks.Count == 0) return;

        using var dialog = new EnrichOptionsDialog(tracks.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _forge.EnqueueEnrich(tracks, dialog.Options);
        _status.Text = $"Filling tags on {tracks.Count} track(s) - watch the Jobs panel.";
        _status.ForeColor = Theme.TextDim;
    }

    private void RunAnalyze()
    {
        var tracks = Selected();
        if (tracks.Count == 0) return;

        _forge.EnqueueAnalyze(tracks);
        _status.Text = $"Analysing {tracks.Count} track(s) - watch the Jobs panel.";
        _status.ForeColor = Theme.TextDim;
    }

    private void RunFind()
    {
        var tracks = Selected();
        if (tracks.Count == 0) return;
        SendToFindRequested?.Invoke(tracks);
    }

    private void OpenEditor()
    {
        var tracks = Selected();
        if (tracks.Count != 1) return;

        using var editor = new TagEditorDialog(_forge, tracks[0]);
        if (editor.ShowDialog(this) == DialogResult.OK) ApplyFilter();
    }

    private void RevealSelected()
    {
        var track = Selected().FirstOrDefault();
        if (track is null || !File.Exists(track.Path)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{track.Path}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }
}
