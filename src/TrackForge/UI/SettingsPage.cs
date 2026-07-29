using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>One card, two columns, accent eyebrows marking each section.</summary>
public sealed class SettingsPage : Panel
{
    private readonly ForgeService _forge;

    private readonly FlatTextBox _library = new(monospace: true);
    private readonly FlatTextBox _output = new(monospace: true);
    private readonly FlatTextBox _pattern = new(monospace: true);
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
    private readonly Label _tokens = new();
    private readonly Label _preview = new();
    private readonly Label _tools = new();
    private readonly Panel _toolBlock = new();
    private readonly FlatButton _installTools = new();

    private const int LabelWidth = 82;
    private const int RowStep = 30;
    private const int SectionGap = 16;

    public SettingsPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(Theme.Pad);
        AutoScroll = true;

        var card = new CardPanel { Dock = DockStyle.Top, Height = 460 };

        int left = 14;
        int right = 430;
        int y = 14;
        int ry = 14;

        // ---- left column -------------------------------------------------
        Eyebrow(card, "Paths", left, ref y);
        PathRow(card, "Library", _library, left, ref y);
        PathRow(card, "Save to", _output, left, ref y);

        y += SectionGap;
        Eyebrow(card, "Audio", left, ref y);
        ComboRow(card, "Format", _format, new[] { "mp3", "flac", "opus", "m4a" }, left, ref y, 92);
        ComboRow(card, "Bitrate", _bitrate, new[] { "320", "256", "192", "128" }, left, ref y, 92);

        y += SectionGap;
        Eyebrow(card, "Naming", left, ref y);
        TextRow(card, "Pattern", _pattern, left, ref y, 240);
        _pattern.Inner.TextChanged += (_, _) => UpdatePreview();

        _tokens.SetBounds(left + LabelWidth, y - 2, 320, 14);
        _tokens.Font = Theme.NumericSmall;
        _tokens.ForeColor = Theme.TextFaint;
        card.Controls.Add(_tokens);

        _preview.SetBounds(left + LabelWidth, y + 13, 320, 14);
        _preview.Font = Theme.NumericSmall;
        _preview.ForeColor = Theme.TextDim;
        card.Controls.Add(_preview);
        y += 34;

        var save = new FlatButton
        {
            Text = "Save settings",
            Primary = true,
            Size = new Size(102, Theme.PrimaryButtonHeight),
            Location = new Point(left, y),
        };
        save.Click += (_, _) => SaveToConfig();
        card.Controls.Add(save);

        _saved.SetBounds(left + 110, y + 5, 300, 16);
        _saved.Font = Theme.Secondary;
        _saved.ForeColor = Theme.Good;
        card.Controls.Add(_saved);
        y += 36;

        // ---- right column ------------------------------------------------
        Eyebrow(card, "Behaviour", right, ref ry);
        Check(card, _analyze, "Detect BPM and key on download", right, ref ry);
        Check(card, _autoArt, "Pick cover art automatically", right, ref ry);
        Check(card, _titleCase, "Force Title Case", right, ref ry);
        Check(card, _sourceUrl, "Store the source URL in the file", right, ref ry);
        Check(card, _djay, "Read BPM from djay's library", right, ref ry);

        ry += SectionGap;
        Eyebrow(card, "Lookup", right, ref ry);
        ComboRow(card, "iTunes store", _country, new[] { "CA", "US", "GB", "AU", "DE", "FR", "JP" },
                 right, ref ry, 72);
        ComboRow(card, "Cookies", _cookies,
                 new[] { "none", "opera", "chrome", "edge", "firefox", "brave", "vivaldi" },
                 right, ref ry, 92);

        ry += SectionGap;
        Eyebrow(card, "Tools", right, ref ry);

        _toolBlock.SetBounds(right, ry, 330, 54);
        _toolBlock.BackColor = Theme.ChromePanel;
        _toolBlock.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.ChromeBorder);
            e.Graphics.DrawRectangle(p, 0, 0, _toolBlock.Width - 1, _toolBlock.Height - 1);
        };

        _tools.SetBounds(8, 6, 314, 42);
        _tools.Font = Theme.NumericSmall;
        _tools.ForeColor = Theme.TextDim;
        _toolBlock.Controls.Add(_tools);
        card.Controls.Add(_toolBlock);
        ry += 60;

        _installTools.Text = "Install / update tools";
        _installTools.Size = new Size(140, Theme.ButtonHeight);
        _installTools.Location = new Point(right, ry);
        _installTools.Click += async (_, _) => await InstallToolsAsync();
        card.Controls.Add(_installTools);
        ry += 34;

        card.Height = Math.Max(y, ry) + 14;
        Controls.Add(card);
    }

    // ---------------------------------------------------------- layout

    private static void Eyebrow(Control parent, string text, int x, ref int y)
    {
        var label = new Label
        {
            Location = new Point(x, y),
            Size = new Size(260, 14),
            BackColor = Color.Transparent,
        };
        label.Paint += (_, e) => Theme.DrawEyebrow(e.Graphics, text, label.ClientRectangle);
        parent.Controls.Add(label);
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
            Font = Theme.Body,
            ForeColor = Theme.TextDim,
        });
        box.SetBounds(x + LabelWidth, y, width, Theme.FieldHeight);
        parent.Controls.Add(box);
        y += RowStep;
    }

    private void PathRow(Control parent, string caption, FlatTextBox box, int x, ref int y)
    {
        TextRow(parent, caption, box, x, ref y, 210);

        var browse = new FlatButton
        {
            Text = "…",
            Size = new Size(Theme.FieldHeight, Theme.FieldHeight),
            Location = new Point(x + LabelWidth + 216, y - RowStep),
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
            Font = Theme.Body,
            ForeColor = Theme.TextDim,
        });
        combo.SetBounds(x + LabelWidth, y, width, 22);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Theme.SurfaceAlt;
        combo.ForeColor = Theme.Text;
        combo.Font = Theme.Body;
        combo.Items.AddRange(items);
        parent.Controls.Add(combo);
        y += 28;
    }

    private static void Check(Control parent, CheckBox box, string text, int x, ref int y)
    {
        box.Text = text;
        box.Location = new Point(x, y);
        box.Size = new Size(320, 20);
        box.Font = Theme.Body;
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

    /// <summary>Live preview computed through the real NameFormatter token rules.</summary>
    private void UpdatePreview()
    {
        _tokens.Text = "{track} {tracknum} {title} {artist} {albumartist} {album} {year}";

        var sample = new Track
        {
            Title = "vicinity of obscenity",
            Artist = "system of a down",
            AlbumArtist = "system of a down",
            Album = "steal this album!",
            Year = "2002",
            TrackNumber = "9",
        };

        try { _preview.Text = NameFormatter.BuildFileName(sample, _pattern.Text, ".mp3"); }
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
