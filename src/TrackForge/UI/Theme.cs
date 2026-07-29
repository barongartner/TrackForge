namespace TrackForge.UI;

/// <summary>
/// Flat dark palette. No gradients, no glows, no emoji, square corners.
///
/// Accent discipline: <see cref="Accent"/> appears only on the active tab underline,
/// primary buttons, the 2px inset bar on a selected or best-match row, running
/// progress bars, and the uppercase section eyebrows in Settings. Nowhere else.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x0d, 0x0f, 0x12);
    public static readonly Color Surface = Color.FromArgb(0x15, 0x18, 0x1d);
    public static readonly Color SurfaceAlt = Color.FromArgb(0x1b, 0x1f, 0x25);
    public static readonly Color SurfaceHigh = Color.FromArgb(0x23, 0x28, 0x2f);
    public static readonly Color Border = Color.FromArgb(0x2e, 0x34, 0x3d);

    /// <summary>Divider between table rows.</summary>
    public static readonly Color RowDivider = Color.FromArgb(0x19, 0x1d, 0x23);
    /// <summary>Odd table rows, and non-best rows in Find.</summary>
    public static readonly Color RowOdd = Color.FromArgb(0x17, 0x1b, 0x21);

    /// <summary>Top bar, jobs dock, tool block - chrome rather than content.</summary>
    public static readonly Color ChromePanel = Color.FromArgb(0x12, 0x15, 0x1a);
    public static readonly Color ChromeBorder = Color.FromArgb(0x22, 0x27, 0x2f);

    public static readonly Color Text = Color.FromArgb(0xe8, 0xeb, 0xef);
    public static readonly Color TextStrong = Color.FromArgb(0xc1, 0xc8, 0xd1);
    public static readonly Color TextDim = Color.FromArgb(0x92, 0x9c, 0xa8);
    public static readonly Color TextMuted = Color.FromArgb(0x8a, 0x94, 0xa1);
    public static readonly Color TextFaint = Color.FromArgb(0x62, 0x6c, 0x78);
    public static readonly Color TextFainter = Color.FromArgb(0x5b, 0x65, 0x72);
    public static readonly Color TextCount = Color.FromArgb(0x7d, 0x87, 0x94);

    public static readonly Color Accent = Color.FromArgb(0xc8, 0x44, 0x2f);
    public static readonly Color AccentHover = Color.FromArgb(0xde, 0x54, 0x3d);
    public static readonly Color AccentDim = Color.FromArgb(0x78, 0x2a, 0x1e);

    public static readonly Color Good = Color.FromArgb(0x4e, 0xad, 0x7a);
    public static readonly Color Warn = Color.FromArgb(0xd1, 0x9c, 0x3e);
    public static readonly Color Bad = Color.FromArgb(0xc6, 0x54, 0x54);
    public static readonly Color Selection = Color.FromArgb(0x26, 0x2e, 0x38);

    /// <summary>Border of a grab card once its download finished.</summary>
    public static readonly Color DoneBorder = Color.FromArgb(0x2f, 0x4a, 0x3b);
    /// <summary>Wave-mark bars in the artwork placeholder.</summary>
    public static readonly Color WaveBar = Color.FromArgb(0x3a, 0x42, 0x50);

    // ---- type -------------------------------------------------------------
    // Segoe UI throughout; Consolas for every numeric and path, which is what
    // makes the table columns line up.

    public static readonly Font Body = new("Segoe UI", 9f);                       // 12px 400
    public static readonly Font Emphasis = new("Segoe UI Semibold", 9f);          // 12px 600
    public static readonly Font Secondary = new("Segoe UI", 8.25f);               // 11px 400
    public static readonly Font Eyebrow = new("Segoe UI Semibold", 7.5f);         // 10px 600
    public static readonly Font InspectorTitle = new("Segoe UI Semibold", 10.5f); // 14px 600

    public static readonly Font Numeric = new("Consolas", 9f);
    public static readonly Font NumericSmall = new("Consolas", 8.25f);

    // Kept so older call sites keep compiling.
    public static readonly Font UI = Body;
    public static readonly Font UIBold = Emphasis;
    public static readonly Font Small = Secondary;
    public static readonly Font Mono = Numeric;
    public static readonly Font Heading = InspectorTitle;

    // ---- metrics ----------------------------------------------------------

    public const int Gap = 6;
    public const int Pad = 10;
    public const int FieldHeight = 24;
    public const int RowHeight = 26;
    public const int HeaderHeight = 24;
    public const int ButtonHeight = 24;
    public const int PrimaryButtonHeight = 26;
    public const int TitleBarHeight = 32;
    public const int TopBarHeight = 38;
    public const int JobsDockWidth = 320;
    public const int GrabArtSize = 88;

    /// <summary>Uppercase section label: 10px 600, letter-spaced, accent.</summary>
    public static void DrawEyebrow(Graphics g, string text, Rectangle bounds)
    {
        var spaced = string.Join(" ", text.ToUpperInvariant().ToCharArray());
        TextRenderer.DrawText(g, spaced, Eyebrow, bounds, Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

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
