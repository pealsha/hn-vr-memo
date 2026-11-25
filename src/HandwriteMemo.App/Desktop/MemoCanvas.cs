using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class MemoCanvas : Control
{
    private readonly HandwriteEngine _engine;
    private readonly MemoSession _page;
    private readonly Func<double> _seconds;
    private readonly Func<DrawingTool> _tool;
    private readonly Func<EraserMode> _eraseMode;
    private bool _pressed;
    private uint? _pointer;
    private bool _nativePenSeen;
    private Point? _hoverPoint;
    private CanvasMode _mode = new LivePage(EditingEnabled: true);

    private abstract record CanvasMode;
    private sealed record LivePage(bool EditingEnabled) : CanvasMode;
    private sealed record TrashPreview(MemoDocument? Document) : CanvasMode;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ReadOnly => _mode is not LivePage { EditingEnabled: true };
    private BoardSize PageSize => _mode is TrashPreview { Document: { } document } ? document.BoardSize : _engine.BoardSize;

    internal void ShowLivePage(bool editingEnabled) => SetMode(new LivePage(editingEnabled));
    internal void ShowTrashPreview(MemoDocument? document) => SetMode(new TrashPreview(document));

    private void SetMode(CanvasMode mode)
    {
        if (_mode == mode)
            return;
        _mode = mode;
        if (ReadOnly)
            EndInput();
        Invalidate();
    }

    public MemoCanvas(HandwriteEngine engine, MemoSession page, Func<double> seconds, Func<DrawingTool> tool, Func<EraserMode> eraseMode)
    {
        this._engine = engine;
        this._page = page;
        this._seconds = seconds;
        this._tool = tool;
        this._eraseMode = eraseMode;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = UiTheme.Background;
        Cursor = Cursors.Cross;
    }

    internal RectangleF PaperBounds => FitPaperBounds(PageSize);

    private RectangleF FitPaperBounds(BoardSize size)
    {
        int margin = LogicalToDeviceUnits(40);
        float width = Math.Max(1, ClientSize.Width - margin), height = Math.Max(1, ClientSize.Height - margin);
        float ratio = size.WidthMetres / size.HeightMetres;
        if (width / height > ratio)
            width = height * ratio;
        else
            height = width / ratio;
        return new((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    internal float InkWidthPixels(float widthMetres, BoardSize? referenceSize = null)
    {
        var size = referenceSize ?? PageSize;
        return widthMetres / size.WidthMetres * FitPaperBounds(size).Width;
    }

    internal Vector2? PagePoint(PointF point)
    {
        var r = PaperBounds;
        return r.Contains(point) ? new((point.X - r.Left) / r.Width, (point.Y - r.Top) / r.Height) : null;
    }

    internal void BeginInput(Point point)
    {
        if (ReadOnly || PagePoint(point) is null || !_page.BeginDesktop(_seconds()))
            return;
        _pressed = true;
        Capture = true;
        Focus();
        _engine.SetEraserMode(_eraseMode());
        MoveInput(point);
    }

    private bool ShowsHoverCursor => !ReadOnly && _tool() == DrawingTool.Eraser && _eraseMode() == EraserMode.Partial;

    internal void MoveInput(Point point)
    {
        bool hadHover = _hoverPoint is not null;
        _hoverPoint = point;
        if (_pressed)
        {
            Invalidate();
            _page.DesktopPoint(PagePoint(point), true, _seconds(), _tool());
            return;
        }
        if (ShowsHoverCursor && (hadHover || PagePoint(point) is not null))
            Invalidate();
    }

    internal void EndInput()
    {
        if (!_pressed)
            return;
        _pressed = false;
        _page.DesktopPoint(null, false, _seconds(), _tool());
        Capture = false;
        Invalidate();
    }

    internal static bool AcceptMouse(bool nativePenSeen, bool activePen, long extraInfo) =>
        !activePen && (!nativePenSeen || (extraInfo & 0xFFFFFF00L) != 0xFF515700L);
    private bool AcceptCurrentMouse() => AcceptMouse(_nativePenSeen, _pointer.HasValue, (long)GetMessageExtraInfo());
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (AcceptCurrentMouse() && e.Button == MouseButtons.Left)
            BeginInput(e.Location);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (AcceptCurrentMouse())
            MoveInput(e.Location);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (AcceptCurrentMouse() && e.Button == MouseButtons.Left)
            EndInput();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHover();
    }

    private void ClearHover()
    {
        bool hadHover = _hoverPoint is not null;
        _hoverPoint = null;
        if (hadHover && ShowsHoverCursor)
            Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
            EndInput();
    }

    protected override void WndProc(ref Message m)
    {
        const int Down = 0x246, Update = 0x245, Up = 0x247, CaptureChanged = 0x24C, Enter = 0x249, Leave = 0x24A;
        if (m.Msg is Down or Update or Up or CaptureChanged or Enter or Leave)
        {
            uint id = (uint)((long)m.WParam & 0xFFFF);
            bool pen = _pointer == id || GetPointerType(id, out uint type) && type == 3;
            if (pen)
            {
                // Consume the complete pen sequence: DefWindowProc must not promote it to mouse ink too.
                _nativePenSeen = true;
                var point = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
                bool canceled = ((long)m.WParam & 0x80000000L) != 0;
                if (m.Msg == Down && _pointer is null)
                {
                    _pointer = id;
                    BeginInput(point);
                }
                else if (m.Msg is Update or Enter && !canceled && (_pointer is null || _pointer == id))
                    MoveInput(point);
                if (_pointer == id && (m.Msg is Up or CaptureChanged or Leave || canceled))
                {
                    EndInput();
                    _pointer = null;
                }
                if (m.Msg is Leave or CaptureChanged || canceled)
                    ClearHover();
                if (m.Msg == Leave)
                    _nativePenSeen = false;
                m.Result = IntPtr.Zero;
                return;
            }
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerType(uint id, out uint type);
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var r = PaperBounds;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        float cornerRadius = 6 * scale;
        var shadow = Color.FromArgb(225, 225, 225);
        UiTheme.Tile(g, new RectangleF(r.X + 2 * scale, r.Y + 3 * scale, r.Width, r.Height), shadow, shadow, scale, cornerRadius);
        using var paperPath = UiTheme.Rounded(r, cornerRadius);
        using var paper = new SolidBrush(UiTheme.Surface);
        g.FillPath(paper, paperPath);
        g.SetClip(paperPath);
        float ScaleY(float uv) => r.Top + uv * r.Height;
        using var grid = new Pen(UiTheme.Grid);
        float cell = r.Width / PageSize.WidthMetres * .015f;
        for (float x = r.Left + cell; x < r.Right; x += cell)
            g.DrawLine(grid, x, r.Top, x, r.Bottom);
        for (float y = r.Top + cell; y < r.Bottom; y += cell)
            g.DrawLine(grid, r.Left, y, r.Right, y);
        if (_mode is TrashPreview { Document: { } doc })
        {
            foreach (var stroke in doc.Strokes)
                MemoRenderer.DrawStroke(g, stroke.Points.Select(p => new PointF(r.Left + p.XMetres / doc.BoardSize.WidthMetres * r.Width,
                    ScaleY(p.YMetres / doc.BoardSize.HeightMetres))).ToArray(), stroke.Color, stroke.WidthMetres / doc.BoardSize.WidthMetres * r.Width);
        }
        else if (_mode is LivePage)
            foreach (var stroke in _engine.Strokes)
                MemoRenderer.DrawStroke(g, stroke.Points.Select(p => new PointF(r.Left + p.Uv.X * r.Width, ScaleY(p.Uv.Y))).ToArray(),
                    stroke.Color, stroke.WidthMetres / _engine.BoardSize.WidthMetres * r.Width);
        if (!ReadOnly && _tool() == DrawingTool.Eraser && _eraseMode() == EraserMode.Partial && _hoverPoint is { } point && PagePoint(point) is not null)
        {
            float radius = ToolPalette.EraserRadiusMetres / PageSize.WidthMetres * r.Width;
            // A white halo keeps the true-sized outline visible over both dark ink and the grid.
            using var halo = new Pen(Color.White, 4);
            using var range = new Pen(_pressed && _page.DesktopOwnsInput ? UiTheme.Text : UiTheme.AccentInk, 2);
            g.DrawEllipse(halo, point.X - radius, point.Y - radius, radius * 2, radius * 2);
            g.DrawEllipse(range, point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }
        g.ResetClip();
        using var border = new Pen(UiTheme.Border);
        g.DrawPath(border, paperPath);
    }
}
