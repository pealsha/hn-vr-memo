using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal static class MemoRenderer
{
    public const float PixelsPerMetre = BoardMetrics.ExportPixelsPerMetre;
    public static Size PixelSize(BoardSize size) => new(
        Math.Max(1, (int)MathF.Round(size.WidthMetres * PixelsPerMetre)),
        Math.Max(1, (int)MathF.Round(size.HeightMetres * PixelsPerMetre)));
    public static void WritePng(MemoDocument document, Stream destination)
    {
        using var image = Render(document);
        image.Save(destination, ImageFormat.Png);
    }

    public static Bitmap Render(MemoDocument document)
    {
        var pixels = PixelSize(document.BoardSize);
        var image = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(248, 247, 242));
            foreach (var stroke in document.Strokes)
            {
                float widthPixels = stroke.WidthMetres / document.BoardSize.WidthMetres * image.Width;
                var points = stroke.Points.Select(p => new PointF(
                    p.XMetres / document.BoardSize.WidthMetres * image.Width,
                    p.YMetres / document.BoardSize.HeightMetres * image.Height)).ToArray();
                DrawStroke(g, points, stroke.Color, widthPixels);
            }
        }
        return image;
    }

    internal static void DrawStroke(Graphics g, PointF[] points, string color, float widthPixels)
    {
        using var ink = new Pen(ColorTranslator.FromHtml(color), widthPixels)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (points.Length > 1)
            g.DrawLines(ink, points);
        else if (points.Length == 1)
        {
            using var dot = new SolidBrush(ink.Color);
            g.FillEllipse(dot, points[0].X - widthPixels / 2, points[0].Y - widthPixels / 2, widthPixels, widthPixels);
        }
    }
}
