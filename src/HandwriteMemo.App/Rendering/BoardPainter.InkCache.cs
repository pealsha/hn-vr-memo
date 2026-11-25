using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class BoardPainter
{
    private Bitmap? _completedInk;
    private Bitmap? _liveInk;
    private long _completedInkRevision = -1;
    private long _liveInkRevision = -1;
    private Stroke? _cachedLiveStroke;
    private int _cachedLivePointCount;
    private int _cachedCompletedStrokeCount;

    // The paper and completed strokes change only on edits that replace existing ink.
    // A pen stroke is kept separately so each input sample paints just its newest segment.
    private void DrawCachedInk(Graphics destination, HandwriteEngine engine)
    {
        EnsureInkLayers();
        if (_completedInkRevision != engine.InkRevision)
            RebuildCompletedInk(engine);
        else
        {
            CommitCompletedStrokes(engine);
            UpdateLiveInk(engine);
        }

        destination.DrawImageUnscaled(_completedInk!, 0, 0);
        if (_cachedLiveStroke is not null)
            destination.DrawImageUnscaled(_liveInk!, 0, 0);
    }

    // Synthetic benchmark hook: measures just the board raster path, without toolbar text or overlays.
    internal void DrawInkForMeasurement(HandwriteEngine engine)
    {
        var pixels = MemoRenderer.PixelSize(engine.BoardSize);
        if (Image.Size != pixels)
        {
            Image.Dispose();
            Image = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        }
        using var g = Graphics.FromImage(Image);
        DrawCachedInk(g, engine);
    }

    private void EnsureInkLayers()
    {
        if (_completedInk?.Size == Image.Size && _liveInk?.Size == Image.Size)
            return;
        DisposeInkCache();
        _completedInk = new Bitmap(Image.Width, Image.Height, PixelFormat.Format32bppArgb);
        _liveInk = new Bitmap(Image.Width, Image.Height, PixelFormat.Format32bppArgb);
        _completedInkRevision = -1;
        _liveInkRevision = -1;
        _cachedLiveStroke = null;
        _cachedLivePointCount = 0;
        _cachedCompletedStrokeCount = 0;
    }

    private void RebuildCompletedInk(HandwriteEngine engine)
    {
        using (var g = Graphics.FromImage(_completedInk!))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPaperGrid(g, Image.Size);
            foreach (var stroke in engine.Strokes)
            {
                if (ReferenceEquals(stroke, engine.ActiveStroke))
                    continue;
                DrawStroke(g, stroke, 0, engine.BoardSize.WidthMetres);
            }
        }
        ClearLiveInk();
        _cachedLiveStroke = null;
        _cachedLivePointCount = 0;
        _cachedCompletedStrokeCount = engine.Strokes.Count - (engine.ActiveStroke is null ? 0 : 1);
        _completedInkRevision = engine.InkRevision;
        _liveInkRevision = -1;
        UpdateLiveInk(engine);
    }

    internal static void DrawPaperGrid(Graphics g, Size size, bool anchorRight = false)
    {
        const int Spacing = 32;
        g.Clear(UiTheme.Surface);
        using var grid = new Pen(UiTheme.Grid);
        for (int offset = Spacing; offset < size.Width; offset += Spacing)
        {
            int x = anchorRight ? size.Width - offset : offset;
            g.DrawLine(grid, x, 0f, x, size.Height);
        }
        for (int y = Spacing; y < size.Height; y += Spacing)
            g.DrawLine(grid, 0, y, size.Width, y);
    }

    private void UpdateLiveInk(HandwriteEngine engine)
    {
        var active = engine.ActiveStroke;
        if (active is null)
        {
            _liveInkRevision = engine.ActiveStrokeRevision;
            return;
        }

        if (!ReferenceEquals(active, _cachedLiveStroke))
        {
            ClearLiveInk();
            _cachedLiveStroke = active;
            _cachedLivePointCount = 0;
        }
        if (_liveInkRevision == engine.ActiveStrokeRevision && _cachedLivePointCount == active.Points.Count)
            return;

        using (var g = Graphics.FromImage(_liveInk!))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawStroke(g, active, _cachedLivePointCount, engine.BoardSize.WidthMetres);
        }
        _cachedLivePointCount = active.Points.Count;
        _liveInkRevision = engine.ActiveStrokeRevision;
    }

    private void CommitCompletedStrokes(HandwriteEngine engine)
    {
        int completedCount = engine.Strokes.Count - (engine.ActiveStroke is null ? 0 : 1);
        if (completedCount < _cachedCompletedStrokeCount)
        {
            // Structural mutations normally increment InkRevision. This defensive path keeps a
            // stale cache from surviving an unexpected host mutation.
            RebuildCompletedInk(engine);
            return;
        }
        if (completedCount == _cachedCompletedStrokeCount)
            return;
        using var g = Graphics.FromImage(_completedInk!);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_cachedLiveStroke is not null &&
            _cachedCompletedStrokeCount < engine.Strokes.Count &&
            ReferenceEquals(engine.Strokes[_cachedCompletedStrokeCount], _cachedLiveStroke))
        {
            // Desktop events can append points and release entirely between paint calls.
            // In that case the live bitmap is incomplete: raster this one finished stroke in full.
            var finished = engine.Strokes[_cachedCompletedStrokeCount];
            if (_cachedLivePointCount == finished.Points.Count)
                g.DrawImageUnscaled(_liveInk!, 0, 0);
            else
                DrawStroke(g, finished, 0, engine.BoardSize.WidthMetres);
            _cachedCompletedStrokeCount++;
            ClearLiveInk();
            _cachedLiveStroke = null;
            _cachedLivePointCount = 0;
        }
        for (int i = _cachedCompletedStrokeCount; i < completedCount; i++)
            DrawStroke(g, engine.Strokes[i], 0, engine.BoardSize.WidthMetres);
        _cachedCompletedStrokeCount = completedCount;
    }

    private void DrawStroke(Graphics g, Stroke stroke, int fromPoint, float boardWidthMetres)
    {
        int start = Math.Max(0, fromPoint - 1);
        var points = stroke.Points.Skip(start).Select(p => new PointF(p.Uv.X * Image.Width, p.Uv.Y * Image.Height)).ToArray();
        MemoRenderer.DrawStroke(g, points, stroke.Color, stroke.WidthMetres / boardWidthMetres * Image.Width);
    }

    private void ClearLiveInk()
    {
        using var g = Graphics.FromImage(_liveInk!);
        g.Clear(Color.Transparent);
    }

    private void DisposeInkCache()
    {
        _completedInk?.Dispose();
        _completedInk = null;
        _liveInk?.Dispose();
        _liveInk = null;
    }
}
