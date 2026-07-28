using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Two compact columns rather than one tall sparse one.</summary>
public sealed class SettingsPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _library = new();
    private readonly FlatTextBox _output = new();
    private readonly FlatTextBox _pattern = new();
    private readonly ComboBox _format = new();
    private readonly ComboBox _bitrate = new();
    private readonly ComboBox _cookies = new();
    private readonly ComboBox _country = new();
    private readonly CheckBox _analyze = new();
    private readonly CheckBox _autoArt = new();
    private readonly CheckBox _titleCase = new();
    private readonly CheckBox _sourceUrl = new();
    private readonly CheckBox _djay = new();
    private readonly Label _saved = new();
    private readonly Label _tools = new();
    private readonly Label _preview = new();
    private readonly FlatButton _installTools = new();

    private const int LabelWidth = 96;
    private const int RowStep = 30;

    public SettingsPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(Theme.Pad);
        AutoScroll = true;

        var card = new CardPanel { Dock = DockStyle.Top, Height = 430 };

        // ---- left column -------------------------------------------------
        int left = Theme.Pad + 4;
        int y = 12;

        Heading(card, "PATHS", left, ref y);
        PathRow(card, "Library", _library, left, ref y);
        PathRow(card, "Save to", _output, left, ref y);

        y += 6;
        Heading(card, "AUDIO", left, ref y);
        ComboRow(card, "Format", _format, new[] { "mp3", "flac", "opus", "m4a" }, left, ref y, 90);
        ComboRow(card, "Bitrate", _bitrate, new[] { "320", "256", "192", "128" }, left, ref y, 90);

        y += 6;
        Heading(card, "NAMING", left, ref y);
        TextRow(card, "Pattern", _pattern, left, ref y, 250);
        _pattern.Inner.TextChanged += (_, _) => UpdatePreview();

        _preview.SetBounds(left + LabelWidth, y - 2, 300, 30);
        _preview.Font = Theme.Small;
        _preview.ForeColor = Theme.TextFaint;
        card.Controls.Add(_preview);
        y += 34;

        // ---- right column ------------------------------------------------
        int right = 410;
        int ry = 12;

        Heading(card, "BEHAVIOUR", right, ref ry);
        Check(card, _analyze, "Detect BPM and key on download", right, ref ry);
        Check(card, _autoArt, "Pick cover art automatically", right, ref ry);
        Check(card, _titleCase, "Force Title Case", right, ref ry);
        Check(card, _sourceUrl, "Store the source URL in the file", right, ref ry);
        Check(card, _djay, "Read BPM from djay's library", right, ref ry);

        ry += 8;
        Heading(card, "LOOKUP", right, ref ry);
        ComboRow(card, "iTunes store", _country, new[] { "CA", "US", "GB", "AU", "DE", "FR", "JP" },
                 right, ref ry, 70);
        ComboRow(card, "Cookies", _cookies,
                 new[] { "none", "opera", "chrome", "edge", "firefox", "brave", "vivaldi" },
                 right, ref ry, 100);

        ry += 8;
        Heading(card, "TOOLS", right, ref ry);
        _tools.SetBounds(right, ry, 340, 46);
        _tools.Font = Theme.Mono;
        _tools.ForeColor = Theme.TextDim;
        card.Controls.Add(_tools);
        ry += 50;

        _installTools.Text = "Install / update tools";
        _installTools.Size = new Size(150, Theme.ButtonHeight);
        _installTools.Location = new Point(right, ry);
        _installTools.Click += async (_, _) => await InstallToolsAsync();
        card.Controls.Add(_installTools);

        // ---- save --------------------------------------------------------
        int saveY = Math.Max(y, ry + 36) + 4;

        var save = new FlatButton
        {
            Text = "Save settings",
            Primary = true,
            Size = new Size(112, Theme.ButtonHeight),
            Location = new Point(left, saveY),
        };
        save.Click += (_, _) => SaveToConfig();

        _saved.SetBounds(left + 122, saveY + 5, 460, 16);
        _saved.Font = Theme.Small;
        _saved.ForeColor = Theme.Good;

        card.Controls.Add(save);
        card.Controls.Add(_saved);
        card.Height = saveY + 44;

        Controls.Add(card);
    }

    // ---------------------------------------------------------- layout

    private static void Heading(Control parent, string text, int x, ref int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(260, 15),
            Font = Theme.Small,
            ForeColor = Theme.Accent,
        });
        y += 20;
    }

    private static void TextRow(Control parent, string caption, FlatTextBox box,
                                int x, ref int y, int width)
    {
        parent.Controls.Add(new Label
        {
            Text = caption,
            Location = new Point(x, y + 4),
            Size = new Size(LabelWidth - 6, 16),
            ForeColor = Theme.TextDim,
        });
        box.SetBounds(x + LabelWidth, y, width, 24);
        parent.Controls.Add(box);
        y += RowStep;
    }

    private void PathRow(Control parent, string caption, FlatTextBox box, int x, ref int y)
    {
        TextRow(parent, caption, box, x, ref y, 190);

        var browse = new FlatButton
        {
            Text = "...",
            Size = new Size(28, 24),
            Location = new Point(x + LabelWidth + 196, y - RowStep),
        };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = box.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
        };
        parent.Controls.Add(browse);
    }

    private static void ComboRow(Control parent, string caption, ComboBox combo,
                                 string[] items, int x, ref int y, int width)
    {
        parent.Controls.Add(new Label
        {
            Text = caption,
            Location = new Point(x, y + 3),
            Size = new Size(LabelWidth - 6, 16),
            ForeColor = Theme.TextDim,
        });
        combo.SetBounds(x + LabelWidth, y, width, 22);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Theme.SurfaceAlt;
        combo.ForeColor = Theme.Text;
        combo.Items.AddRange(items);
        parent.Controls.Add(combo);
        y += 28;
    }

    private static void Check(Control parent, CheckBox box, string text, int x, ref int y)
    {
        box.Text = text;
        box.Location = new Point(x, y);
        box.Size = new Size(330, 20);
        box.ForeColor = Theme.Text;
        box.FlatStyle = FlatStyle.Flat;
        parent.Controls.Add(box);
        y += 24;
    }

    // ------------------------------------------------------------- data

    public void LoadFromConfig()
    {
        var c = _forge.Config;
        _library.Text = c.LibraryFolder;
        _output.Text = c.OutputFolder;
        _pattern.Text = c.FilenamePattern;
        _format.SelectedItem = c.Format;
        _bitrate.SelectedItem = c.Bitrate;
        _country.SelectedItem = c.ItunesCountry;
        _cookies.SelectedItem = string.IsNullOrWhiteSpace(c.CookiesFromBrowser) ? "none" : c.CookiesFromBrowser;
        _analyze.Checked = c.AnalyzeBpmAndKey;
        _autoArt.Checked = c.AutoArt;
        _titleCase.Checked = c.ForceTitleCase;
        _sourceUrl.Checked = c.WriteSourceUrl;
        _djay.Checked = c.ImportDjayData;
        UpdatePreview();
    }

    private void SaveToConfig()
    {
        var c = _forge.Config;
        c.LibraryFolder = _library.Text.Trim();
        c.OutputFolder = _output.Text.Trim();
        c.FilenamePattern = _pattern.Text.Trim();
        c.Format = _format.SelectedItem?.ToString() ?? "mp3";
        c.Bitrate = _bitrate.SelectedItem?.ToString() ?? "320";
        c.ItunesCountry = _country.SelectedItem?.ToString() ?? "CA";
        var cookies = _cookies.SelectedItem?.ToString() ?? "none";
        c.CookiesFromBrowser = cookies == "none" ? "" : cookies;
        c.AnalyzeBpmAndKey = _analyze.Checked;
        c.AutoArt = _autoArt.Checked;
        c.ForceTitleCase = _titleCase.Checked;
        c.WriteSourceUrl = _sourceUrl.Checked;
        c.ImportDjayData = _djay.Checked;

        _forge.SaveConfig();
        _saved.Text = "Saved. Rescan the library if you changed the folder.";
    }

    private void UpdatePreview()
    {
        var sample = new Track
        {
            Title = "vicinity of obscenity",
            Artist = "system of a down",
            AlbumArtist = "system of a down",
            Album = "steal this album!",
            Year = "2002",
            TrackNumber = "9",
        };

        try
        {
            _preview.Text = "{track} {tracknum} {title} {artist} {albumartist} {album} {year}\r\n"
                          + NameFormatter.BuildFileName(sample, _pattern.Text, ".mp3");
        }
        catch { _preview.Text = "That pattern is not valid."; }
    }

    public void ShowToolStatus(string? ytDlp, string? ffmpeg)
    {
        _tools.Text = $"yt-dlp  {ytDlp ?? "not found"}\r\n" +
                      $"ffmpeg  {(ffmpeg is null ? "not found" : "ok")}\r\n" +
                      $"folder  {ToolInstaller.ToolsDirectory}";
        _tools.ForeColor = ytDlp is null || ffmpeg is null ? Theme.Bad : Theme.TextDim;
        _installTools.Primary = ytDlp is null || ffmpeg is null;
        _installTools.Invalidate();
    }

    private async Task InstallToolsAsync()
    {
        var missing = new List<string> { "yt-dlp", "ffmpeg" };
        using var dialog = new ToolSetupDialog(_forge, missing);
        dialog.ShowDialog(this);

        if (dialog.InstalledSomething && FindForm() is MainForm main)
            await main.RefreshToolStatusAsync();
    }
}
