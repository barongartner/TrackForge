using TrackForge.Core;

namespace TrackForge.UI;

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

    public SettingsPage(ForgeService forge)
    {
        _forge = forge;
        BackColor = Theme.Background;
        Padding = new Padding(18, 16, 18, 16);
        AutoScroll = true;

        var card = new CardPanel { Dock = DockStyle.Top, Height = 640, Padding = new Padding(20) };

        int y = 18;
        AddHeading(card, "Paths", ref y);
        AddPathRow(card, "Library folder", _library, ref y);
        AddPathRow(card, "Save downloads to", _output, ref y);

        y += 10;
        AddHeading(card, "Audio", ref y);
        AddCombo(card, "Format", _format, new[] { "mp3", "flac", "opus", "m4a" }, ref y, 120);
        AddCombo(card, "MP3 bitrate", _bitrate, new[] { "320", "256", "192", "128" }, ref y, 120);

        y += 10;
        AddHeading(card, "Naming", ref y);
        AddTextRow(card, "Filename pattern", _pattern, ref y, 340);
        _pattern.Inner.TextChanged += (_, _) => UpdatePreview();

        _preview.Location = new Point(200, y);
        _preview.Size = new Size(600, 34);
        _preview.Font = Theme.Small;
        _preview.ForeColor = Theme.TextFaint;
        card.Controls.Add(_preview);
        y += 42;

        y += 6;
        AddHeading(card, "Behaviour", ref y);
        AddCheck(card, _analyze, "Detect BPM and musical key from the audio on every download", ref y);
        AddCheck(card, _autoArt, "Pick the best cover art automatically after a lookup", ref y);
        AddCheck(card, _titleCase, "Force Title Case on title, artist and album", ref y);
        AddCheck(card, _sourceUrl, "Store the source URL inside the file", ref y);
        AddCheck(card, _djay, "Read BPM that Algoriddim djay has already analysed", ref y);

        y += 10;
        AddCombo(card, "iTunes store", _country, new[] { "CA", "US", "GB", "AU", "DE", "FR", "JP" }, ref y, 90);
        AddCombo(card, "Cookies from browser", _cookies,
            new[] { "none", "opera", "chrome", "edge", "firefox", "brave", "vivaldi" }, ref y, 140);

        var cookieHint = new Label
        {
            Text = "Only needed if a link asks you to sign in.",
            Location = new Point(200, y),
            Size = new Size(500, 18),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
        };
        card.Controls.Add(cookieHint);
        y += 32;

        var save = new FlatButton { Text = "Save settings", Primary = true, Size = new Size(130, 32), Location = new Point(200, y) };
        save.Click += (_, _) => SaveToConfig();

        _saved.Location = new Point(342, y + 7);
        _saved.Size = new Size(400, 18);
        _saved.ForeColor = Theme.Good;

        card.Controls.Add(save);
        card.Controls.Add(_saved);
        y += 48;

        AddHeading(card, "Tools", ref y);
        _tools.Location = new Point(24, y);
        _tools.Size = new Size(760, 74);
        _tools.Font = Theme.Mono;
        _tools.ForeColor = Theme.TextDim;
        card.Controls.Add(_tools);

        Controls.Add(card);
    }

    // ---------------------------------------------------------- layout

    private static void AddHeading(Control parent, string text, ref int y)
    {
        var label = new Label
        {
            Text = text.ToUpperInvariant(),
            Location = new Point(24, y),
            Size = new Size(300, 18),
            Font = Theme.Small,
            ForeColor = Theme.Accent,
        };
        parent.Controls.Add(label);
        y += 26;
    }

    private static void AddTextRow(Control parent, string caption, FlatTextBox box, ref int y, int width)
    {
        var label = new Label
        {
            Text = caption,
            Location = new Point(24, y + 6),
            Size = new Size(170, 18),
            ForeColor = Theme.TextDim,
        };
        box.Location = new Point(200, y);
        box.Size = new Size(width, 30);

        parent.Controls.Add(label);
        parent.Controls.Add(box);
        y += 40;
    }

    private void AddPathRow(Control parent, string caption, FlatTextBox box, ref int y)
    {
        AddTextRow(parent, caption, box, ref y, 440);

        var browse = new FlatButton { Text = "Browse", Size = new Size(84, 30), Location = new Point(648, y - 40) };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = box.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
        };
        parent.Controls.Add(browse);
    }

    private static void AddCombo(Control parent, string caption, ComboBox combo,
                                 string[] items, ref int y, int width)
    {
        var label = new Label
        {
            Text = caption,
            Location = new Point(24, y + 4),
            Size = new Size(170, 18),
            ForeColor = Theme.TextDim,
        };
        combo.Location = new Point(200, y);
        combo.Size = new Size(width, 26);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Theme.SurfaceAlt;
        combo.ForeColor = Theme.Text;
        combo.Items.AddRange(items);

        parent.Controls.Add(label);
        parent.Controls.Add(combo);
        y += 36;
    }

    private static void AddCheck(Control parent, CheckBox box, string text, ref int y)
    {
        box.Text = text;
        box.Location = new Point(200, y);
        box.Size = new Size(560, 22);
        box.ForeColor = Theme.Text;
        box.FlatStyle = FlatStyle.Flat;
        parent.Controls.Add(box);
        y += 28;
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
        _saved.Text = "Saved to " + AppConfig.ConfigPath;
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
            var name = NameFormatter.BuildFileName(sample, _pattern.Text, ".mp3");
            _preview.Text = $"Tokens: {{track}} {{tracknum}} {{title}} {{artist}} {{albumartist}} {{album}} {{year}}\n" +
                            $"Preview: {name}";
        }
        catch
        {
            _preview.Text = "That pattern is not valid.";
        }
    }

    public void ShowToolStatus(string? ytDlp, string? ffmpeg)
    {
        _tools.Text =
            $"yt-dlp   {(ytDlp is null ? "NOT FOUND   ->  pip install -U yt-dlp" : ytDlp)}\n" +
            $"ffmpeg   {(ffmpeg is null ? "NOT FOUND   ->  winget install Gyan.FFmpeg" : ffmpeg)}\n" +
            $"config   {AppConfig.ConfigPath}";
        _tools.ForeColor = ytDlp is null || ffmpeg is null ? Theme.Bad : Theme.TextDim;
    }
}
