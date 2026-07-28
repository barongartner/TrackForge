using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TrackForge.UI;

/// <summary>Flat button. Primary = filled accent, otherwise outlined.</summary>
public sealed class FlatButton : Button
{
    private bool _hover;

    public bool Primary { get; set; }
    public bool Danger { get; set; }

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.UI;
        Height = 30;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill, border, text;

        if (!Enabled)
        {
            fill = Theme.SurfaceAlt; border = Theme.Border; text = Theme.TextFaint;
        }
        else if (Primary)
        {
            fill = _hover ? Theme.AccentHover : Theme.Accent;
            border = fill;
            text = Color.White;
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

        TextRenderer.DrawText(g, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Top-bar navigation button with an accent underline when active.</summary>
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
        Font = Theme.UI;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);

        if (_hover && !Active)
            using (var b = new SolidBrush(Theme.Surface))
                g.FillRectangle(b, ClientRectangle);

        var colour = Active ? Theme.Text : (_hover ? Theme.Text : Theme.TextDim);
        TextRenderer.DrawText(g, Text, Active ? Theme.UIBold : Theme.UI, ClientRectangle, colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (Active)
            using (var b = new SolidBrush(Theme.Accent))
                g.FillRectangle(b, new Rectangle(0, Height - 2, Width, 2));
    }
}

/// <summary>Card-style container with a one-pixel border.</summary>
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

/// <summary>Text box with a placeholder and a flat dark border.</summary>
public sealed class FlatTextBox : Panel
{
    public TextBox Inner { get; }

    public FlatTextBox(bool multiline = false)
    {
        BackColor = Theme.SurfaceAlt;
        Padding = new Padding(8, multiline ? 6 : 5, 8, multiline ? 6 : 5);
        Height = multiline ? 90 : 30;

        Inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            Font = Theme.UI,
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

/// <summary>Thin flat progress bar.</summary>
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
        Height = 4;
        BackColor = Theme.SurfaceHigh;
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

/// <summary>Small pill-shaped status label.</summary>
public sealed class Pill : Control
{
    public Color PillColour { get; set; } = Theme.TextDim;

    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.Small;
        Height = 20;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Rounded(rect, Height / 2))
        {
            using var b = new SolidBrush(Theme.SurfaceAlt);
            g.FillPath(b, path);
            using var p = new Pen(PillColour);
            g.DrawPath(p, path);
        }
        TextRenderer.DrawText(g, Text, Font, rect, PillColour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>ListView with a dark owner-drawn header and no flicker.</summary>
public class DarkListView : ListView
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? app, string? id);

    public DarkListView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.UI;
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

        var text = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.Small, text, Theme.TextDim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) => e.DrawDefault = false;
}
