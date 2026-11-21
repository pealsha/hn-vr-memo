using System.Drawing.Drawing2D;

namespace HandwriteMemo.App;

// PC and VR share the drawing-tool paths. Navigation and settings glyphs only appear on PC.
internal static class ToolbarGlyph
{
    public static void Draw(Graphics g, ToolbarIcon icon, RectangleF rect, Color ink, Color background)
    {
        var state = g.Save();
        g.TranslateTransform(rect.Left, rect.Top);
        g.ScaleTransform(rect.Width / 24, rect.Height / 24);
        if (icon is ToolbarIcon.PreviousPage or ToolbarIcon.NextPage)
        {
            using var chevron = new Pen(ink, 2.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            int tip = icon == ToolbarIcon.PreviousPage ? 8 : 16;
            int tail = 24 - tip;
            g.DrawLines(chevron, [new Point(tail, 5), new Point(tip, 12), new Point(tail, 19)]);
        }
        else if (icon == ToolbarIcon.NewPage)
        {
            // Same stroke and extent as the page arrows beside it.
            using var plus = new Pen(ink, 2.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(plus, 12, 5, 12, 19);
            g.DrawLine(plus, 5, 12, 19, 12);
        }
        else if (icon == ToolbarIcon.Refresh)
        {
            DrawRefresh(g, ink);
        }
        else if (icon == ToolbarIcon.Settings)
        {
            DrawSettings(g, ink);
        }
        else
        {
            using var path = FluentToolbarIcons.Create(icon);
            using var brush = new SolidBrush(ink);
            g.FillPath(brush, path);
            if (icon == ToolbarIcon.EraseStroke)
            {
                using var fill = new SolidBrush(background);
                g.FillEllipse(fill, 13, 13, 11, 11);
                using var cross = new Pen(ink, 1.75f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(cross, 16, 16, 21, 21);
                g.DrawLine(cross, 21, 16, 16, 21);
            }
        }
        g.Restore(state);
    }

    private static void DrawSettings(Graphics g, Color ink)
    {
        const float Center = 12f;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        // One quadrant of the outline relative to the center, repeated at right angles.
        ReadOnlySpan<PointF> quadrant =
        [
            new(-1.25f, -8), new(1.25f, -8), new(1.75f, -5.75f), new(2.75f, -5.25f),
            new(4.75f, -6.5f), new(6.5f, -4.75f), new(5.25f, -2.75f), new(5.75f, -1.75f)
        ];
        var outline = new PointF[quadrant.Length * 4];
        for (int i = 0; i < quadrant.Length; i++)
        {
            var (x, y) = (quadrant[i].X, quadrant[i].Y);
            outline[i] = new(Center + x, Center + y);
            outline[i + quadrant.Length] = new(Center - y, Center + x);
            outline[i + quadrant.Length * 2] = new(Center - x, Center - y);
            outline[i + quadrant.Length * 3] = new(Center + y, Center - x);
        }

        using var pen = new Pen(ink, 1.75f) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, outline);
        const float HoleRadius = 3f;
        g.DrawEllipse(pen, Center - HoleRadius, Center - HoleRadius, HoleRadius * 2, HoleRadius * 2);
    }

    private static void DrawRefresh(Graphics g, Color ink)
    {
        // Public Icons Reload uses a 120 x 120 artboard. The curve and arrowhead are separate fills.
        g.TranslateTransform(3, 3);
        g.ScaleTransform(18f / 120, 18f / 120);
        using var ring = new GraphicsPath();
        ring.AddBeziers([
            new PointF(60, 95.5f), new PointF(40.425f, 95.5f), new PointF(24.5f, 79.574f), new PointF(24.5f, 60),
            new PointF(24.5f, 40.425f), new PointF(40.425f, 24.5f), new PointF(60, 24.5f),
            new PointF(73.62f, 24.5f), new PointF(85.467f, 32.214f), new PointF(91.418f, 43.5f)
        ]);
        ring.AddLine(new PointF(91.418f, 43.5f), new PointF(114.045f, 43.5f));
        ring.AddBeziers([
            new PointF(114.045f, 43.5f), new PointF(106.984f, 20.347f), new PointF(85.462f, 3.5f), new PointF(60, 3.5f),
            new PointF(28.796f, 3.5f), new PointF(3.5f, 28.796f), new PointF(3.5f, 60),
            new PointF(3.5f, 91.203f), new PointF(28.796f, 116.5f), new PointF(60, 116.5f),
            new PointF(76.264f, 116.5f), new PointF(90.911f, 109.618f), new PointF(101.221f, 98.62f)
        ]);
        ring.AddLine(new PointF(101.221f, 98.62f), new PointF(85.889f, 84.255f));
        ring.AddBezier(85.889f, 84.255f, 79.406f, 91.168f, 70.201f, 95.5f, 60, 95.5f);
        ring.CloseFigure();
        using var brush = new SolidBrush(ink);
        g.FillPath(brush, ring);
        g.FillPolygon(brush, [new PointF(120, 21.832f), new PointF(119.992f, 68.842f), new PointF(74.827f, 55.811f)]);
    }
}
