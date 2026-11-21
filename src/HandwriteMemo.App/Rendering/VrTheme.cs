using System.Drawing.Drawing2D;

namespace HandwriteMemo.App;

// personal-ui-foundation 0.3.1. Paper, pigments and exported documents do not use these values.
internal static class VrTheme
{
    private static readonly TextureBrush s_grain = CreateGrain();
    internal const float Radius = UiDesign.VrRadius;
    internal const float PlateRadius = UiDesign.VrPlateRadius;
    internal static Color Plate => UiDesign.Plate;
    internal static Color Surface => UiDesign.Surface;
    internal static Color Text => UiDesign.Ink;
    internal static Color Secondary => UiDesign.Muted;
    internal static Color Border => UiDesign.Border;
    internal static Color Control => UiDesign.Control;
    internal static Color Accent => UiDesign.Accent;
    internal static Color AccentInk => UiDesign.AccentInk;
    internal static Color OnAccent => UiDesign.OnAccent;
    internal static Color Star => UiDesign.Star;
    internal static Color Notice => UiDesign.Notice;
    internal static Color NoticeInk => UiDesign.NoticeInk;
    internal static Color Error => UiDesign.Error;
    internal static Color ErrorInk => UiDesign.ErrorInk;

    internal static void PlateFill(Graphics g, RectangleF rect)
    {
        using var path = UiTheme.Rounded(rect, PlateRadius);
        using var brush = new SolidBrush(Plate);
        g.FillPath(brush, path);
    }

    internal static void Tile(Graphics g, RectangleF rect, bool selected, Color background, Color border, bool material = true)
    {
        if (selected)
            for (int i = UiDesign.HaloWidth; i > 0; i--)
            {
                using var halo = UiTheme.Rounded(RectangleF.Inflate(rect, i, i), Radius + i);
                using var glow = new SolidBrush(Color.FromArgb(UiDesign.HaloAlpha, Accent));
                g.FillPath(glow, halo);
            }
        if (material)
        {
            var shadowRect = rect;
            shadowRect.Offset(0, UiDesign.ShadowOffsetY);
            using var shadow = UiTheme.Rounded(shadowRect, Radius);
            using var brush = new SolidBrush(UiDesign.Shadow);
            g.FillPath(brush, shadow);
        }
        using var path = UiTheme.Rounded(rect, Radius);
        using var fill = new SolidBrush(selected ? Accent : background);
        g.FillPath(fill, path);
        if (material)
        {
            g.FillPath(s_grain, path);
            if (selected)
                DrawSelectedGlow(g, rect, path);
        }
        using var pen = new Pen(selected ? Accent : border, selected ? UiDesign.FocusWidth : UiDesign.BorderWidth);
        g.DrawPath(pen, path);
    }

    private static TextureBrush CreateGrain()
    {
        const int Size = 64;
        using var tile = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(tile);
        var random = new Random(7919);
        using var white = new SolidBrush(Color.FromArgb(UiDesign.GrainWhiteAlpha, Color.White));
        using var black = new SolidBrush(Color.FromArgb(UiDesign.GrainBlackAlpha, Color.Black));
        for (int i = 0; i < Size * Size / UiDesign.GrainEveryPixels; i++)
            g.FillRectangle(random.Next(2) == 0 ? white : black, random.Next(Size), random.Next(Size), 1, 1);
        // Keep the brush in canvas coordinates so repainting never shifts the pattern.
        return new TextureBrush(tile, WrapMode.Tile);
    }

    private static void DrawSelectedGlow(Graphics g, RectangleF rect, GraphicsPath path)
    {
        using var ellipse = new GraphicsPath();
        ellipse.AddEllipse(RectangleF.Inflate(rect, rect.Width / UiDesign.SelectedGlowGrowDivisor, rect.Height / UiDesign.SelectedGlowGrowDivisor));
        using var glow = new PathGradientBrush(ellipse)
        {
            CenterColor = Color.FromArgb(UiDesign.SelectedGlowAlpha, Color.White),
            SurroundColors = [Color.Transparent]
        };
        var saved = g.Save();
        try
        {
            g.SetClip(path, CombineMode.Intersect);
            g.FillPath(glow, ellipse);
        }
        finally
        {
            g.Restore(saved);
        }
    }
}
