using System.Drawing.Drawing2D;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class FoundationLookTests
{
    [TestMethod]
    public void StableMaterial()
    {
        var rect = new RectangleF(20, 20, 180, 80);
        foreach (bool selected in new[] { false, true })
        {
            using var first = Tile(rect, selected);
            using var second = Tile(rect, selected);
            Check(FoundationPixels.Differences(first, second) == 0, "Repeated tile pixels must be identical.");
            // Draw at other bounds and verify they do not alter the original grain.
            for (int i = 0; i < 130; i++)
                using (Tile(new(10 + i / 10f, 10, 100, 50), selected)) { }
            using var regenerated = Tile(rect, selected);
            Check(FoundationPixels.Differences(first, regenerated) == 0, "Drawing elsewhere must leave the original grain in place.");
            int textured = 0;
            Color baseColor = selected ? UiDesign.Accent : UiDesign.Surface;
            for (int y = 40; y < 80; y++)
                for (int x = 40; x < 180; x++)
                    if (first.GetPixel(x, y).ToArgb() != baseColor.ToArgb()) textured++;
            Check(textured > 100, "Material must be present, rather than a flat deterministic fill.");
        }
    }

    [TestMethod]
    public void HaloDoesNotReachNeighbours()
    {
        CheckFamily(Enumerable.Range(0, ToolPalette.ButtonCount).Select(i => Pixels(ToolPalette.Bounds(i))));
        CheckFamily(Enumerable.Range(0, BoardControls.EditorButtons.Count).Select(i => Pixels(BoardControls.ButtonBounds(i, false))));
        CheckFamily(Enumerable.Range(0, BoardControls.ListButtons.Count).Select(i => Pixels(BoardControls.ButtonBounds(i, true))));
        CheckFamily(Enumerable.Range(0, BoardControls.PagesPerScreen).Select(i => Pixels(BoardControls.CardBounds(i))));
        CheckFamily(Enumerable.Range(0, DashboardControls.Count).Select(i => (RectangleF)DashboardControls.Bounds(i)));

        static RectangleF Pixels((float Left, float Top, float Width, float Height) r) => new(
            r.Left * BoardPainter.Width, r.Top * BoardPainter.Height + ToolPalette.DockHeightPixels + ToolPalette.DockGapPixels,
            r.Width * BoardPainter.Width, r.Height * BoardPainter.Height);
        static void CheckFamily(IEnumerable<RectangleF> source)
        {
            var rectangles = source.ToArray();
            foreach (var selected in rectangles)
            {
                using var first = new Bitmap(1100, 1200);
                using var second = new Bitmap(1100, 1200);
                using (var a = Graphics.FromImage(first))
                using (var b = Graphics.FromImage(second))
                {
                    a.Clear(UiDesign.Plate);
                    b.Clear(UiDesign.Plate);
                    a.SmoothingMode = b.SmoothingMode = SmoothingMode.AntiAlias;
                    VrTheme.Tile(a, selected, false, UiDesign.Surface, UiDesign.Border);
                    VrTheme.Tile(b, selected, true, UiDesign.Surface, UiDesign.Border);
                }
                foreach (var neighbour in rectangles.Where(r => r != selected))
                    for (int y = (int)Math.Floor(neighbour.Top); y <= (int)Math.Ceiling(neighbour.Bottom); y++)
                        for (int x = (int)Math.Floor(neighbour.Left); x <= (int)Math.Ceiling(neighbour.Right); x++)
                            Check(first.GetPixel(x, y) == second.GetPixel(x, y), "Selection spill reached a neighbouring component.");
            }
        }
    }

    [TestMethod]
    public void DesignValues()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "docs", "DESIGN.md")))
            directory = Directory.GetParent(directory)?.FullName ?? throw new InvalidOperationException("Cannot find docs/DESIGN.md.");
        string text = File.ReadAllText(Path.Combine(directory, "docs", "DESIGN.md"));
        var values = Regex.Matches(text.Split("---")[1], @"(?m)^  (\w+):\s*([^\r\n]+)")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim().Trim('"'));
        Check(text.Contains("personal-ui-foundation@" + UiDesign.FoundationVersion), "Foundation version must match.");
        foreach (var (name, color) in Colors())
        {
            string expected = color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
            Check(values[name] == expected, "Design color differs: " + name);
        }
        foreach (var (name, value) in Metrics())
            Check(Math.Abs(double.Parse(values[name], CultureInfo.InvariantCulture) - value) < .00001, "Design metric differs: " + name);
        Check(UiTheme.Accent == VrTheme.Accent && UiTheme.Text == VrTheme.Text, "Desktop and VR share the foundation accent and ink.");
    }

    [TestMethod]
    public void Contrast()
    {
        string output = Path.GetFullPath("artifacts/foundation-tests");
        Directory.CreateDirectory(output);
        FoundationComparison.Contrast(output);
        using var tile = Tile(new(20, 20, 180, 80), true);
        double red = 0, green = 0, blue = 0;
        for (int y = 55; y < 65; y++)
            for (int x = 105; x < 115; x++)
            {
                var pixel = tile.GetPixel(x, y);
                red += pixel.R;
                green += pixel.G;
                blue += pixel.B;
            }
        var center = Color.FromArgb((int)(red / 100), (int)(green / 100), (int)(blue / 100));
        Check(FoundationComparison.Ratio(UiDesign.OnAccent, center) >= 4.5, "Rendered glow center average text contrast.");
    }

    [TestMethod]
    public void ExportUnchanged()
    {
        var sample = FoundationCapture.Sample();
        string folder = Path.GetFullPath(Path.Combine("artifacts/foundation-tests", Guid.NewGuid().ToString("N")));
        var paths = new MemoStore(folder).Save(sample, stream => MemoRenderer.WritePng(sample, stream));
        using var saved = new Bitmap(paths.PngPath);
        // Frozen from the approved light prototype; the same exporter predates the UI migration.
        Check(saved.Size == new Size(1024, 768), "Export dimensions must not change.");
        Check(Convert.ToHexString(SHA256.HashData(FoundationPixels.Read(saved))) ==
            "6FFEA2B36E9B98B944FE31A77C839254C8072881BD851601DCACF8A1D01A374F", "Saved PNG pixels changed.");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths.JsonPath))) ==
            "9A1C7EFCEFEEE100982AD958ECE8D75FFDEFC98D7A29983B6E83158DE06FD192", "Saved JSON bytes changed.");
    }

    private static Bitmap Tile(RectangleF rect, bool selected)
    {
        var image = new Bitmap(240, 140);
        using var g = Graphics.FromImage(image);
        g.Clear(UiDesign.Plate);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        VrTheme.Tile(g, rect, selected, UiDesign.Surface, UiDesign.Border);
        return image;
    }

    private static (string Name, Color Value)[] Colors() =>
    [
        (nameof(UiDesign.Plate), UiDesign.Plate), (nameof(UiDesign.Surface), UiDesign.Surface),
        (nameof(UiDesign.Ink), UiDesign.Ink), (nameof(UiDesign.Muted), UiDesign.Muted),
        (nameof(UiDesign.Border), UiDesign.Border), (nameof(UiDesign.Control), UiDesign.Control),
        (nameof(UiDesign.Accent), UiDesign.Accent), (nameof(UiDesign.AccentInk), UiDesign.AccentInk),
        (nameof(UiDesign.OnAccent), UiDesign.OnAccent), (nameof(UiDesign.Star), UiDesign.Star),
        (nameof(UiDesign.Notice), UiDesign.Notice), (nameof(UiDesign.NoticeInk), UiDesign.NoticeInk),
        (nameof(UiDesign.Error), UiDesign.Error), (nameof(UiDesign.ErrorInk), UiDesign.ErrorInk),
        (nameof(UiDesign.Shadow), UiDesign.Shadow), (nameof(UiDesign.DesktopCanvas), UiDesign.DesktopCanvas),
        (nameof(UiDesign.DesktopSurface), UiDesign.DesktopSurface), (nameof(UiDesign.DesktopBorder), UiDesign.DesktopBorder),
        (nameof(UiDesign.DesktopHover), UiDesign.DesktopHover), (nameof(UiDesign.DesktopPressed), UiDesign.DesktopPressed),
    ];

    private static (string Name, double Value)[] Metrics() =>
    [
        (nameof(UiDesign.BodyPixels), UiDesign.BodyPixels), (nameof(UiDesign.RadiusPerBody), UiDesign.RadiusPerBody),
        (nameof(UiDesign.PlateRadiusExtraPerBody), UiDesign.PlateRadiusExtraPerBody), (nameof(UiDesign.VrRadius), UiDesign.VrRadius),
        (nameof(UiDesign.VrPlateRadius), UiDesign.VrPlateRadius), (nameof(UiDesign.DesktopRadius), UiDesign.DesktopRadius),
        (nameof(UiDesign.PaperRadius), UiDesign.PaperRadius), (nameof(UiDesign.GrainEveryPixels), UiDesign.GrainEveryPixels),
        (nameof(UiDesign.GrainWhiteAlpha), UiDesign.GrainWhiteAlpha), (nameof(UiDesign.GrainBlackAlpha), UiDesign.GrainBlackAlpha),
        (nameof(UiDesign.SelectedGlowAlpha), UiDesign.SelectedGlowAlpha), (nameof(UiDesign.SelectedGlowGrowDivisor), UiDesign.SelectedGlowGrowDivisor),
        (nameof(UiDesign.HaloWidth), UiDesign.HaloWidth), (nameof(UiDesign.HaloAlpha), UiDesign.HaloAlpha),
        (nameof(UiDesign.ShadowOffsetY), UiDesign.ShadowOffsetY), (nameof(UiDesign.FocusWidth), UiDesign.FocusWidth),
        (nameof(UiDesign.BorderWidth), UiDesign.BorderWidth),
    ];
}
