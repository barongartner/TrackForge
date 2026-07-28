using TrackForge.Core;

namespace TrackForge.UI;

public sealed class MainForm : Form
{
    private readonly ForgeService _forge = new();

    private readonly Panel _topBar = new();
    private readonly Panel _pageHost = new();
    private readonly JobsPanel _jobs;
    private readonly Pill _toolPill = new();
    private readonly FlatButton _jobsButton = new();

    private readonly List<(NavButton button, Control page)> _pages = new();

    private GrabPage _grab = null!;
    private LibraryPage _library = null!;
    private FindPage _find = null!;
    private SettingsPage _settings = null!;

    public MainForm()
    {
        Text = "TrackForge";
        MinimumSize = new Size(1100, 680);
        Size = new Size(1420, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UI;
        DoubleBuffered = true;
        KeyPreview = true;

        _jobs = new JobsPanel(_forge);

        BuildTopBar();
        BuildPages();

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = Theme.Background;
        _pageHost.Padding = new Padding(0);

        Controls.Add(_pageHost);
        Controls.Add(_jobs);
        Controls.Add(_topBar);

        _forge.Jobs.JobChanged += OnJobChanged;
        _forge.LibraryChanged += () => BeginInvoke(() => _library.RefreshFromService());

        Shown += async (_, _) => await StartupAsync();
        FormClosing += (_, _) => { _forge.SaveConfig(); _forge.Dispose(); };
        KeyDown += OnKeyDown;
    }

    // ------------------------------------------------------------ chrome

    private void BuildTopBar()
    {
        _topBar.Dock = DockStyle.Top;
        _topBar.Height = 52;
        _topBar.BackColor = Theme.Background;
        _topBar.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, 18, 19, 4, 15);
            TextRenderer.DrawText(e.Graphics, "TrackForge", new Font("Segoe UI Semibold", 12f),
                new Rectangle(30, 16, 200, 21), Theme.Text, TextFormatFlags.Left);
        };

        _toolPill.Text = "checking tools";
        _toolPill.Size = new Size(140, 20);
        _toolPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _jobsButton.Text = "Jobs";
        _jobsButton.Size = new Size(84, 28);
        _jobsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _jobsButton.Click += (_, _) => _jobs.Toggle();

        _topBar.Controls.Add(_toolPill);
        _topBar.Controls.Add(_jobsButton);
        _topBar.Resize += (_, _) => LayoutTopBar();
    }

    private void LayoutTopBar()
    {
        _jobsButton.Location = new Point(_topBar.Width - _jobsButton.Width - 16, 12);
        _toolPill.Location = new Point(_jobsButton.Left - _toolPill.Width - 12, 16);
    }

    private void BuildPages()
    {
        _grab = new GrabPage(_forge);
        _library = new LibraryPage(_forge);
        _find = new FindPage(_forge);
        _settings = new SettingsPage(_forge);

        AddPage("Grab", _grab);
        AddPage("Library", _library);
        AddPage("Find Online", _find);
        AddPage("Settings", _settings);

        _library.SendToFindRequested += tracks =>
        {
            _find.LoadQueries(tracks);
            Show(2);
        };
        _find.SendToGrabRequested += urls =>
        {
            _grab.AddUrls(urls);
            Show(0);
        };

        Show(0);
    }

    private void AddPage(string title, Control page)
    {
        var nav = new NavButton
        {
            Text = title,
            Size = new Size(Math.Max(92, TextRenderer.MeasureText(title, Theme.UIBold).Width + 34), 52),
            Location = new Point(150 + _pages.Sum(p => p.button.Width), 0),
        };
        int index = _pages.Count;
        nav.Click += (_, _) => Show(index);

        page.Dock = DockStyle.Fill;
        page.Visible = false;

        _topBar.Controls.Add(nav);
        _pageHost.Controls.Add(page);
        _pages.Add((nav, page));
    }

    private void Show(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].button.Active = i == index;
            _pages[i].button.Invalidate();
            _pages[i].page.Visible = i == index;
        }
        if (_pages[index].page is LibraryPage lib) lib.FocusSearch();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode is >= Keys.D1 and <= Keys.D4)
        {
            Show(e.KeyCode - Keys.D1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            _ = _library.RescanAsync();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.J)
        {
            _jobs.Toggle();
            e.Handled = true;
        }
    }

    // ----------------------------------------------------------- startup

    private async Task StartupAsync()
    {
        LayoutTopBar();
        _settings.LoadFromConfig();

        var (ytDlp, ffmpeg) = await _forge.Downloader.CheckToolsAsync();
        _settings.ShowToolStatus(ytDlp, ffmpeg);

        if (ytDlp is null || ffmpeg is null)
        {
            var missing = new List<string>();
            if (ytDlp is null) missing.Add("yt-dlp");
            if (ffmpeg is null) missing.Add("ffmpeg");
            _toolPill.Text = "missing: " + string.Join(", ", missing);
            _toolPill.PillColour = Theme.Bad;
            _grab.SetToolsReady(false);
        }
        else
        {
            _toolPill.Text = "yt-dlp " + ytDlp;
            _toolPill.PillColour = Theme.Good;
            _grab.SetToolsReady(true);
        }
        _toolPill.Invalidate();
        LayoutTopBar();

        await _library.RescanAsync();
    }

    private void OnJobChanged(Job job)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _jobs.Refresh(_forge.Jobs.Jobs);
                int active = _forge.Jobs.ActiveCount;
                _jobsButton.Text = active > 0 ? $"Jobs  {active}" : "Jobs";
                _jobsButton.Primary = active > 0;
                _jobsButton.Invalidate();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
