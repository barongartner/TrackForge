using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Paste links, review what came back, grab it.</summary>
public sealed class GrabPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _urlBox = new(multiline: true);
    private readonly FlatButton _fetch = new();
    private readonly FlatButton _grabAll = new();
    private readonly FlatButton _lookupAll = new();
    private readonly FlatButton _clear = new();
    private readonly Label _note = new();
    private readonly FlowLayoutPanel _cards = new();

    private bool _toolsReady = true;

    public GrabPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(18, 16, 18, 16);

        var intake = new CardPanel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(16) };

        var caption = new Label
        {
            Text = "YouTube links, one per line. Playlists expand into every track.",
            Location = new Point(16, 12),
            Size = new Size(700, 18),
            ForeColor = Theme.TextDim,
        };

        _urlBox.Location = new Point(16, 34);
        _urlBox.Size = new Size(900, 84);
        _urlBox.Inner.WordWrap = false;
        _urlBox.Inner.ScrollBars = ScrollBars.Both;
        _urlBox.PlaceholderText = "https://www.youtube.com/watch?v=...";
        _urlBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _fetch.Text = "Fetch metadata";
        _fetch.Primary = true;
        _fetch.Size = new Size(140, 32);
        _fetch.Location = new Point(16, 128);
        _fetch.Click += async (_, _) => await FetchAsync();

        _lookupAll.Text = "Look up all";
        _lookupAll.Size = new Size(110, 32);
        _lookupAll.Location = new Point(164, 128);
        _lookupAll.Enabled = false;
        _lookupAll.Click += async (_, _) => await LookupAllAsync();

        _grabAll.Text = "Grab all";
        _grabAll.Size = new Size(96, 32);
        _grabAll.Location = new Point(282, 128);
        _grabAll.Enabled = false;
        _grabAll.Click += (_, _) => GrabAll();

        _clear.Text = "Clear";
        _clear.Size = new Size(80, 32);
        _clear.Location = new Point(386, 128);
        _clear.Enabled = false;
        _clear.Click += (_, _) => ClearCards();

        _note.Location = new Point(478, 136);
        _note.Size = new Size(500, 18);
        _note.ForeColor = Theme.TextFaint;
        _note.Font = Theme.Small;
        _note.AutoEllipsis = true;

        intake.Controls.AddRange(new Control[]
            { caption, _urlBox, _fetch, _lookupAll, _grabAll, _clear, _note });
        intake.Resize += (_, _) => _urlBox.Width = intake.Width - 32;

        _cards.Dock = DockStyle.Fill;
        _cards.FlowDirection = FlowDirection.TopDown;
        _cards.WrapContents = false;
        _cards.AutoScroll = true;
        _cards.BackColor = Theme.Background;
        _cards.Padding = new Padding(0, 12, 0, 0);
        _cards.Resize += (_, _) => ResizeCards();

        Controls.Add(_cards);
        Controls.Add(intake);

        _urlBox.Inner.KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await FetchAsync(); }
        };
    }

    public void SetToolsReady(bool ready)
    {
        _toolsReady = ready;
        if (!ready)
        {
            _note.Text = "yt-dlp or ffmpeg is missing. See Settings for how to install them.";
            _note.ForeColor = Theme.Bad;
        }
    }

    public void AddUrls(IEnumerable<string> urls)
    {
        var existing = _urlBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim()).ToHashSet();
        var fresh = urls.Where(u => !string.IsNullOrWhiteSpace(u) && existing.Add(u.Trim())).ToList();
        if (fresh.Count == 0) return;

        var text = _urlBox.Text.TrimEnd();
        _urlBox.Text = (text.Length > 0 ? text + Environment.NewLine : "")
                       + string.Join(Environment.NewLine, fresh);
    }

    // -------------------------------------------------------------- fetch

    private async Task FetchAsync()
    {
        if (!_toolsReady)
        {
            MessageBox.Show(this,
                "yt-dlp and ffmpeg both need to be on your PATH.\n\n" +
                "Install yt-dlp:   pip install -U yt-dlp\n" +
                "Install ffmpeg:  winget install Gyan.FFmpeg",
                "Missing tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var urls = _urlBox.Text
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        if (urls.Count == 0)
        {
            _note.Text = "Paste at least one link.";
            _note.ForeColor = Theme.Warn;
            return;
        }

        _fetch.Enabled = false;
        _fetch.Text = "Fetching...";
        _note.ForeColor = Theme.TextDim;

        int added = 0, failed = 0;
        foreach (var url in urls)
        {
            _note.Text = $"Reading {url}";
            try
            {
                var (entries, playlist) = await _forge.Downloader.ProbeAsync(url);
                foreach (var entry in entries) { AddCard(entry); added++; }
                if (playlist is not null) _note.Text = $"Playlist: {playlist} ({entries.Count} tracks)";
            }
            catch (Exception ex)
            {
                failed++;
                _note.Text = ex.Message;
                _note.ForeColor = Theme.Bad;
            }
        }

        _fetch.Enabled = true;
        _fetch.Text = "Fetch metadata";

        if (failed == 0)
        {
            _note.Text = $"{added} track(s) ready. Look them up, check the tags, then grab.";
            _note.ForeColor = Theme.TextFaint;
            _urlBox.Text = "";
        }

        UpdateButtons();
    }

    private void AddCard(VideoEntry entry)
    {
        var card = new GrabCard(_forge, entry)
        {
            Width = Math.Max(880, _cards.ClientSize.Width - 24),
        };
        card.RemoveRequested += c =>
        {
            _cards.Controls.Remove(c);
            c.Dispose();
            UpdateButtons();
        };
        _cards.Controls.Add(card);
    }

    private void ResizeCards()
    {
        foreach (Control c in _cards.Controls)
            c.Width = Math.Max(880, _cards.ClientSize.Width - 24);
    }

    private async Task LookupAllAsync()
    {
        _lookupAll.Enabled = false;
        _lookupAll.Text = "Looking up...";
        foreach (var card in _cards.Controls.OfType<GrabCard>().ToList())
            await card.LookupAsync();
        _lookupAll.Text = "Look up all";
        UpdateButtons();
    }

    private void GrabAll()
    {
        foreach (var card in _cards.Controls.OfType<GrabCard>().Where(c => !c.IsGrabbed).ToList())
            card.Grab();
    }

    private void ClearCards()
    {
        foreach (Control c in _cards.Controls.OfType<Control>().ToList())
        {
            _cards.Controls.Remove(c);
            c.Dispose();
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool any = _cards.Controls.Count > 0;
        _grabAll.Enabled = any;
        _lookupAll.Enabled = any;
        _clear.Enabled = any;
    }
}
