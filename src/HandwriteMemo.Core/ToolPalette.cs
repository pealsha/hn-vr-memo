using System.Numerics;

namespace HandwriteMemo.Core;

public enum DrawingTool
{
    Pen,
    Eraser
}
public enum EraserMode
{
    Stroke,
    Partial
}

/// <summary>Shared VR dock layout, fixed above the paper's top-left corner.</summary>
public static class ToolPalette
{
    public const int ButtonCount = 9;
    public const float EraserRadiusMetres = .012f;
    public static IReadOnlyList<string> Colors { get; } = Array.AsReadOnly(new[] { MemoStroke.DefaultColor, "#C53939", "#2464C4", "#23834B" });
    public static IReadOnlyList<float> Widths { get; } = Array.AsReadOnly(new[] { .00075f, MemoStroke.DefaultWidthMetres, .003f });

    public const int DockWidthPixels = BoardMetrics.ReferenceWidthPixels, DockHeightPixels = 304, DockGapPixels = 8;
    private static readonly int[] s_leftPixels = [8, 88, 168, 248, 336, 416, 496, 592, 804];

    public static (float Left, float Top, float Width, float Height) Bounds(int index)
    {
        if (index < 0 || index >= ButtonCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        int width = index < 7 ? 72 : index == 7 ? 200 : 212;
        return (s_leftPixels[index] / (float)BoardMetrics.ReferenceWidthPixels, (120 - DockHeightPixels - DockGapPixels) / (float)BoardMetrics.ReferenceHeightPixels,
            width / (float)BoardMetrics.ReferenceWidthPixels, 64 / (float)BoardMetrics.ReferenceHeightPixels);
    }

    public static Vector2 Center(int index)
    {
        var r = Bounds(index);
        return new(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }

    public static int HitTest(Vector2 uv)
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            var r = Bounds(i);
            if (uv.X >= r.Left && uv.X <= r.Left + r.Width && uv.Y >= r.Top && uv.Y <= r.Top + r.Height)
                return i;
        }
        return -1;
    }

    public static int HitTest(Vector2 uv, BoardSize size) => HitTest(new(
        uv.X * size.WidthMetres / BoardSize.Default.WidthMetres,
        uv.Y * size.HeightMetres / BoardSize.Default.HeightMetres));
}
