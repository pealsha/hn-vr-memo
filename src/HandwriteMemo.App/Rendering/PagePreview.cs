using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// List-only ink rendering. The saved PNG and the editable page retain their full-page geometry.
internal static class PagePreview
{
    private sealed record Polyline(MemoStroke Stroke, List<PointF> Points);
    private sealed class Ink
    {
        public List<Polyline> Polylines { get; } = [];
        public RectangleF? Bounds { get; set; }
    }

    public static Bitmap Create(MemoDocument document, int width, int height)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(UiTheme.Surface);
        float w = document.BoardSize.WidthMetres, h = document.BoardSize.HeightMetres;
        // One crop and one scale for all ink, including legacy ink outside today's writing area.
        var board = new RectangleF(0, 0, w, h);
        var ink = Collect(document, board);
        var area = new RectangleF(4, 4, width - 8, height - 8);
        if (ink.Bounds is not { } bounds)
        {
            using var font = new Font(UiTheme.FontFamily, Math.Min(24, height * .22f), FontStyle.Regular, GraphicsUnit.Pixel);
            using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("空のページ", font, Brushes.SlateGray, area, centered);
        }
        else
            Draw(g, ink.Polylines, bounds, board, area);
        return image;
    }

    private static Ink Collect(MemoDocument document, RectangleF board)
    {
        var ink = new Ink();
        foreach (var stroke in document.Strokes)
        {
            float radius = stroke.WidthMetres / 2;
            var expandedBoard = RectangleF.Inflate(board, radius, radius);
            List<PointF>? points = null;
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                var p = stroke.Points[i];
                var q = stroke.Points[Math.Max(0, i - 1)];
                if (ClipInk(new(q.XMetres, q.YMetres), new(p.XMetres, p.YMetres), radius, board, expandedBoard, out var a, out var b) is not { } bounds)
                {
                    points = null;
                    continue;
                }
                ink.Bounds = ink.Bounds is { } previous ? RectangleF.Union(previous, bounds) : bounds;
                // Separate runs when a stroke leaves the board and re-enters at a different point.
                if (points is null || points[^1] != a)
                {
                    points = [a];
                    ink.Polylines.Add(new(stroke, points));
                }
                if (a != b)
                    points.Add(b);
            }
        }
        return ink;
    }

    private static RectangleF? ClipInk(PointF a, PointF b, float radius, RectangleF region, RectangleF expandedRegion, out PointF start, out PointF end)
    {
        if (!Clip(a, b, expandedRegion, out start, out end))
            return null;
        var bounds = RectangleF.Intersect(region, RectangleF.FromLTRB(
            Math.Min(start.X, end.X) - radius, Math.Min(start.Y, end.Y) - radius,
            Math.Max(start.X, end.X) + radius, Math.Max(start.Y, end.Y) + radius));
        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }

    private static void Draw(Graphics g, List<Polyline> polylines, RectangleF bounds, RectangleF board, RectangleF destination)
    {
        // 10 mm minimum framing avoids turning one tiny dot into a card-sized disc.
        float scale = Math.Min(destination.Width / (Math.Max(.01f, bounds.Width) * 1.12f),
            destination.Height / (Math.Max(.01f, bounds.Height) * 1.12f));
        float x = destination.Left + destination.Width / 2 - (bounds.Left + bounds.Width / 2) * scale;
        float y = destination.Top + destination.Height / 2 - (bounds.Top + bounds.Height / 2) * scale;
        PointF Map(PointF point) => new(point.X * scale + x, point.Y * scale + y);
        var mappedBoard = new RectangleF(board.X * scale + x, board.Y * scale + y, board.Width * scale, board.Height * scale);
        g.SetClip(RectangleF.Intersect(destination, mappedBoard), CombineMode.Intersect);
        // Draw from vector ink at final size; a raster downscale would lose fine lines.
        foreach (var (stroke, points) in polylines)
        {
            var mapped = new PointF[points.Count];
            for (int i = 0; i < mapped.Length; i++)
                mapped[i] = Map(points[i]);
            MemoRenderer.DrawStroke(g, mapped, stroke.Color, Math.Max(1.6f, stroke.WidthMetres * scale));
        }
    }

    // Slab clipping retains crossing lines even when both recorded points are outside a region.
    private static bool Clip(PointF a, PointF b, RectangleF region, out PointF start, out PointF end)
    {
        float first = 0, last = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Axis(float origin, float delta, float low, float high)
        {
            if (delta == 0)
                return origin >= low && origin <= high;
            float t0 = (low - origin) / delta, t1 = (high - origin) / delta;
            first = Math.Max(first, Math.Min(t0, t1));
            last = Math.Min(last, Math.Max(t0, t1));
            return first <= last;
        }
        bool visible = Axis(a.X, dx, region.Left, region.Right) && Axis(a.Y, dy, region.Top, region.Bottom);
        start = first == 0 ? a : new(a.X + first * dx, a.Y + first * dy);
        end = last == 1 ? b : new(a.X + last * dx, a.Y + last * dy);
        return visible;
    }
}
