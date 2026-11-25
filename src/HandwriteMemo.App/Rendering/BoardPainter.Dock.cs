using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class BoardPainter
{
    private readonly record struct DockNotice(string? Text, bool IsError, bool SaveFailed);
    private readonly record struct EditorDockState(string PenColor, float PenWidth, EraserMode Eraser,
        bool Pinned, bool UndoEnabled, bool RedoEnabled, DockNotice Notice);
    private EditorDockState? _editorDockState;

    public Bitmap ToolsImage { get; } = new(ToolPalette.DockWidthPixels, ToolPalette.DockHeightPixels, PixelFormat.Format32bppArgb);

    // This overlay stays at a fixed physical offset from the paper's top-left corner.
    // Its transparent upper area is used only for recovery guidance, never over ink.
    public static BoardPose ToolsPose(BoardPose board, BoardSize size) => new(board.LocalToWorld(new(
        (BoardSize.Default.WidthMetres - size.WidthMetres) / 2,
        size.HeightMetres / 2 + (ToolPalette.DockHeightPixels / 2f + ToolPalette.DockGapPixels) / MemoRenderer.PixelsPerMetre, 0)), board.Rotation);

    private void DrawDock(HandwriteEngine engine, FrameResult frame, VrInputStatus status, MemoSession? page, VrPageBrowser? vrPages, bool list)
    {
        var notice = NoticeForDock(status, page, list ? vrPages : null);
        DrawDock(engine, frame, notice, page, vrPages, list);
        _editorDockState = !list && frame.CursorUv is not { Y: < 0 } ? EditorDock(engine, page, notice) : null;
    }

    internal bool DrawResizeDock(HandwriteEngine engine, VrInputStatus status, MemoSession page)
    {
        var notice = NoticeForDock(status, page, null);
        var state = EditorDock(engine, page, notice);
        if (_editorDockState == state)
            return false;
        // Resize owns input, so there is no dock hover.
        DrawDock(engine, default, notice, page, null, list: false);
        _editorDockState = state;
        return true;
    }

    private static EditorDockState EditorDock(HandwriteEngine engine, MemoSession? page, DockNotice notice) => new(
        engine.PenColor, engine.PenWidthMetres, engine.EraserMode, page?.IsPinned == true,
        engine.CanUndo && page?.IsBusy != true, engine.CanRedo && page?.IsBusy != true, notice);

    private static DockNotice NoticeForDock(VrInputStatus status, MemoSession? page, VrPageBrowser? list)
    {
        string? guide = page?.VrFailureMessage ?? page?.Notice ?? list?.Notice;
        if (guide is null && (status.ShowInDock || status.IsError))
            guide = status.Text;
        return new(guide, page?.VrFailureMessage is not null || status.IsError, page?.Error is not null);
    }

    private void DrawDock(HandwriteEngine engine, FrameResult frame, DockNotice notice, MemoSession? page, VrPageBrowser? vrPages, bool list)
    {
        using var g = Graphics.FromImage(ToolsImage);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        if (notice.Text is { } guide)
        {
            bool error = notice.IsError;
            using var fill = new SolidBrush(error ? VrTheme.Error : VrTheme.Notice);
            using var border = new Pen(error ? VrTheme.ErrorInk : VrTheme.NoticeInk, 3);
            using var guideInk = new SolidBrush(error ? VrTheme.ErrorInk : VrTheme.NoticeInk);
            g.FillRectangle(fill, 3, 3, Width - 6, 84);
            g.DrawRectangle(border, 3, 3, Width - 6, 84);
            if (notice.SaveFailed)
            {
                g.DrawString("保存できません · 筆跡はアプリ内に保持中", _buttonFont, guideInk, 12, 4);
                g.DrawString("操作を終え、トリガーを離して待つと自動再試行（約1秒ごと）。", _smallFont, guideInk, 12, 35);
                g.DrawString("続く場合はPCで保存先を確認し「保存を再試行」。", _smallFont, guideInk, 12, 59);
            }
            else
                g.DrawString(guide, _buttonFont, guideInk, new RectangleF(12, 8, Width - 24, 74));
        }
        DrawCommands(g, engine, frame, page, vrPages, list);
        if (!list)
            DrawPalette(g, engine, frame);
        DrawDockCursor(g, frame);
    }

    private void DrawPalette(Graphics g, HandwriteEngine engine, FrameResult frame)
    {
        VrTheme.PlateFill(g, new(0, 112, Width, 80));
        int colorCount = ToolPalette.Colors.Count;
        int swatchCount = colorCount + ToolPalette.Widths.Count;
        int hover = frame.CursorUv is { } uv ? ToolPalette.HitTest(uv, engine.BoardSize) : -1;
        for (int i = 0; i < ToolPalette.ButtonCount; i++)
        {
            var rect = DockBounds(ToolPalette.Bounds(i));
            if (i == colorCount || i == swatchCount)
                DrawDockSeparator(g, DockBounds(ToolPalette.Bounds(i - 1)).Right, rect);
            bool selected = engine.IsPaletteSelected(i);
            if (i < swatchCount)
            {
                VrTheme.SwatchTile(g, rect, selected, hover == i);
                bool colorSwatch = i < colorCount;
                float diameter = colorSwatch ? 28 : ToolPalette.Widths[i - colorCount] / ToolPalette.Widths[^1] * 28;
                using var ink = new SolidBrush(colorSwatch ? ColorTranslator.FromHtml(ToolPalette.Colors[i]) : VrTheme.Text);
                g.FillEllipse(ink, rect.X + (rect.Width - diameter) / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);
            }
            else
            {
                VrTheme.Tile(g, rect, selected, VrTheme.Surface, VrTheme.Border);
                if (hover == i)
                {
                    using var pen = new Pen(VrTheme.AccentInk, 3);
                    using var outline = UiTheme.Rounded(RectangleF.Inflate(rect, 3, 3), VrTheme.Radius);
                    g.DrawPath(pen, outline);
                }
                bool stroke = i == swatchCount;
                DrawButtonContent(g, rect, selected ? VrTheme.OnAccent : VrTheme.Text, selected ? VrTheme.Accent : VrTheme.Surface,
                    stroke ? "線ごと消す" : "部分消し", stroke ? ToolbarIcon.EraseStroke : ToolbarIcon.ErasePart);
            }
        }
    }

    private void DrawDockCursor(Graphics g, FrameResult frame)
    {
        if (frame.CursorUv is { } cursor && cursor.Y < 0 && frame.Contact != ContactState.None)
        {
            float x = cursor.X * Image.Width, y = cursor.Y * Image.Height + ToolPalette.DockHeightPixels + ToolPalette.DockGapPixels;
            using var pen = new Pen(VrTheme.AccentInk, 3);
            g.DrawEllipse(pen, x - 9, y - 9, 18, 18);
        }
    }

    private void DrawCommands(Graphics g, HandwriteEngine engine, FrameResult frame, MemoSession? page, VrPageBrowser? vrPages, bool list)
    {
        VrTheme.PlateFill(g, new(0, BoardControls.DockTopPixels - 8, Width, 88));
        var buttons = list ? BoardControls.ListButtons : BoardControls.EditorButtons;
        var hover = frame.CursorUv is { } target ? BoardControls.HitTest(target, list, engine.BoardSize) : null;
        for (int i = 0; i < buttons.Count; i++)
        {
            var command = buttons[i];
            var rect = DockBounds(BoardControls.ButtonBounds(i, list));
            if (!list && command is BoardCommand.PageList or BoardCommand.TogglePin or BoardCommand.Close)
                DrawDockSeparator(g, DockBounds(BoardControls.ButtonBounds(i - 1, list)).Right, rect);
            bool enabled = command switch
            {
                BoardCommand.Undo => engine.CanUndo && page?.IsBusy != true,
                BoardCommand.Redo => engine.CanRedo && page?.IsBusy != true,
                BoardCommand.PreviousPage => vrPages?.CanNavigate(-1) ?? false,
                BoardCommand.NextPage => vrPages?.CanNavigate(1) ?? false,
                _ => true,
            };
            bool pin = command == BoardCommand.TogglePin;
            bool selected = pin && page?.IsPinned == true;
            (string Label, ToolbarIcon? Icon) content = command switch
            {
                BoardCommand.Undo => ("", ToolbarIcon.Undo),
                BoardCommand.Redo => ("", ToolbarIcon.Redo),
                BoardCommand.NewPage => ("新しいページ", ToolbarIcon.NewPage),
                BoardCommand.PageList => ("一覧", null),
                BoardCommand.PreviousPage => ("", ToolbarIcon.PreviousPage),
                BoardCommand.NextPage => ("", ToolbarIcon.NextPage),
                BoardCommand.TogglePin => (selected ? "★ ピン解除" : "☆ ピン留め", null),
                BoardCommand.BackToPage => ("ページに戻る", null),
                BoardCommand.RefreshList => ("更新", ToolbarIcon.Refresh),
                _ => ("閉じる", null),
            };
            bool history = command is BoardCommand.Undo or BoardCommand.Redo;
            if (!history)
                VrTheme.Tile(g, rect, selected, VrTheme.Surface, VrTheme.Border, enabled);
            if (enabled && hover == command)
                DrawHover(g, rect);
            var ink = !enabled ? VrTheme.Secondary : selected ? VrTheme.OnAccent : pin ? VrTheme.AccentInk : VrTheme.Text;
            DrawButtonContent(g, rect, ink, history ? VrTheme.Plate : VrTheme.Surface, content.Label, content.Icon);
        }
    }

    private static RectangleF DockBounds((float Left, float Top, float Width, float Height) bounds) => new(
        bounds.Left * Width, bounds.Top * Height + ToolPalette.DockHeightPixels + ToolPalette.DockGapPixels,
        bounds.Width * Width, bounds.Height * Height);

    private static void DrawDockSeparator(Graphics g, float previousRight, RectangleF next)
    {
        float x = (previousRight + next.Left) / 2;
        using var pen = new Pen(Color.FromArgb(128, VrTheme.Control));
        g.DrawLine(pen, x, next.Top + 8, x, next.Bottom - 8);
    }

    private void DrawButtonContent(Graphics g, RectangleF rect, Color ink, Color background, string label, ToolbarIcon? icon = null)
    {
        using var format = StringFormat.GenericTypographic;
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;
        format.FormatFlags |= StringFormatFlags.NoWrap;
        float centerX = rect.Left + rect.Width / 2;
        float centerY = rect.Top + rect.Height / 2;
        if (icon is { } glyph)
        {
            float side = label.Length == 0 ? 40 : 32;
            float gap = label.Length == 0 ? 0 : 8;
            float labelWidth = label.Length == 0 ? 0 : g.MeasureString(label, _buttonFont, PointF.Empty, format).Width;
            float iconLeft = centerX - (side + gap + labelWidth) / 2;
            ToolbarGlyph.Draw(g, glyph, new(iconLeft, centerY - side / 2, side, side), ink, background);
            centerX += (side + gap) / 2;
        }
        if (label.Length > 0)
        {
            using var brush = new SolidBrush(ink);
            g.DrawString(label, _buttonFont, brush, new PointF(centerX, centerY), format);
        }
    }
}
