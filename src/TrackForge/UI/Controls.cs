using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TrackForge.UI;

/// <summary>Flat, square button. Primary = filled accent, otherwise outlined.</summary>
public sealed class FlatButton : Button
{
    private bool _hover;

    public bool Primary { get; set; }
    public bool Danger { get; set; }
    /// <summary>Filter chip: accent fill when active, hairline box when not.</summary>
    public bool Chip { get; set; }

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.Body;
        Height = Theme.ButtonHeight;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill, border, text;
        var font = Font;

        if (!Enabled)
        {
            fill = Theme.SurfaceAlt; border = Theme.Border; text = Theme.TextFaint;
        }
        else if (Primary)
        {
            fill = _hover ? Theme.AccentHover : Theme.Accent;
            border = fill;
            text = Color.White;
            font = Theme.Emphasis;
        }
        else if (Chip)
        {
            fill = _hover ? Theme.SurfaceHigh : Theme.SurfaceAlt;
            border = Theme.Border;
            text = Theme.TextDim;
        }
        else if (Danger)
        {
            fill = _hover ? Theme.SurfaceHigh : Color.Transparent;
            border = Theme.Bad;
            text = Theme.Bad;
        }
        else
        {
            fill = _hover ? Theme.SurfaceHigh : Theme.SurfaceAlt;
            border = Theme.Border;
            text = Theme.Text;
        }

        using (var b = new SolidBrush(fill)) g.FillRectangle(b, rect);
        using (var p = new Pen(border)) g.DrawRectangle(p, rect);

        TextRenderer.DrawText(g, Text, font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Page tab. 2px accent underline when active.</summary>
public sealed class NavButton : Button
{
    private bool _hover;
    public bool Active { get; set; }

    public NavButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.ChromePanel);

        if (_hover && !Active)
            using (var b = new SolidBrush(Theme.SurfaceAlt))
                g.FillRectangle(b, ClientRectangle);

        var colour = Active || _hover ? Theme.Text : Theme.TextDim;
        TextRenderer.DrawText(g, Text, Active ? Theme.Emphasis : Theme.Body, ClientRectangle, colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (Active)
            using (var b = new SolidBrush(Theme.Accent))
                g.FillRectangle(b, new Rectangle(0, Height - 2, Width, 2));
    }
}

/// <summary>Card container with a 1px hairline.</summary>
public class CardPanel : Panel
{
    public Color BorderColour { get; set; } = Theme.Border;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var p = new Pen(BorderColour);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>Square text box with a placeholder and a focus-accent hairline.</summary>
public sealed class FlatTextBox : Panel
{
    public TextBox Inner { get; }

    public FlatTextBox(bool multiline = false, bool monospace = false)
    {
        BackColor = Theme.SurfaceAlt;
        Padding = new Padding(7, multiline ? 5 : 4, 7, multiline ? 5 : 4);
        Height = multiline ? 52 : Theme.FieldHeight;

        Inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            Font = monospace ? Theme.Numeric : Theme.Body,
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            WordWrap = false,
        };
        Controls.Add(Inner);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override string Text
    {
        get => Inner.Text;
        set => Inner.Text = value;
    }

    public string PlaceholderText
    {
        get => Inner.PlaceholderText;
        set => Inner.PlaceholderText = value;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Inner.Focused ? Theme.Accent : Theme.Border);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Inner.GotFocus += (_, _) => Invalidate();
        Inner.LostFocus += (_, _) => Invalidate();
    }
}

/// <summary>Thin square progress bar.</summary>
public sealed class FlatProgress : Control
{
    private double _value;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public Color BarColour { get; set; } = Theme.Accent;

    public FlatProgress()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 3;
        BackColor = Theme.ChromeBorder;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int w = (int)(Width * _value / 100.0);
        if (w <= 0) return;
        using var b = new SolidBrush(BarColour);
        e.Graphics.FillRectangle(b, 0, 0, w, Height);
    }
}

