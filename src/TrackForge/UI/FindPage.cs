using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>
/// Takes track names - typed in or sent over from the Library - and finds them on
/// YouTube, so anything missing from disk can go straight into Grab.
/// </summary>
public sealed class FindPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _queries = new(multiline: true, monospace: true);
    private readonly FlatButton _search = new();
    private readonly FlatButton _sendAll = new();
    private readonly FlatButton _clear = new();
    private readonly Label _note = new();
    private readonly DarkListView _results = new();

    private readonly List<Row> _rows = new();

    public event Action<IReadOnlyList<string>>? SendToGrabRequested;

    private sealed record Row(string Query, VideoEntry Entry, bool Best);

    public FindPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(Theme.Pad);

        var intake = new CardPanel { Dock = DockStyle.Top, Height = 100 };

        _queries.Location = new Point(Theme.Pad, Theme.Pad);
        _queries.Size = new Size(600, 48);
        _queries.PlaceholderText = "Artist - Title, one per line. Or send tracks here from Library.";
        _queries.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var buttons = new (FlatButton b, string text, int w, bool primary, Action click)[]
        {
            (_search,  "Search",    72, true,  () => _ = SearchAsync()),
            (_sendAll, "Send best", 76, false, SendBest),
            (_clear,   "Clear",     54, false, () => { _rows.Clear(); Render(); _note.Text = ""; }),
        };

        int x = Theme.Pad;
        int buttonY = Theme.Pad + 48 + Theme.Gap;
        foreach (var (b, text, w, primary, click) in buttons)
        {
            b.Text = text;
            b.Size = new Size(w, Theme.PrimaryButtonHeight);
            b.Location = new Point(x, buttonY);
            b.Primary = primary;
            b.Click += (_, _) => click();
            intake.Controls.Add(b);
            x += w + Theme.Gap;
        }
        _sendAll.Enabled = false;

        _note.Location = new Point(x + 4, buttonY + 5);
        _note.Size = new Size(400, 16);
        _note.ForeColor = Theme.TextFaint;
        _note.Font = Theme.Secondary;
        _note.AutoEllipsis = true;
        _note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        intake.Controls.Add(_queries);
        intake.Controls.Add(_note);
        intake.Resize += (_, _) =>
        {
            _queries.Width = intake.Width - Theme.Pad * 2;
            _note.Width = Math.Max(120, intake.Width - _note.Left - Theme.Pad);
        };

        _results.Dock = DockStyle.Fill;
        _results.Columns.Add("Searched for", 190);
        _results.Columns.Add("YouTube title", 290);
        _results.Columns.Add("Channel", 150);
        _results.Columns.Add("Len", 56);
        _results.Columns.Add("Views", 84);
        _results.Columns.Add("Link", 210);

        foreach (var i in new[] { 3, 4, 5 }) _results.NumericColumns.Add(i);
        _results.IsAccented = item => item.Tag is Row { Best: true };
        _results.ColourFor = (item, column) =>
        {
            var best = item.Tag is Row { Best: true };
            return column switch
            {
                0 => Theme.Text,
                5 => Theme.TextFaint,
                2 or 3 or 4 => Theme.TextDim,
                _ => best ? Theme.Text : Theme.TextDim,
            };
        };

        _results.DoubleClick += (_, _) => SendSelected();

        var menu = new ContextMenuStrip { BackColor = Theme.SurfaceAlt, ForeColor = Theme.Text };
        menu.Items.Add("Send to Grab", null, (_, _) => SendSelected());
        menu.Items.Add("Copy link", null, (_, _) => CopySelected());
        menu.Items.Add("Open in browser", null, (_, _) => OpenSelected());
        _results.ContextMenuStrip = menu;

        var spacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Background };

        Controls.Add(_results);
        Controls.Add(spacer);
        Controls.Add(intake);
    }

    public void LoadQueries(IReadOnlyList<Track> tracks)
    {
        var lines = tracks
            .Select(t => $"{t.Artist} - {t.Title}".Trim(' ', '-'))
            .Where(s => s.Length > 0).Distinct();
        _queries.Text = string.Join(Environment.NewLine, lines);
        _note.Text = $"{tracks.Count} loaded from library.";
        _note.ForeColor = Theme.TextFaint;
    }

    private async Task SearchAsync()
    {
        var queries = _queries.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        if (queries.Count == 0)
        {
            _note.Text = "Nothing to search for.";
            _note.ForeColor = Theme.Warn;
            return;
        }

        _search.Enabled = false;
        _search.Text = "...";
        _rows.Clear();

        for (int i = 0; i < queries.Count; i++)
        {
            _note.Text = $"{i + 1}/{queries.Count}  {queries[i]}";
            _note.ForeColor = Theme.TextMuted;

            var hits = await _forge.Downloader.SearchAsync(queries[i], limit: 3);
            for (int h = 0; h < hits.Count; h++) _rows.Add(new Row(queries[i], hits[h], h == 0));
            Render();
        }

        _search.Enabled = true;
        _search.Text = "Search";
        _sendAll.Enabled = _rows.Count > 0;
        _sendAll.Invalidate();

        int found = _rows.Select(r => r.Query).Distinct().Count();
        _note.Text = $"Results for {found}/{queries.Count}. Double-click a row to send it to Grab.";
        _note.ForeColor = Theme.TextFaint;
    }

    private void Render()
    {
        _results.BeginUpdate();
        _results.Items.Clear();

        foreach (var row in _rows)
        {
            _results.Items.Add(new ListViewItem(new[]
            {
                row.Best ? row.Query : "",
                row.Entry.RawTitle,
                row.Entry.Uploader,
                row.Entry.DurationText,
                row.Entry.ViewCount > 0 ? row.Entry.ViewCount.ToString("N0") : "",
                row.Entry.Url,
            })
            { Tag = row });
        }

        _results.EndUpdate();
    }

    private List<Row> SelectedRows() =>
        _results.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Row>().ToList();

    private void SendSelected()
    {
        var urls = SelectedRows().Select(r => r.Entry.Url).Where(u => u.Length > 0).ToList();
        if (urls.Count > 0) SendToGrabRequested?.Invoke(urls);
    }

    private void SendBest()
    {
        var urls = _rows.Where(r => r.Best).Select(r => r.Entry.Url).Where(u => u.Length > 0).ToList();
        if (urls.Count > 0) SendToGrabRequested?.Invoke(urls);
    }

    private void CopySelected()
    {
        var urls = SelectedRows().Select(r => r.Entry.Url).ToList();
        if (urls.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, urls)); } catch { }
    }

    private void OpenSelected()
    {
        foreach (var url in SelectedRows().Select(r => r.Entry.Url).Take(5))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch { }
        }
    }
}
