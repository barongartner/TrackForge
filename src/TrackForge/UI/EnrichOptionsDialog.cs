using TrackForge.Core;

namespace TrackForge.UI;

/// <summary>Picks which fields a bulk "fill tags from online" run is allowed to touch.</summary>
public sealed class EnrichOptionsDialog : Form
{
    private readonly Dictionary<string, CheckBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _overwrite = new();
    private readonly CheckBox _art = new();
    private readonly CheckBox _analyze = new();
    private readonly CheckBox _rename = new();

    public ForgeService.EnrichOptions Options { get; private set; } =
        new(false, true, false, false, Array.Empty<string>());

    public EnrichOptionsDialog(int trackCount)
    {
        Text = "Fill tags from online";
        Size = new Size(480, 470);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UI;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        var header = new Label
        {
            Text = $"{trackCount} track(s) selected. iTunes, Deezer and MusicBrainz get searched,\n" +
                   "and the highest-scoring match wins.",
            Location = new Point(20, 18),
            Size = new Size(420, 40),
            ForeColor = Theme.TextDim,
        };

        var fieldsLabel = new Label
        {
            Text = "FIELDS TO FILL",
            Location = new Point(20, 68),
            Size = new Size(200, 16),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
        };

        var fields = new (string key, string label, bool on)[]
        {
            ("album", "Album", true),
            ("albumartist", "Album artist", true),
            ("year", "Year", true),
            ("genre", "Genre", true),
            ("track", "Track number", true),
            ("disc", "Disc number", true),
            ("title", "Title", false),
            ("artist", "Artist", false),
            ("isrc", "ISRC", true),
            ("publisher", "Publisher", false),
        };

        int x = 20, y = 92;
        foreach (var (key, label, on) in fields)
        {
            var box = new CheckBox
            {
                Text = label,
                Checked = on,
                Location = new Point(x, y),
                Size = new Size(200, 22),
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
            };
            _fieldBoxes[key] = box;
            Controls.Add(box);

            if (x == 20) x = 240;
            else { x = 20; y += 26; }
        }

        int optionsTop = y + 40;

        var optionsLabel = new Label
        {
            Text = "OPTIONS",
            Location = new Point(20, optionsTop - 24),
            Size = new Size(200, 16),
            Font = Theme.Small,
            ForeColor = Theme.TextFaint,
        };

        Configure(_overwrite, "Overwrite fields that already have a value", optionsTop, false);
        Configure(_art, "Download and embed cover art where it's missing", optionsTop + 26, true);
        Configure(_analyze, "Analyse BPM and key from the audio (slower)", optionsTop + 52, false);
        Configure(_rename, "Rename files to match the naming pattern", optionsTop + 78, false);

        var warning = new Label
        {
            Text = "Tags are written straight to the files. There is no undo.",
            Location = new Point(20, optionsTop + 112),
            Size = new Size(420, 18),
            Font = Theme.Small,
            ForeColor = Theme.Warn,
        };

        var run = new FlatButton
        {
            Text = "Run",
            Primary = true,
            Size = new Size(96, 32),
            Location = new Point(252, optionsTop + 140),
        };
        run.Click += (_, _) =>
        {
            Options = new ForgeService.EnrichOptions(
                _overwrite.Checked, _art.Checked, _analyze.Checked, _rename.Checked,
                _fieldBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList());
            DialogResult = DialogResult.OK;
        };

        var cancel = new FlatButton
        {
            Text = "Cancel",
            Size = new Size(88, 32),
            Location = new Point(356, optionsTop + 140),
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { header, fieldsLabel, optionsLabel, warning, run, cancel });
        AcceptButton = run;
        CancelButton = cancel;
    }

    private void Configure(CheckBox box, string text, int top, bool on)
    {
        box.Text = text;
        box.Checked = on;
        box.Location = new Point(20, top);
        box.Size = new Size(420, 22);
        box.ForeColor = Theme.Text;
        box.FlatStyle = FlatStyle.Flat;
        Controls.Add(box);
    }
}
