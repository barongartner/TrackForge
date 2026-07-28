using TrackForge.Core;

namespace TrackForge.UI;

public sealed class MainForm : Form
{
    private readonly ForgeService _forge = new();

    private readonly Panel _topBar = new();
    private readonly Panel _pageHost = new();
    private readonly JobsPanel _jobs;
    private readonly Label _toolStatus = new();
    private readonly FlatButton _jobsButton = new();

    private readonly List<(NavButton button, Control page)> _pages = new();

    private GrabPage _grab = null!;
    private LibraryPage _library = null!;
    private FindPage _find = null!;
    private SettingsPage _settings = null!;

    public MainForm()
    {
        Text = "TrackForge";
        MinimumSize = new Size(900, 560);
        Size = new Size(1120, 700);
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

        Controls.Add(_pageHost);
        Controls.Add(_jobs);
        Controls.Add(_topBar);

        _forge.Jobs.JobChanged += OnJobChanged;
        _forge.LibraryChanged += () =>
        {
            if (IsHandleCreated) BeginInvoke(() => _library.RefreshFromService());
        };

        Shown += async (_, _) => await StartupAsync();
        FormClosing += (_, _) => { _forge.SaveConfig(); _forge.Dispose(); };
        KeyDown += OnKeyDown;
    }

    // ------------------------------------------------------------ chrome

    private void BuildTopBar()
    {
        _topBar.Dock = DockStyle.Top;
        _topBar.Height = Theme.TopBarHeight;
        _topBar.BackColor = Theme.Background;
        _topBar.Paint += (_, e) =>
        {
            using var border = new Pen(Theme.Border);
            e.Graphics.DrawLine(border, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, Theme.Pad, 13, 3, 14);
            TextRenderer.DrawText(e.Graphics, "TrackForge", Theme.UIBold,
                new Rectangle(Theme.Pad + 9, 0, 110, _topBar.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };

        _toolStatus.AutoSize = false;
        _toolStatus.Size = new Size(150, 16);
        _toolStatus.Font = Theme.Small;
        _toolStatus.ForeColor = Theme.TextFaint;
        _toolStatus.TextAlign = ContentAlignment.MiddleRight;
        _toolStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _toolStatus.Text = "checking";

        _jobsButton.Text = "Jobs";
        _jobsButton.Size = new Size(66, 24);
        _jobsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _jobsButton.Click += (_, _) => _jobs.Toggle();

        _topBar.Controls.Add(_toolStatus);
        _topBar.Controls.Add(_jobsButton);
        _topBar.Resize += (_, _) => LayoutTopBar();
    }

    private void LayoutTopBar()
    {
        _jobsButton.Location = new Point(_topBar.Width - _jobsButton.Width - Theme.Pad, 8);
        _toolStatus.Location = new Point(_jobsButton.Left - _toolStatus.Width - Theme.Gap, 12);
    }

    private void BuildPages()
    {
        _grab = new GrabPage(_forge);
        _library = new LibraryPage(_forge);
        _find = new FindPage(_forge);
        _settings = new SettingsPage(_forge);

        AddPage("Grab", _grab);
        AddPage("Library", _library);
        AddPage("Find", _find);
        AddPage("Settings", _settings);

        _library.SendToFindRequested += tracks => { _find.LoadQueries(tracks); Show(2); };
        _find.SendToGrabRequested += urls => { _grab.AddUrls(urls); Show(0); };

        Show(0);
    }

    private void AddPage(string title, Control page)
    {
        var width = TextRenderer.MeasureText(title, Theme.UIBold).Width + 26;
        var nav = new NavButton
        {
            Text = title,
            Size = new Size(width, Theme.TopBarHeight),
            Location = new Point(112 + _pages.Sum(p => p.button.Width), 0),
        };
        int index = _pages.Count;
        nav.Click += (_, _) => Show(index);

        page.Dock = DockStyle.Fill;
        page.Visible = false;

        _topBar.Controls.Add(nav);
        _pageHost.Controls.Add(page);
        _pages.Add((nav, page));
    }

    /// <summary>Test hook for the handle-leak stress run.</summary>
    internal void ShowPageForTesting(int index) => Show(index);

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
        await RefreshToolStatusAsync(offerInstall: true);
        await _library.RescanAsync();
    }

    /// <summary>
    /// Checks for yt-dlp and ffmpeg. On first run, offers to fetch them rather than
    /// leaving the user to work out what a PATH is.
    /// </summary>
    public async Task RefreshToolStatusAsync(bool offerInstall = false)
    {
        var (ytDlp, ffmpeg) = await _forge.Downloader.CheckToolsAsync();
        _settings.ShowToolStatus(ytDlp, ffmpeg);

        if (ytDlp is not null && ffmpeg is not null)
        {
            _toolStatus.Text = "tools ready";
            _toolStatus.ForeColor = Theme.Good;
            _grab.SetToolsReady(true);
            return;
        }

        var missing = new List<string>();
        if (ytDlp is null) missing.Add("yt-dlp");
        if (ffmpeg is null) missing.Add("ffmpeg");

        _toolStatus.Text = "missing " + string.Join(" + ", missing);
        _toolStatus.ForeColor = Theme.Bad;
        _grab.SetToolsReady(false);
        LayoutTopBar();

        if (!offerInstall) return;

        using var dialog = new ToolSetupDialog(_forge, missing);
        dialog.ShowDialog(this);
        if (dialog.InstalledSomething) await RefreshToolStatusAsync(offerInstall: false);
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
                _jobsButton.Text = active > 0 ? $"Jobs {active}" : "Jobs";
                _jobsButton.Primary = active > 0;
                _jobsButton.Invalidate();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