/// <summary>Five-bar wave mark used as the artwork placeholder and app tile.</summary>
public sealed class WaveMark : Control
{
    private static readonly double[] Heights = { 0.30, 0.62, 0.44, 0.80, 0.36 };
    private const int AccentBar = 3;

    public WaveMark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.SurfaceAlt;
    }

    public Image? Image { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (Image is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Image, new Rectangle(1, 1, Width - 2, Height - 2));
        }
        else
        {
            int barWidth = Math.Max(3, Width / 12);
            int spacing = Math.Max(2, barWidth / 2);
            int total = Heights.Length * barWidth + (Heights.Length - 1) * spacing;
            int x = (Width - total) / 2;
            int baseline = (int)(Height * 0.74);

            for (int i = 0; i < Heights.Length; i++)
            {
                int h = (int)(Height * 0.48 * Heights[i] / 0.80);
                var colour = i == AccentBar ? Theme.Accent : Theme.WaveBar;
                using var b = new SolidBrush(colour);
                g.FillRectangle(b, x, baseline - h, barWidth, h);
                x += barWidth + spacing;
            }
        }

        using var p = new Pen(Theme.Border);
        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>
/// Table. 24px header, 26px rows, 1px dividers, alternating row fills, a 2px accent
/// inset on the selected row, and Consolas for numeric columns so they line up.
/// </summary>
public class DarkListView : ListView
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? app, string? id);

    /// <summary>Column indexes rendered in Consolas.</summary>
    public HashSet<int> NumericColumns { get; } = new();

    /// <summary>Column indexes shown dimmer than body text.</summary>
    public HashSet<int> DimColumns { get; } = new();

    public int SortColumn { get; set; } = -1;
    public bool SortAscending { get; set; } = true;

    /// <summary>Per-row colour override, by column. Return null to use the default.</summary>
    public Func<ListViewItem, int, Color?>? ColourFor { get; set; }

    /// <summary>Rows that carry the 2px accent inset even when not selected.</summary>
    public Func<ListViewItem, bool>? IsAccented { get; set; }

    public DarkListView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        OwnerDraw = true;
        MultiSelect = true;
        GridLines = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SetWindowTheme(Handle, "DarkMode_Explorer", null); } catch { }
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (var b = new SolidBrush(Theme.SurfaceAlt)) e.Graphics.FillRectangle(b, e.Bounds);
        using (var p = new Pen(Theme.Border))
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        bool active = e.ColumnIndex == SortColumn;
        var colour = active ? Theme.Text : Theme.TextDim;
        var text = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height);

        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.Secondary, text, colour,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (!active) return;

        var arrow = SortAscending ? "▲" : "▼";
        var arrowRect = new Rectangle(e.Bounds.Right - 14, e.Bounds.Y, 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, arrow, Theme.Eyebrow, arrowRect, Theme.Accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) => e.DrawDefault = false;

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var item = e.Item;
        if (item is null) return;

        bool selected = item.Selected;
        bool accented = selected || (IsAccented?.Invoke(item) ?? false);

        var background = selected ? Theme.Selection
            : (e.ItemIndex % 2 == 0 ? Theme.Surface : Theme.RowOdd);

        using (var b = new SolidBrush(background)) e.Graphics.FillRectangle(b, e.Bounds);

        // 1px divider under every row.
        using (var p = new Pen(Theme.RowDivider))
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        if (accented && e.ColumnIndex == 0)
            using (var b = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.X, e.Bounds.Y, 2, e.Bounds.Height - 1));

        var text = e.SubItem?.Text ?? "";
        var colour = ColourFor?.Invoke(item, e.ColumnIndex)
                     ?? (DimColumns.Contains(e.ColumnIndex) ? Theme.TextDim : Theme.Text);
        var font = NumericColumns.Contains(e.ColumnIndex) ? Theme.Numeric : Theme.Body;

        var bounds = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 11, e.Bounds.Height - 1);
        TextRenderer.DrawText(e.Graphics, text, font, bounds, colour,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
