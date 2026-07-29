using System.Runtime.InteropServices;
using TrackForge.Core;

namespace TrackForge.UI;

public sealed class MainForm : Form
{
    private readonly ForgeService _forge = new();

    private readonly Panel _topBar = new();
    private readonly Panel _pageHost = new();
    private readonly JobsPanel _jobs;
    private readonly Label _toolStatus = new();
    private readonly Panel _toolDot = new();
    private readonly FlatButton _jobsButton = new();

    private readonly List<(NavButton button, Control page)> _pages = new();

    private GrabPage _grab = null!;
    private LibraryPage _library = null!;
    private FindPage _find = null!;
    private SettingsPage _settings = null!;

    public MainForm()
    {
        Text = "TrackForge";
        MinimumSize = new Size(940, 580);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
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

    // ------------------------------------------------------- native chrome

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>
    /// Recolours the real title bar rather than drawing a fake one. Going borderless
    /// would mean re-implementing snap layouts, resize edges, double-click-to-maximise
    /// and the system menu, none of which is worth it.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Set(DwmwaUseImmersiveDarkMode, 1);                    // Win10 1809+
        Set(DwmwaCaptionColor, ToColorRef(Theme.Background)); // Win11 22000+ below here
        Set(DwmwaTextColor, ToColorRef(Theme.Text));
        Set(DwmwaBorderColor, ToColorRef(Theme.ChromeBorder));

        void Set(int attribute, int value)
        {
            try { DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int)); }
            catch { /* older Windows: the dark-mode attribute alone still applies */ }
        }
    }

    /// <summary>COLORREF is 0x00BBGGRR, the reverse of the usual hex order.</summary>
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // ------------------------------------------------------------ top bar

    private void BuildTopBar()
    {
        _topBar.Dock = DockStyle.Top;
        _topBar.Height = Theme.TopBarHeight;
        _topBar.BackColor = Theme.ChromePanel;
        _topBar.Paint += (_, e) =>
        {
            using var border = new Pen(Theme.ChromeBorder);
            e.Graphics.DrawLine(border, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
        };

        _toolDot.Size = new Size(6, 6);
        _toolDot.BackColor = Theme.TextFaint;
        _toolDot.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _toolDot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(_toolDot.BackColor);
            e.Graphics.FillEllipse(b, 0, 0, 6, 6);
        };

        _toolStatus.AutoSize = false;
        _toolStatus.Size = new Size(150, 16);
        _toolStatus.Font = Theme.Secondary;
        _toolStatus.ForeColor = Theme.TextDim;
        _toolStatus.TextAlign = ContentAlignment.MiddleLeft;
        _toolStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _toolStatus.Text = "checking";
        _toolStatus.BackColor = Color.Transparent;

        _jobsButton.Text = "Jobs";
        _jobsButton.Size = new Size(64, Theme.ButtonHeight);
        _jobsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _jobsButton.Click += (_, _) => _jobs.Toggle();

        _topBar.Controls.Add(_toolDot);
        _topBar.Controls.Add(_toolStatus);
        _topBar.Controls.Add(_jobsButton);
        _topBar.Resize += (_, _) => LayoutTopBar();
    }

    private void LayoutTopBar()
    {
        int y = (Theme.TopBarHeight - Theme.ButtonHeight) / 2;
        _jobsButton.Location = new Point(_topBar.Width - _jobsButton.Width - Theme.Pad, y);

        var width = TextRenderer.MeasureText(_toolStatus.Text, Theme.Secondary).Width + 4;
        _toolStatus.Width = width;
        _toolStatus.Location = new Point(_jobsButton.Left - width - 12, (Theme.TopBarHeight - 16) / 2);
        _toolDot.Location = new Point(_toolStatus.Left - 11, (Theme.TopBarHeight - 6) / 2);
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
        var width = TextRenderer.MeasureText(title, Theme.Emphasis).Width + 30;
        var nav = new NavButton
        {
            Text = title,
            Size = new Size(width, Theme.TopBarHeight),
            Location = new Point(Theme.Pad + _pages.Sum(p => p.button.Width), 0),
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

    internal void ShowPageForTesting(int index) => Show(index);
    internal void StressLibraryForTesting() => _library.StressForTesting();
    internal GrabPage GrabPageForTesting => _grab;

    internal Task StartupForTestingAsync()
    {
        _settings.LoadFromConfig();
        return RefreshToolStatusAsync(offerInstall: false);
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

    public async Task RefreshToolStatusAsync(bool offerInstall = false)
    {
        var (ytDlp, ffmpeg) = await _forge.Downloader.CheckToolsAsync();
        _settings.ShowToolStatus(ytDlp, ffmpeg);

        if (ytDlp is not null && ffmpeg is not null)
        {
            _toolStatus.Text = "tools ready";
            _toolStatus.ForeColor = Theme.TextDim;
            _toolDot.BackColor = Theme.Good;
            _toolDot.Invalidate();
            _grab.SetToolsReady(true);
            LayoutTopBar();
            return;
        }

        var missing = new List<string>();
        if (ytDlp is null) missing.Add("yt-dlp");
        if (ffmpeg is null) missing.Add("ffmpeg");

        _toolStatus.Text = "missing " + string.Join(" + ", missing);
        _toolStatus.ForeColor = Theme.Bad;
        _toolDot.BackColor = Theme.Bad;
        _toolDot.Invalidate();
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
                LayoutTopBar();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
