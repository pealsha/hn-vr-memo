using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HandwriteMemo.App;

// Keeps each group intact when wrapping; the final (page) group stays right aligned.
internal sealed class DesktopToolbar : Panel
{
    private bool _large, _layingOut;
    private int _buttonDpi = 96;
    internal bool LargeButtons => _large;
    internal int TileSide => ButtonPixels(_large ? 48 : 36);
    internal int ActionHeight => ButtonPixels(_large ? 48 : 32);

    internal void UpdateButtonMetrics(int editorWidth, int dpi)
    {
        _buttonDpi = dpi;
        // Leave a gap between entering and leaving large mode during a resize.
        if (editorWidth >= ButtonPixels(1080))
            _large = true;
        else if (editorWidth <= ButtonPixels(1000))
            _large = false;
    }

    private int ButtonPixels(int logical) => (int)Math.Round(logical * (_buttonDpi / 96f));

    internal int SingleRowWidth => Padding.Horizontal + Controls.Cast<Control>().Sum(group => group.GetPreferredSize(Size.Empty).Width);

    public DesktopToolbar()
    {
        BackColor = UiTheme.Background;
        Padding = new(8, 5, 8, 7);
        DoubleBuffered = true;
    }

    internal FlowLayoutPanel AddGroup(string name, params Control[] buttons)
    {
        var group = new FlowLayoutPanel
        {
            AccessibleName = name,
            AccessibleRole = AccessibleRole.Grouping,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Controls.Count == 0 ? Padding.Empty : new(9, 0, 0, 0)
        };
        if (Controls.Count > 0)
            group.Paint += (_, e) =>
            {
                using var line = new Pen(UiTheme.Border);
                int x = group.Padding.Left / 2;
                e.Graphics.DrawLine(line, x, 6, x, group.Height - 6);
            };
        group.Controls.AddRange(buttons);
        Controls.Add(group);
        return group;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_layingOut)
            return;
        _layingOut = true;
        try
        {
            base.OnLayout(e);
            int side = TileSide;
            foreach (Control group in Controls)
            {
                group.SuspendLayout();
                foreach (Control button in group.Controls)
                {
                    if (button is ToolbarTile)
                    {
                        button.Size = new(side, side);
                    }
                    else if (button is Button pageButton)
                    {
                        SizeTextButton(pageButton);
                        pageButton.MinimumSize = new(0, side);
                    }
                }
                group.ResumeLayout(true);
            }
            int x = Padding.Left, y = Padding.Top, rowHeight = 0;
            int right = Math.Max(Padding.Left + 1, ClientSize.Width - Padding.Right);
            for (int i = 0; i < Controls.Count; i++)
            {
                var group = Controls[i];
                var size = group.GetPreferredSize(Size.Empty);
                if (x > Padding.Left && x + size.Width > right)
                {
                    x = Padding.Left;
                    y += rowHeight;
                    rowHeight = 0;
                }
                if (i == Controls.Count - 1)
                    x = Math.Max(x, right - size.Width);
                group.Bounds = new(x, y, size.Width, size.Height);
                x += size.Width;
                rowHeight = Math.Max(rowHeight, size.Height);
            }
            int height = y + rowHeight + Padding.Bottom;
            if (Height != height)
                Height = height;
        }
        finally
        {
            _layingOut = false;
        }
    }

    // Page, catalog and recovery actions share this toolbar's size mode and DPI.
    internal void SizeTextButton(ButtonBase button)
    {
        button.Font = _large ? UiTheme.DesktopLargeFont : UiTheme.DesktopFont;
        if (button is Button action)
            action.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new(0, ActionHeight);
        int horizontal = ButtonPixels(_large ? 12 : 8);
        int vertical = ButtonPixels(4);
        button.Padding = new(horizontal, vertical, horizontal, vertical);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
    }
}

internal enum ToolbarIcon
{
    Pen,
    EraseStroke,
    ErasePart,
    Undo,
    Redo,
    PreviousPage,
    NextPage,
    NewPage,
    Refresh,
    Settings
}

// A standard Button retains keyboard activation, focus, tooltips and enabled-state behavior.
// Only its surface is painted here, so Windows cannot add a square frame over the tile.
internal sealed class ToolbarTile : Button
{
    private readonly ToolbarIcon? _icon;
    private readonly Color _sampleColor;
    private readonly Func<float>? _diameter;
    private bool _selected, _hovered, _pressed;

    internal ToolbarTile(string name, ToolbarIcon icon) : this(name)
    {
        this._icon = icon;
    }

    internal ToolbarTile(string name, Color color, Func<float> diameter) : this(name)
    {
        _sampleColor = color;
        this._diameter = diameter;
    }

    private ToolbarTile(string name)
    {
        AccessibleName = name;
        Size = new(36, 36);
        Margin = new(2);
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            UpdateDescription();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
        }
    }

    private void UpdateDescription() => AccessibleDescription = _selected ? "選択中。" : "";

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        bool highContrast = SystemInformation.HighContrast;
        bool history = _icon is ToolbarIcon.Undo or ToolbarIcon.Redo;
        Color accent = highContrast ? SystemColors.Highlight : UiTheme.Accent;
        Color background = !Enabled ? SystemColors.Control : _selected
            ? (highContrast ? SystemColors.Highlight : UiTheme.Selection)
            : (highContrast ? SystemColors.Control : UiTheme.Surface);
        if (Enabled && !_selected && (_hovered || _pressed))
            background = highContrast ? SystemColors.Highlight
            : _pressed ? UiTheme.Pressed : UiTheme.Hover;
        if (Enabled && _selected && _diameter is not null && !highContrast)
            background = UiTheme.Surface;
        Color border = !Enabled ? SystemColors.GrayText : _selected ? accent
            : _hovered || Focused && ShowFocusCues ? accent : UiTheme.Border;
        using var tile = new GraphicsPath();
        float inset = 1.5f * scale, corner = UiDesign.DesktopRadius * 2 * scale;
        var r = new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset);
        tile.AddArc(r.Left, r.Top, corner, corner, 180, 90);
        tile.AddArc(r.Right - corner, r.Top, corner, corner, 270, 90);
        tile.AddArc(r.Right - corner, r.Bottom - corner, corner, corner, 0, 90);
        tile.AddArc(r.Left, r.Bottom - corner, corner, corner, 90, 90);
        tile.CloseFigure();
        using var fill = new SolidBrush(background);
        using var outline = new Pen(border, (_selected || _hovered || Focused && ShowFocusCues ? 2 : 1) * scale);
        // History keeps the full button target, but has no idle tile or border.
        if (!history || Enabled && (_hovered || _pressed))
            g.FillPath(fill, tile);
        if (!history)
            g.DrawPath(outline, tile);

        Color ink = !Enabled ? SystemColors.GrayText : highContrast && (_selected || _hovered)
            ? SystemColors.HighlightText : _selected && _diameter is null ? UiTheme.OnAccent : ForeColor;
        if (_diameter is not null)
        {
            float size = Math.Clamp(_diameter(), 0, Math.Min(Width, Height) - 10 * scale);
            using var brush = new SolidBrush(!Enabled ? SystemColors.GrayText : _sampleColor);
            g.FillEllipse(brush, (Width - size) / 2, (Height - size) / 2, size, size);
        }
        else
        {
            float side = Math.Min(Width, Height) * (2f / 3);
            ToolbarGlyph.Draw(g, _icon!.Value,
                new((Width - side) / 2, (Height - side) / 2, side, side), ink, background);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(5 * scale), -(int)(5 * scale)), ink, background);
    }
}
