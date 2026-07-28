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
    private readonly FlatButton _analyze = new();
    private readonly FlatButton _find = new();
    private readonly FlatButton _edit = new();
    private readonly FlatButton _reveal = new();
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

        var bar = BuildBar();
        BuildList();

        Controls.Add(_list);
        Controls.Add(bar);
    }

    /// <summary>Search, filters, counts and bulk actions in a single 66px strip.</summary>
    private Control BuildBar()
    {
        var bar = new CardPanel { Dock = DockStyle.Top, Height = 66 };

        _search.Location = new Point(Theme.Pad, 8);
        _search.Size = new Size(230, 24);
        _search.PlaceholderText = "Search";
        _search.Inner.TextChanged += (_, _) => ApplyFilter();

        var chipDefs = new (string key, string label)[]
        {
            ("all", "All"), ("art", "No art"), ("year", "No year"), ("genre", "No genre"),
            ("album", "No album"), ("bpm", "No BPM"), ("incomplete", "Incomplete"),
        };

        int x = _search.Right + Theme.Gap * 2;
        foreach (var (key, label) in chipDefs)
        {
            var chip = new FlatButton
            {
                Text = label,
                Font = Theme.Small,
                Size = new Size(TextRenderer.MeasureText(label, Theme.Small).Width + 16, 24),
                Location = new Point(x, 8),
                Primary = key == "all",
            };
            chip.Click += (_, _) =>
            {
                _filter = key;
                foreach (var c in _chips) { c.Primary = ReferenceEquals(c, chip); c.Invalidate(); }
                ApplyFilter();
            };
            _chips.Add(chip);
            bar.Controls.Add(chip);
            x += chip.Width + 4;
        }

        _count.Location = new Point(x + 8, 12);
        _count.Size = new Size(220, 16);
        _count.Font = Theme.Small;
        _count.ForeColor = Theme.TextFaint;
        _count.AutoEllipsis = true;

        _rescan.Text = "Rescan";
        _rescan.Size = new Size(66, 24);
        _rescan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _rescan.Click += async (_, _) => await RescanAsync();

        _status.Location = new Point(Theme.Pad, 40);
        _status.Size = new Size(300, 16);
        _status.Font = Theme.Small;
        _status.ForeColor = Theme.TextDim;
        _status.AutoEllipsis = true;

        var actionDefs = new (FlatButton b, string text, int w, bool primary, Action click)[]
        {
            (_fillAll, "Fill every track", 100, true,  RunFillAll),
            (_enrich,  "Fill selected",     88, false, RunEnrich),
            (_analyze, "BPM + key",         76, false, RunAnalyze),
            (_find,    "Find on YouTube",   98, false, RunFind),
            (_edit,    "Edit",              48, false, OpenEditor),
            (_reveal,  "Show file",         66, false, RevealSelected),
        };

        foreach (var (b, text, w, primary, click) in actionDefs)
        {
            b.Text = text;
            b.Font = Theme.Small;
            b.Size = new Size(w, 24);
            b.Primary = primary;
            // "Fill every track" works on whatever is shown, so it never needs a selection.
            b.Enabled = ReferenceEquals(b, _fillAll);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            b.Click += (_, _) => click();
            _actions.Add(b);
            bar.Controls.Add(b);
        }

        bar.Controls.AddRange(new Control[] { _search, _count, _rescan, _status });
        bar.Resize += (_, _) =>
        {
            _rescan.Location = new Point(bar.Width - _rescan.Width - Theme.Pad, 8);
            int right = bar.Width - Theme.Pad;
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                right -= _actions[i].Width;
                _actions[i].Location = new Point(right, 38);
                right -= Theme.Gap;
            }
            _count.Width = Math.Max(60, _rescan.Left - _count.Left - Theme.Gap);
        };
        return bar;
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;

        var columns = new (string title, int width)[]
        {
            ("Title", 230), ("Artist", 150), ("Album", 160), ("Year", 46),
            ("Genre", 90), ("#", 34), ("BPM", 48), ("Key", 58),
            ("Len", 52), ("Missing", 170),
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

        if (selected && e.ColumnIndex == 0)
            using (var b = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.X, e.Bounds.Y, 2, e.Bounds.Height));

        var text = e.SubItem?.Text ?? "";
        var colour = Theme.Text;

        if (e.ColumnIndex == 9)
        {
            colour = text.Length == 0 ? Theme.Good : Theme.Warn;
            if (text.Length == 0) text = "complete";
        }
        else if (e.ColumnIndex is 3 or 5 or 8) colour = Theme.TextDim;
        else if (e.ColumnIndex is 6 or 7 && track is not null
                 && string.IsNullOrWhiteSpace(track.Bpm) && track.DjayBpm.HasValue)
            colour = Theme.TextFaint;   // came from djay, not from this file's tags

        var bounds = new Rectangle(e.Bounds.X + 5, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Theme.UI, bounds, colour,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // -------------------------------------------------------------- data

    public void FocusSearch() => _search.Inner.Focus();

    /// <summary>
    /// Test hook: churns the list the way real use does - filter, search, re-render,
    /// change selection, and load artwork - so the leak detector sees the paths that
    /// actually matter rather than just page visibility.
    /// </summary>
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

            // Artwork is the usual GDI offender: a Bitmap handed to a control and
            // never disposed leaks until the process runs out of objects.
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
        _status.ForeColor = Theme.TextDim;

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

    private void Render()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var t in _shown)
        {
            _list.Items.Add(new ListViewItem(new[]
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
            { Tag = t });
        }

        _list.EndUpdate();

        int incomplete = _all.Count(t => !t.IsComplete);
        _count.Text = $"{_shown.Count} shown / {_all.Count} total / {incomplete} need work";
        UpdateSelectionState();
    }

    private void SortBy(int column)
    {
        var keys = new[] { "title", "artist", "album", "year", "genre", "track", "bpm", "key", "length", "missing" };
        if (column < 0 || column >= keys.Length) return;

        _sortAscending = keys[column] != _sortColumn || !_sortAscending;
        _sortColumn = keys[column];
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
        foreach (var b in new[] { _enrich, _analyze, _find, _reveal }) { b.Enabled = n > 0; b.Invalidate(); }
        _edit.Enabled = n == 1;
        _edit.Invalidate();

        _fillAll.Enabled = _shown.Count > 0;
        _fillAll.Text = _shown.Count == _all.Count
            ? "Fill every track"
            : $"Fill all {_shown.Count} shown";
        _fillAll.Invalidate();

        if (n > 0) _status.Text = $"{n} selected";
    }

    // ------------------------------------------------------------ actions

    private void RunEnrich() => Enrich(Selected());

    /// <summary>
    /// Fills every track currently shown, so the common case - pick "Incomplete",
    /// fix the lot - doesn't need a select-all first.
    /// </summary>
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
        _status.ForeColor = Theme.TextDim;
    }

    private void RunAnalyze()
    {
        var tracks = Selected();
        if (tracks.Count == 0) return;
        _forge.EnqueueAnalyze(tracks);
        _status.Text = $"Analysing {tracks.Count} - see Jobs";
        _status.ForeColor = Theme.TextDim;
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
