using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>
/// Takes track names - typed in or sent over from the Library - and finds them
/// on YouTube, so anything missing from disk can go straight into Grab.
/// </summary>
public sealed class FindPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _queries = new(multiline: true);
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
        Padding = new Padding(18, 16, 18, 16);

        var intake = new CardPanel { Dock = DockStyle.Top, Height = 172, Padding = new Padding(14) };

        var caption = new Label
        {
            Text = "One search per line. Or select tracks in the Library and send them here.",
            Location = new Point(16, 12),
            Size = new Size(700, 18),
            ForeColor = Theme.TextDim,
        };

        _queries.Location = new Point(16, 34);
        _queries.Size = new Size(880, 80);
        _queries.PlaceholderText = "Artist - Title";
        _queries.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _search.Text = "Search YouTube";
        _search.Primary = true;
        _search.Size = new Size(140, 32);
        _search.Location = new Point(16, 124);
        _search.Click += async (_, _) => await SearchAsync();

        _sendAll.Text = "Send best to Grab";
        _sendAll.Size = new Size(146, 32);
        _sendAll.Location = new Point(164, 124);
        _sendAll.Enabled = false;
        _sendAll.Click += (_, _) => SendBest();

        _clear.Text = "Clear";
        _clear.Size = new Size(80, 32);
        _clear.Location = new Point(318, 124);
        _clear.Click += (_, _) => { _rows.Clear(); Render(); _note.Text = ""; };

        _note.Location = new Point(410, 132);
        _note.Size = new Size(500, 18);
        _note.ForeColor = Theme.TextFaint;
        _note.Font = Theme.Small;
        _note.AutoEllipsis = true;

        intake.Controls.AddRange(new Control[] { caption, _queries, _search, _sendAll, _clear, _note });
        intake.Resize += (_, _) => _queries.Width = intake.Width - 32;

        _results.Dock = DockStyle.Fill;
        _results.Columns.Add("Searched for", 230);
        _results.Columns.Add("YouTube title", 330);
        _results.Columns.Add("Channel", 180);
        _results.Columns.Add("Length", 66);
        _results.Columns.Add("Views", 90);
        _results.Columns.Add("Link", 260);
        _results.DrawSubItem += DrawSubItem;
        _results.DoubleClick += (_, _) => SendSelected();

        var menu = new ContextMenuStrip { BackColor = Theme.SurfaceAlt, ForeColor = Theme.Text };
        menu.Items.Add("Send to Grab", null, (_, _) => SendSelected());
        menu.Items.Add("Copy link", null, (_, _) => CopySelected());
        menu.Items.Add("Open in browser", null, (_, _) => OpenSelected());
        _results.ContextMenuStrip = menu;

        Controls.Add(_results);
        Controls.Add(intake);
    }

    public void LoadQueries(IReadOnlyList<Track> tracks)
    {
        var lines = tracks
            .Select(t => $"{t.Artist} - {t.Title}".Trim(' ', '-'))
            .Where(s => s.Length > 0)
            .Distinct();
        _queries.Text = string.Join(Environment.NewLine, lines);
        _note.Text = $"{tracks.Count} track(s) loaded from the library.";
        _note.ForeColor = Theme.TextFaint;
    }

    private async Task SearchAsync()
    {
        var queries = _queries.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        if (queries.Count == 0)
        {
            _note.Text = "Nothing to search for.";
            _note.ForeColor = Theme.Warn;
            return;
        }

        _search.Enabled = false;
        _search.Text = "Searching...";
        _rows.Clear();

        for (int i = 0; i < queries.Count; i++)
        {
            _note.Text = $"{i + 1}/{queries.Count}  {queries[i]}";
            _note.ForeColor = Theme.TextDim;

            var hits = await _forge.Downloader.SearchAsync(queries[i], limit: 3);
            for (int h = 0; h < hits.Count; h++)
                _rows.Add(new Row(queries[i], hits[h], h == 0));

            Render();
        }

        _search.Enabled = true;
        _search.Text = "Search YouTube";
        _sendAll.Enabled = _rows.Count > 0;

        int found = _rows.Select(r => r.Query).Distinct().Count();
        _note.Text = $"Found results for {found} of {queries.Count} search(es). " +
                     "Double-click a row to send it to Grab.";
        _note.ForeColor = Theme.TextFaint;
    }

    private void Render()
    {
        _results.BeginUpdate();
        _results.Items.Clear();

        foreach (var row in _rows)
        {
            var item = new ListViewItem(new[]
            {
                row.Best ? row.Query : "",
                row.Entry.RawTitle,
                row.Entry.Uploader,
                row.Entry.DurationText,
                row.Entry.ViewCount > 0 ? row.Entry.ViewCount.ToString("N0") : "",
                row.Entry.Url,
            })
            { Tag = row };
            _results.Items.Add(item);
        }

        _results.EndUpdate();
    }

    private void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var row = e.Item?.Tag as Row;
        bool selected = e.Item?.Selected == true;

        var background = selected ? Theme.Selection
            : (row?.Best == true ? Theme.Surface : Theme.SurfaceAlt);
        using (var b = new SolidBrush(background)) e.Graphics.FillRectangle(b, e.Bounds);

        if (row?.Best == true && e.ColumnIndex == 0)
            using (var b = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.X, e.Bounds.Y, 2, e.Bounds.Height));

        var colour = e.ColumnIndex switch
        {
            0 => Theme.Text,
            5 => Theme.TextFaint,
            2 or 3 or 4 => Theme.TextDim,
            _ => row?.Best == true ? Theme.Text : Theme.TextDim,
        };

        var bounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", Theme.UI, bounds, colour,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private List<Row> SelectedRows() =>
        _results.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Row>().ToList();

    private void SendSelected()
    {
        var urls = SelectedRows().Select(r => r.Entry.Url).Where(u => u.Length > 0).ToList();
        if (urls.Count == 0) return;
        SendToGrabRequested?.Invoke(urls);
    }

    private void SendBest()
    {
        var urls = _rows.Where(r => r.Best).Select(r => r.Entry.Url).Where(u => u.Length > 0).ToList();
        if (urls.Count == 0) return;
        SendToGrabRequested?.Invoke(urls);
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
