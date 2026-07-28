namespace TrackForge.UI;

/// <summary>Flat dark palette. No gradients, no glows, no emoji.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 15, 18);
    public static readonly Color Surface = Color.FromArgb(21, 24, 29);
    public static readonly Color SurfaceAlt = Color.FromArgb(27, 31, 37);
    public static readonly Color SurfaceHigh = Color.FromArgb(35, 40, 47);
    public static readonly Color Border = Color.FromArgb(46, 52, 61);

    public static readonly Color Text = Color.FromArgb(232, 235, 239);
    public static readonly Color TextDim = Color.FromArgb(146, 156, 168);
    public static readonly Color TextFaint = Color.FromArgb(98, 108, 120);

    public static readonly Color Accent = Color.FromArgb(200, 68, 47);
    public static readonly Color AccentHover = Color.FromArgb(222, 84, 61);
    public static readonly Color AccentDim = Color.FromArgb(120, 42, 30);

    public static readonly Color Good = Color.FromArgb(78, 173, 122);
    public static readonly Color Warn = Color.FromArgb(209, 156, 62);
    public static readonly Color Bad = Color.FromArgb(198, 84, 84);
    public static readonly Color Selection = Color.FromArgb(38, 46, 56);

    public static readonly Font UI = new("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font UIBold = new("Segoe UI Semibold", 9.5f, FontStyle.Regular);
    public static readonly Font Heading = new("Segoe UI Semibold", 13f, FontStyle.Regular);
    public static readonly Font Small = new("Segoe UI", 8.5f, FontStyle.Regular);
    public static readonly Font Mono = new("Consolas", 9f, FontStyle.Regular);

    /// <summary>Applies the palette down a control tree.</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBox tb:
                    tb.BackColor = SurfaceAlt;
                    tb.ForeColor = Text;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox cb:
                    cb.BackColor = SurfaceAlt;
                    cb.ForeColor = Text;
                    cb.FlatStyle = FlatStyle.Flat;
                    break;
                case CheckBox chk:
                    chk.ForeColor = Text;
                    chk.BackColor = Color.Transparent;
                    chk.FlatStyle = FlatStyle.Flat;
                    break;
                case Label lbl:
                    lbl.ForeColor = TextDim;
                    lbl.BackColor = Color.Transparent;
                    break;
                case Panel p:
                    if (p.BackColor == SystemColors.Control) p.BackColor = Surface;
                    break;
            }
            if (c.HasChildren) Apply(c);
        }
    }
}
