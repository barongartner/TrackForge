using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Paste links, review what came back, grab it.</summary>
public sealed class GrabPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _urlBox = new(multiline: true, monospace: true);
    private readonly FlatButton _download = new();
    private readonly FlatButton _fetch = new();
    private readonly FlatButton _grabAll = new();
    private readonly FlatButton _lookupAll = new();
    private readonly FlatButton _clear = new();
    private readonly Label _note = new();
    private readonly FlowLayoutPanel _cards = new();
    private readonly Label _empty = new();

    private bool _toolsReady = true;

    public GrabPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(Theme.Pad);

        var intake = new CardPanel { Dock = DockStyle.Top, Height = 104 };

        _urlBox.Location = new Point(Theme.Pad, Theme.Pad);
        _urlBox.Size = new Size(600, 52);
        _urlBox.Inner.WordWrap = false;
        _urlBox.Inner.ScrollBars = ScrollBars.Both;
        _urlBox.PlaceholderText = "Paste YouTube links, one per line. Playlists expand.";
        _urlBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var buttons = new (FlatButton b, string text, int w, bool primary, Action click)[]
        {
            (_download,  "Download",     86, true,  () => _ = FetchAsync(autoGrab: true)),
            (_fetch,     "Review first", 84, false, () => _ = FetchAsync(autoGrab: false)),
            (_lookupAll, "Look up all",  80, false, () => _ = LookupAllAsync()),
            (_grabAll,   "Grab all",     68, false, GrabAll),
            (_clear,     "Clear",        54, false, ClearCards),
        };

        int x = Theme.Pad;
        int buttonY = Theme.Pad + 52 + Theme.Gap;
        foreach (var (b, text, w, primary, click) in buttons)
        {
            b.Text = text;
            b.Size = new Size(w, Theme.PrimaryButtonHeight);
            b.Location = new Point(x, buttonY);
            b.Primary = primary;
            b.Click += (_, _) => click();
            if (b != _download && b != _fetch) b.Enabled = false;
            intake.Controls.Add(b);
            x += w + Theme.Gap;
        }

        _note.Location = new Point(x + 4, buttonY + 5);
        _note.Size = new Size(400, 16);
        _note.ForeColor = Theme.TextFaint;
        _note.Font = Theme.Secondary;
        _note.AutoEllipsis = true;
        _note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        intake.Controls.Add(_urlBox);
        intake.Controls.Add(_note);
        intake.Resize += (_, _) =>
        {
            _urlBox.Width = intake.Width - Theme.Pad * 2;
            _note.Width = Math.Max(120, intake.Width - _note.Left - Theme.Pad);
        };

        _cards.Dock = DockStyle.Fill;
        _cards.FlowDirection = FlowDirection.TopDown;
        _cards.WrapContents = false;
        _cards.AutoScroll = true;
        _cards.BackColor = Theme.Background;
        _cards.Padding = new Padding(0, 8, 0, 0);
        _cards.Resize += (_, _) => ResizeCards();

        _empty.Text = "Paste a YouTube link above and hit Download.\r\n\r\n" +
                      "It finds the tags, grabs the audio and files it for you.\r\n" +
                      "Use Review first if you want to check the tags before it downloads.";
        _empty.Dock = DockStyle.Fill;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.ForeColor = Theme.TextFaint;
        _empty.BackColor = Theme.Background;

        Controls.Add(_empty);
        Controls.Add(_cards);
        Controls.Add(intake);

        _urlBox.Inner.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = FetchAsync(autoGrab: true); }
        };

        UpdateButtons();
    }

    public void SetToolsReady(bool ready)
    {
        _toolsReady = ready;
        if (ready) return;
        _note.Text = "yt-dlp or ffmpeg missing - open Settings to install them.";
        _note.ForeColor = Theme.Bad;
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

    internal bool ToolsReadyForTesting => _toolsReady;
    internal string NoteForTesting => _note.Text;
    internal int CardCountForTesting => _cards.Controls.OfType<GrabCard>().Count();
    internal IEnumerable<GrabCard> CardsForTesting => _cards.Controls.OfType<GrabCard>();
    internal void SetUrlForTesting(string url) => _urlBox.Text = url;
    internal Task ClickDownloadForTesting() => FetchAsync(autoGrab: true);

    // -------------------------------------------------------------- fetch

    private async Task FetchAsync(bool autoGrab)
    {
        if (!_toolsReady)
        {
            MessageBox.Show(this,
                "yt-dlp and ffmpeg are both needed.\n\nOpen Settings and use Install tools.",
                "Missing tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = _urlBox.Text
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Where(s => seen.Add(YtDlp.NormalizeForProbe(s).url))
            .ToList();

        if (urls.Count == 0)
        {
            _note.Text = "Paste at least one link.";
            _note.ForeColor = Theme.Warn;
            return;
        }

        _download.Enabled = false;
        _fetch.Enabled = false;
        var active = autoGrab ? _download : _fetch;
        var originalText = active.Text;
        active.Text = "...";
        _note.ForeColor = Theme.TextMuted;

        var fresh = new List<GrabCard>();
        int failed = 0;

        foreach (var url in urls)
        {
            _note.Text = "Reading " + url;
            try
            {
                var (entries, playlist) = await _forge.Downloader.ProbeAsync(url);
                foreach (var entry in entries) fresh.Add(AddCard(entry));
                if (playlist is not null) _note.Text = $"Playlist: {playlist} ({entries.Count})";
            }
            catch (Exception ex)
            {
                failed++;
                _note.Text = ex.Message;
                _note.ForeColor = Theme.Bad;
            }
        }

        if (failed == 0) _urlBox.Text = "";
        UpdateButtons();

        if (autoGrab && fresh.Count > 0)
        {
            for (int i = 0; i < fresh.Count; i++)
            {
                _note.Text = $"Tagging {i + 1} of {fresh.Count}...";
                _note.ForeColor = Theme.TextMuted;
                await fresh[i].LookupAsync();
                fresh[i].Grab();
            }
            _note.Text = $"{fresh.Count} downloading. Watch the Jobs panel or the bars below.";
            _note.ForeColor = Theme.TextFaint;
        }
        else if (failed == 0)
        {
            _note.Text = $"{fresh.Count} looked up. Check the tags, then grab.";
            _note.ForeColor = Theme.TextFaint;
        }

        active.Text = originalText;
        _download.Enabled = true;
        _fetch.Enabled = true;
        UpdateButtons();
    }

    private GrabCard AddCard(VideoEntry entry)
    {
        var card = new GrabCard(_forge, entry) { Width = CardWidth() };
        card.RemoveRequested += c =>
        {
            _cards.Controls.Remove(c);
            c.Dispose();
            UpdateButtons();
        };
        _cards.Controls.Add(card);
        return card;
    }

    private int CardWidth() => Math.Max(640, _cards.ClientSize.Width - Theme.Pad);

    private void ResizeCards()
    {
        int w = CardWidth();
        foreach (Control c in _cards.Controls) c.Width = w;
    }

    private async Task LookupAllAsync()
    {
        _lookupAll.Enabled = false;
        _lookupAll.Text = "...";
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
        foreach (var b in new[] { _grabAll, _lookupAll, _clear }) { b.Enabled = any; b.Invalidate(); }
        _cards.Visible = any;
        _empty.Visible = !any;
    }
}
