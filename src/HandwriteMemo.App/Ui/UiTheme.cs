using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HandwriteMemo.App;

// Desktop colors and common drawing helpers; VR surfaces use their own palette.
internal static class UiTheme
{
    internal static readonly Icon AppIcon = LoadAppIcon();

    private static Icon LoadAppIcon()
    {
        using var stream = typeof(UiTheme).Assembly.GetManifestResourceStream("HnMemo.ico")!;
        return new Icon(stream);
    }

    internal const string FontFamily = "Yu Gothic UI";
    // Shared for the process lifetime; WinForms controls borrow these fonts.
    internal static readonly Font DesktopFont = new(FontFamily, 10);
    internal static readonly Font DesktopBoldFont = new(FontFamily, 10, FontStyle.Bold);
    internal static readonly Font DesktopSmallFont = new(FontFamily, 9);
    internal static readonly Font DesktopLargeFont = new(FontFamily, 12);
    internal static Color Background => UiDesign.DesktopCanvas;
    internal static Color Surface => UiDesign.DesktopSurface;
    internal static Color Text => UiDesign.Ink;
    internal static Color Secondary => UiDesign.Muted;
    internal static Color Border => UiDesign.DesktopBorder;
    internal static Color Accent => UiDesign.Accent;
    internal static Color Selection { get; } = Accent;
    internal static Color Hover => UiDesign.DesktopHover;
    internal static Color Pressed => UiDesign.DesktopPressed;
    internal static Color AccentInk => UiDesign.AccentInk;
    internal static Color OnAccent => UiDesign.OnAccent;
    internal static Color ErrorInk => UiDesign.ErrorInk;
    internal static Color Grid { get; } = Color.FromArgb(240, 240, 240);

    internal static GraphicsPath Rounded(RectangleF r, float radius = 6)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void Tile(Graphics g, RectangleF r, Color fill, Color border, float width = 1, float radius = 6)
    {
        using var shape = Rounded(r, radius);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, width);
        g.FillPath(brush, shape);
        g.DrawPath(pen, shape);
    }

    // DWM supplies the real Windows 11 title-bar material and window corners. WinForms
    // content stays opaque: extending glass over GDI child controls can corrupt their text.
    // No Windows App SDK, private attributes or replacement window frame is required.
    internal static void ApplyWindow(Form window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;
        int light = 0, round = 2, mica = 2;
        _ = DwmSetWindowAttribute(window.Handle, 20, ref light, sizeof(int));
        _ = DwmSetWindowAttribute(window.Handle, 33, ref round, sizeof(int));
        int result = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemInformation.HighContrast
            ? DwmSetWindowAttribute(window.Handle, 38, ref mica, sizeof(int)) : -1;
        if (result != 0)
        {
            int fallback = ColorTranslator.ToWin32(Background);
            _ = DwmSetWindowAttribute(window.Handle, 35, ref fallback, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static void StyleMenu(ContextMenuStrip menu)
    {
        menu.Renderer = new MenuRenderer { RoundedEdges = true };
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Font = DesktopFont;
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }

    private sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColors())
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var fill = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Accent : Surface);
            e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Secondary : e.Item.Selected ? OnAccent : Text;
            base.OnRenderItemText(e);
        }
    }
}

// Retain Button/CheckBox semantics, focus, Space activation and accessibility.
internal sealed class DesignButton : Button
{
    private bool _hovered, _pressed;
    /// <summary>A glyph drawn before the text at the size of the toolbar tile icons.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ToolbarIcon? LeadingIcon { get; init; }
    internal DesignButton()
    {
        FlatStyle = FlatStyle.Flat;
        Padding = new(8, 4, 8, 4);
        Margin = new(4);
        MinimumSize = new(0, 32);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseCaptureChanged(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnKeyUp(e);
    }
    protected override void OnLostFocus(EventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnLostFocus(e);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        return LeadingIcon is null ? size : new(size.Width + DesignButtonPaint.IconSide(this), size.Height);
    }
    protected override void OnPaint(PaintEventArgs e) => DesignButtonPaint.Draw(this, e.Graphics, _hovered, _pressed, false, Focused && ShowFocusCues, LeadingIcon);
}

internal static class DesignButtonPaint
{
    // Toolbar tiles draw their glyph in two thirds of the tile; a leading icon matches that size.
    internal static int IconSide(Control control) => Math.Max(control.MinimumSize.Height, control.Height) * 2 / 3;

    internal static void Draw(ButtonBase control, Graphics g, bool hovered, bool pressed, bool selected, bool focus, ToolbarIcon? icon = null)
    {
        bool hc = SystemInformation.HighContrast;
        float scale = control.DeviceDpi / 96f;
        g.Clear(control.Parent?.BackColor ?? UiTheme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = hc ? selected ? SystemColors.Highlight : SystemColors.Control :
            !control.Enabled ? UiTheme.Background : selected ? UiTheme.Accent : pressed ? UiTheme.Pressed : hovered ? UiTheme.Hover : UiTheme.Surface;
        Color border = hc ? SystemColors.WindowText : selected || focus || hovered ? UiTheme.Accent : UiTheme.Border;
        Color text = !control.Enabled ? SystemColors.GrayText : hc ? selected ? SystemColors.HighlightText : SystemColors.ControlText : selected ? UiTheme.OnAccent : UiTheme.Text;
        UiTheme.Tile(g, new RectangleF(scale, scale, control.Width - 2 * scale, control.Height - 2 * scale),
            fill, border, (selected || hovered || focus ? 2 : 1) * scale, UiDesign.DesktopRadius * scale);
        var textBounds = control.ClientRectangle;
        if (icon is { } glyph)
        {
            const TextFormatFlags Measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int side = IconSide(control);
            int textWidth = TextRenderer.MeasureText(g, control.Text, control.Font, Size.Empty, Measure).Width;
            int left = (control.Width - side - textWidth) / 2;
            ToolbarGlyph.Draw(g, glyph, new(left, (control.Height - side) / 2f, side, side), text, fill);
            textBounds = new(left + side, 0, textWidth, control.Height);
            TextRenderer.DrawText(g, control.Text, control.Font, textBounds, text, Measure | TextFormatFlags.VerticalCenter);
        }
        else
            TextRenderer.DrawText(g, control.Text, control.Font, textBounds, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (focus)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(control.ClientRectangle, -(int)(4 * scale), -(int)(4 * scale)), text, fill);
    }
}
