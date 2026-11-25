using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal static class VrOverlaySprites
{
    public const float ResizeVisualWidthMetres = BoardResize.HandleWidthMetres;
    public static BoardPose ResizeVisualPose(BoardPose handle) => handle;

    public static Bitmap MakeResizeHandle(bool highlighted = false, bool enabled = true, bool resizing = false)
    {
        var image = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        VrTheme.Tile(g, new(3, 3, 122, 122), highlighted, VrTheme.Surface, VrTheme.Control, enabled);
        using var arrow = new Pen(!enabled ? VrTheme.Secondary : highlighted ? VrTheme.OnAccent : VrTheme.AccentInk, 8)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(arrow, 30, 30, 98, 98);
        g.DrawLines(arrow, [new Point(30, 60), new(30, 30), new(60, 30)]);
        g.DrawLines(arrow, [new Point(68, 98), new(98, 98), new(98, 68)]);
        if (resizing)
        {
            using var heldBorder = new Pen(VrTheme.OnAccent, 4);
            using var heldShape = UiTheme.Rounded(new RectangleF(9, 9, 110, 110), VrTheme.Radius);
            g.DrawPath(heldBorder, heldShape);
        }
        return image;
    }

    public static Bitmap MakeDashboard(int hoverMask = 0, ToggleGestureMode gestureMode = ToggleGestureMode.WristButton, bool boardVisible = false,
        HandSide handedness = HandSide.Right, float boardBrightness = AppOptions.DefaultBoardBrightness, string? settingsNotice = null,
        WristButtonActivationMode activationMode = WristButtonActivationMode.DoubleTap, string? calibrationNotice = null, bool calibrationActive = false)
    {
        var image = new Bitmap(DashboardControls.Width, DashboardControls.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        VrTheme.PlateFill(g, new(0, 0, image.Width, image.Height));
        using var title = new Font(UiTheme.FontFamily, 29, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new Font(UiTheme.FontFamily, 20, FontStyle.Regular, GraphicsUnit.Pixel);
        using var choiceText = new Font(UiTheme.FontFamily, 18, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(VrTheme.Text);
        using var secondary = new SolidBrush(VrTheme.Secondary);
        using var onAccent = new SolidBrush(VrTheme.OnAccent);
        using var errorInk = new SolidBrush(VrTheme.ErrorInk);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        string[] labels = [boardVisible ? "ノートを隠す" : "ノートを出す", "ノートと道具を\n正面に戻す", "終了"];
        for (int i = 0; i < DashboardControls.Count; i++)
        {
            var rect = DashboardControls.Bounds(i);
            if (i == 11 && !calibrationActive)
                continue;
            bool selected = i is >= 3 and < 6 ? Math.Abs(DashboardControls.Brightness(i) - boardBrightness) < .001f :
                i is >= 6 and < 8 ? DashboardControls.Gesture(i) == gestureMode : i is 8 or 9 && DashboardControls.Activation(i) == activationMode;
            VrTheme.Tile(g, rect, selected, VrTheme.Surface, VrTheme.Border);
            if ((hoverMask & (1 << i)) != 0 && !(i == 10 && calibrationActive))
            {
                using var outline = UiTheme.Rounded(rect, VrTheme.Radius);
                using var highlight = new Pen(VrTheme.Accent, 2);
                g.DrawPath(highlight, outline);
            }
            string label = i switch
            {
                < 3 => labels[i],
                < 6 => $"{DashboardControls.Brightness(i) * 100:0}%",
                < 8 => DashboardControls.GestureLabel(DashboardControls.Gesture(i)),
                < 10 => DashboardControls.ActivationLabel(DashboardControls.Activation(i)),
                10 => calibrationActive ? "位置合わせ中" : "手首の位置を合わせる",
                _ => "位置合わせを中止"
            };
            g.DrawString(label, i < 6 ? title : choiceText,
                selected ? onAccent : i == 2 ? errorInk : i == 10 && calibrationActive ? secondary : ink, rect, centered);
        }
        g.DrawString("HnVRMemo  ·  " + (boardVisible ? "ノートは表示中" : "ノートは非表示"), title, ink, 20, 8);
        bool leftHanded = handedness == HandSide.Left;
        string hand = leftHanded ? "右手" : "左手";
        string gesture = gestureMode == ToggleGestureMode.WristButton
            ? $"出す・隠す：{hand}のタップ位置を見て、{(activationMode == WristButtonActivationMode.DoubleTap ? "2回タップ" : "1回タップ")}"
            : "出す・隠す：下のボタン、または SteamVR で割り当てたボタン";
        g.DrawString(gesture, text, ink, 20, 53);
        g.DrawString("持つ：黄色の物をグリップ / 書く・消す：道具を持ってトリガー", text, ink, 20, 86);
        g.DrawString($"広げる：{(leftHanded ? "左下" : "右下")}の矢印でペン先＋トリガー、または空いた手＋グリップ", text, ink, 20, 119);
        g.DrawString(settingsNotice ?? "ボタンで操作するには、SteamVR で「ノートを出す・隠す」を割り当てます。", text,
            settingsNotice is null ? ink : errorInk, 20, 320);
        g.DrawString("ノートの明るさ（VR）", text, ink, new RectangleF(20, 370, 222, 64), centered);
        g.DrawString("手首タップ", text, ink, new RectangleF(20, 452, 152, 64), centered);
        g.DrawString("タップ回数", text, ink, new RectangleF(20, 534, 152, 64), centered);
        g.DrawString(calibrationNotice ?? WristCalibrationController.CalibrationPurpose, text, ink,
            new RectangleF(20, 696, 860, 68));
        return image;
    }

    public static Bitmap MakeWristCue(bool confirmed)
    {
        var image = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        // Both cues use accent; the check mark distinguishes confirmation.
        using var surface = new SolidBrush(VrTheme.Accent);
        using var border = new Pen(VrTheme.OnAccent, 7);
        g.FillEllipse(surface, 5, 5, 118, 118);
        g.DrawEllipse(border, 5, 5, 118, 118);
        if (confirmed)
        {
            using var ink = new Pen(VrTheme.OnAccent, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(ink, [new(34, 65), new(56, 85), new(94, 42)]);
        }
        return image;
    }

    public static Bitmap MakeWristCalibration(string notice)
    {
        var image = new Bitmap(800, 160, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(VrTheme.Plate);
        using var title = new Font(UiTheme.FontFamily, 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new Font(UiTheme.FontFamily, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(VrTheme.Text);
        g.DrawString("手首の位置合わせ", title, ink, 18, 10);
        g.DrawString(notice, text, ink, new RectangleF(18, 52, 764, 100));
        return image;
    }

    public static Bitmap MakeMarker(Color color)
    {
        var image = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 12, 12, 40, 40);
        g.DrawEllipse(Pens.White, 12, 12, 40, 40);
        return image;
    }

    public static Bitmap MakeDashboardTab()
    {
        var image = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(VrTheme.Plate);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        ToolbarGlyph.Draw(g, ToolbarIcon.Pen, new(52, 20, 152, 152), VrTheme.Accent, VrTheme.Plate);
        using var font = new Font(UiTheme.FontFamily, 40, FontStyle.Bold, GraphicsUnit.Pixel);
        using var centered = new StringFormat { Alignment = StringAlignment.Center };
        using var ink = new SolidBrush(VrTheme.Text);
        g.DrawString("手書きメモ", font, ink, new RectangleF(0, 183, 256, 62), centered);
        return image;
    }
}
