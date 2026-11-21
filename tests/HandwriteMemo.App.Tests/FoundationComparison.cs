using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Text;

namespace HandwriteMemo.App;

internal static class FoundationComparison
{
    internal static void Run(string beforeDirectory, string afterDirectory, string output)
    {
        Directory.CreateDirectory(output);
        var differences = new StringBuilder("scene,changed_pixels\n");
        using var font = new Font(UiTheme.FontFamily, 25, FontStyle.Regular, GraphicsUnit.Pixel);
        foreach (string source in Directory.GetFiles(beforeDirectory, "*.png"))
        {
            string name = Path.GetFileNameWithoutExtension(source);
            if (name == "exported-page") continue;
            using var before = new Bitmap(source);
            using var after = new Bitmap(Path.Combine(afterDirectory, name + ".png"));
            differences.AppendLine($"{name},{FoundationPixels.Differences(before, after)}");
            foreach (string background in name.StartsWith("vr-") ? new[] { "light", "dark", "pattern" } : new[] { "pc" })
                Pair(before, after, name + "-" + background, "変更前", "変更後", background);
        }
        Overview("pc", ["pc-editor-pc", "pc-selection-pc", "pc-trash-pc", "pc-delete-confirm-pc", "pc-erase-hover-pc", "pc-button-states-pc"]);
        Overview("vr", ["vr-editor-pattern", "vr-list-pattern", "vr-dashboard-pattern", "vr-erase-proximity-pattern",
            "vr-wrist-pending-pattern", "vr-wrist-confirmed-pattern", "vr-notice-pattern", "vr-resize-held-pattern"]);
        Contrast(output);
        CheckPaper(beforeDirectory, afterDirectory, output);
        File.WriteAllText(Path.Combine(output, "pixels.csv"), differences.ToString());

        void Pair(Bitmap left, Bitmap right, string name, string leftTitle, string rightTitle, string background)
        {
            if (left.Size != right.Size) throw new InvalidOperationException("Mismatched scene dimensions: " + name);
            int panelWidth = Math.Max(480, left.Width);
            using var bitmap = new Bitmap(panelWidth * 2 + 48, Math.Max(160, left.Height) + 92);
            using var g = Graphics.FromImage(bitmap);
            Background(g, bitmap.Size, background);
            g.FillRectangle(Brushes.WhiteSmoke, 0, 0, bitmap.Width, 70);
            g.DrawString(leftTitle, font, Brushes.Black, 16, 6);
            g.DrawString(rightTitle, font, Brushes.Black, panelWidth + 32, 6);
            using var captionFont = new Font(UiTheme.FontFamily, 18, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString(name, captionFont, Brushes.Black, 16, 42);
            g.DrawImageUnscaled(left, 16, 78);
            g.DrawImageUnscaled(right, panelWidth + 32, 78);
            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        }
        void Overview(string name, string[] files)
        {
            const int CellWidth = 1280;
            var rows = new int[(files.Length + 1) / 2];
            for (int i = 0; i < files.Length; i++)
            {
                using var scene = new Bitmap(Path.Combine(output, files[i] + ".png"));
                rows[i / 2] = Math.Max(rows[i / 2], (int)(scene.Height * (CellWidth - 20f) / scene.Width) + 20);
            }
            using var bitmap = new Bitmap(CellWidth * 2, rows.Sum());
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.FromArgb(65, 68, 76));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < files.Length; i++)
            {
                using var scene = new Bitmap(Path.Combine(output, files[i] + ".png"));
                float scale = (CellWidth - 20f) / scene.Width;
                g.DrawImage(scene, i % 2 * CellWidth + 10, rows.Take(i / 2).Sum() + 10, scene.Width * scale, scene.Height * scale);
            }
            bitmap.Save(Path.Combine(output, "overview-" + name + ".png"), ImageFormat.Png);
        }
    }

    private static void Background(Graphics g, Size size, string name)
    {
        g.Clear(name == "light" ? Color.FromArgb(238, 238, 238) : name == "pc" ? Color.WhiteSmoke : Color.FromArgb(27, 30, 38));
        if (name != "pattern") return;
        using var tile = new SolidBrush(Color.FromArgb(138, 151, 163));
        using var stripe = new Pen(Color.FromArgb(183, 152, 112), 16);
        for (int y = 0; y < size.Height; y += 64)
            for (int x = 0; x < size.Width; x += 64)
                if ((x / 64 + y / 64) % 2 == 0) g.FillRectangle(tile, x, y, 64, 64);
        for (int x = -size.Height; x < size.Width; x += 180) g.DrawLine(stripe, x, 0, x + size.Height, size.Height);
    }

    internal static void Contrast(string output)
    {
        var report = new StringBuilder("同梱基盤 check.py と同じ sRGB 相対輝度・比率。単一画素の粒子と輪郭のアンチエイリアスは対象外。\n");
        Add("VR", "ink/surface", VrTheme.Text, VrTheme.Surface, 4.5);
        Add("VR", "muted/surface (disabled text)", VrTheme.Secondary, VrTheme.Surface, 4.5);
        Add("VR", "ink/plate", VrTheme.Text, VrTheme.Plate, 4.5);
        Add("VR", "onAccent/accent", VrTheme.OnAccent, VrTheme.Accent, 4.5);
        Add("VR", "accentInk/surface", VrTheme.AccentInk, VrTheme.Surface, 4.5);
        Add("VR", "noticeInk/notice", VrTheme.NoticeInk, VrTheme.Notice, 4.5);
        Add("VR", "errorInk/error", VrTheme.ErrorInk, VrTheme.Error, 4.5);
        Add("VR", "errorInk/surface (dashboard exit)", VrTheme.ErrorInk, VrTheme.Surface, 4.5);
        Add("VR", "errorInk/plate (dashboard error)", VrTheme.ErrorInk, VrTheme.Plate, 4.5);
        Add("VR", "control/surface", VrTheme.Control, VrTheme.Surface, 3);
        Add("VR", "accent/surface", VrTheme.Accent, VrTheme.Surface, 3);
        Add("VR", "accent/plate", VrTheme.Accent, VrTheme.Plate, 3);
        Add("VR", "star/surface (mark)", VrTheme.Star, VrTheme.Surface, 3);
        Add("VR", "disabled history icon/plate", VrTheme.Secondary, VrTheme.Plate, 3);
        Add("VR", "onAccent/glow center", VrTheme.OnAccent, Lighten(VrTheme.Accent, UiDesign.SelectedGlowAlpha), 4.5);
        Add("PC", "ink/white", UiTheme.Text, UiTheme.Surface, 4.5);
        Add("PC", "muted/background", UiTheme.Secondary, UiTheme.Background, 4.5);
        Add("PC", "ink/hover", UiTheme.Text, UiTheme.Hover, 4.5);
        Add("PC", "ink/pressed", UiTheme.Text, UiTheme.Pressed, 4.5);
        Add("PC", "onAccent/selection", UiTheme.OnAccent, UiTheme.Selection, 4.5);
        Add("PC", "accent/white", UiTheme.Accent, UiTheme.Surface, 3);
        Add("PC", "accent/background", UiTheme.Accent, UiTheme.Background, 3);
        Add("PC", "disabled text/background", SystemColors.GrayText, UiTheme.Background, 4.5);
        Add("cursor", "foundation hover/white paper", UiTheme.AccentInk, Color.White, 3);
        Add("cursor", "foundation contact/white paper", UiTheme.Text, Color.White, 3);
        File.WriteAllText(Path.Combine(output, "contrast.txt"), report.ToString());

        void Add(string theme, string pair, Color foreground, Color background, double minimum, bool required = true)
        {
            double ratio = Ratio(foreground, background);
            report.AppendLine($"{theme} {pair}: {ColorTranslator.ToHtml(foreground)}/{ColorTranslator.ToHtml(background)} = {ratio:F3}:1; min {minimum}; {(ratio >= minimum ? "PASS" : "FAIL")}");
            if (required && ratio < minimum) throw new InvalidOperationException(report.ToString());
        }
    }

    private static Color Lighten(Color color, int alpha) => Color.FromArgb(
        (int)(color.R + (255 - color.R) * alpha / 255d), (int)(color.G + (255 - color.G) * alpha / 255d), (int)(color.B + (255 - color.B) * alpha / 255d));
    internal static double Ratio(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    private static double Luminance(Color color)
    {
        double Channel(byte value) => value / 255d <= .03928 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }

    private static void CheckPaper(string beforeDirectory, string afterDirectory, string output)
    {
        using var before = new Bitmap(Path.Combine(beforeDirectory, "exported-page.png"));
        var report = new StringBuilder();
        {
            using var after = new Bitmap(Path.Combine(afterDirectory, "exported-page.png"));
            int differences = 0;
            for (int y = 0; y < before.Height; y++)
                for (int x = 0; x < before.Width; x++)
                    if (before.GetPixel(x, y) != after.GetPixel(x, y)) differences++;
            report.AppendLine($"after: exported PNG pixel differences = {differences}");
            if (differences != 0) throw new InvalidOperationException(report.ToString());
            bool jsonEqual = File.ReadAllBytes(Path.Combine(beforeDirectory, "exported-page.json"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(afterDirectory, "exported-page.json")));
            report.AppendLine($"after: exported JSON byte identical = {jsonEqual}");
            if (!jsonEqual) throw new InvalidOperationException(report.ToString());
        }
        File.WriteAllText(Path.Combine(output, "paper-check.txt"), report.ToString());
    }
}
