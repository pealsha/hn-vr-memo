using System.Numerics;

namespace HandwriteMemo.Core;

/// <summary>Subtracts a swept circular eraser from page-space polylines, including sparse segments.</summary>
internal static class StrokeEraser
{
    // Returns null for an unchanged page. Surviving strokes retain their order and appearance.
    internal static List<Stroke>? Erase(IReadOnlyList<Stroke> strokes, BoardSize size, Vector2 fromUv, Vector2 toUv,
        float radius, EraserMode mode)
    {
        Vector2 Metres(Vector2 uv) => new(uv.X * size.WidthMetres, uv.Y * size.HeightMetres);
        var from = Metres(fromUv);
        var to = Metres(toUv);
        var result = new List<Stroke>();
        bool changed = false;
        foreach (var stroke in strokes)
        {
            double reach = radius + stroke.WidthMetres / 2;
            bool hit = false;
            var pieces = new List<Stroke>();
            Stroke? piece = null;
            void Append(InkPoint point)
            {
                if (piece is null)
                {
                    piece = new Stroke(stroke.Color, stroke.WidthMetres)
                    {
                        RecordedStartUtc = stroke.RecordedStartUtc,
                        RecordedStartSeconds = stroke.RecordedStartSeconds
                    };
                    pieces.Add(piece);
                }
                if (piece.Points.Count == 0 || piece.Points[^1] != point)
                    piece.Add(point);
            }
            if (stroke.Points.Count == 1)
                hit = Cut(Metres(stroke.Points[0].Uv), Metres(stroke.Points[0].Uv), from, to, reach) is not null;
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var a = stroke.Points[i - 1];
                var b = stroke.Points[i];
                var cut = Cut(Metres(a.Uv), Metres(b.Uv), from, to, reach);
                if (cut is not { } interval || mode == EraserMode.Partial && interval.End - interval.Start <= 1e-8)
                {
                    Append(a);
                    Append(b);
                    continue;
                }
                hit = true;
                if (mode == EraserMode.Stroke)
                    break;
                InkPoint Lerp(double t) => new(Vector2.Lerp(a.Uv, b.Uv, (float)t), a.Seconds + (b.Seconds - a.Seconds) * t);
                if (interval.Start > 1e-8)
                {
                    Append(a);
                    Append(Lerp(interval.Start));
                }
                piece = null;
                if (interval.End < 1 - 1e-8)
                {
                    Append(Lerp(interval.End));
                    Append(b);
                }
            }
            if (!hit)
                result.Add(stroke);
            else
            {
                changed = true;
                if (mode == EraserMode.Partial)
                    result.AddRange(pieces);
            }
        }
        return changed ? result : null;
    }

    // A capsule is convex: the union of its endpoint disks and middle rectangle
    // intersects a line in at most one interval. Double precision protects small strokes.
    private static (double Start, double End)? Cut(Vector2 a, Vector2 b, Vector2 from, Vector2 to, double radius)
    {
        double start = double.PositiveInfinity, end = double.NegativeInfinity;
        double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y;
        void Include(double low, double high)
        {
            low = Math.Max(0, low);
            high = Math.Min(1, high);
            if (low <= high)
            {
                start = Math.Min(start, low);
                end = Math.Max(end, high);
            }
        }
        void Circle(Vector2 center)
        {
            double x = (double)a.X - center.X, y = (double)a.Y - center.Y;
            double aa = dx * dx + dy * dy;
            double cc = x * x + y * y - radius * radius;
            if (aa < 1e-24)
            {
                if (cc <= 0)
                    Include(0, 1);
                return;
            }
            double bb = x * dx + y * dy;
            double disc = bb * bb - aa * cc;
            if (disc < 0)
                return;
            double root = Math.Sqrt(disc);
            Include((-bb - root) / aa, (-bb + root) / aa);
        }
        Circle(from);
        Circle(to);
        double ex = (double)to.X - from.X, ey = (double)to.Y - from.Y;
        double length = Math.Sqrt(ex * ex + ey * ey);
        if (length > 1e-12)
        {
            ex /= length;
            ey /= length;
            double x = (double)a.X - from.X, y = (double)a.Y - from.Y;
            double low = 0, high = 1;
            bool Slab(double position, double delta, double min, double max)
            {
                if (Math.Abs(delta) < 1e-15)
                    return position >= min && position <= max;
                double first = (min - position) / delta, last = (max - position) / delta;
                low = Math.Max(low, Math.Min(first, last));
                high = Math.Min(high, Math.Max(first, last));
                return low <= high;
            }
            if (Slab(x * ex + y * ey, dx * ex + dy * ey, 0, length) &&
                Slab(-x * ey + y * ex, -dx * ey + dy * ex, -radius, radius))
                Include(low, high);
        }
        return start <= end ? (start, end) : null;
    }
}
