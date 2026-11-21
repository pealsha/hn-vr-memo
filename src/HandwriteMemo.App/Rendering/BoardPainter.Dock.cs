using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class BoardPainter
{
    public Bitmap ToolsImage { get; } = new(ToolPalette.DockWidthPixels, ToolPalette.DockHeightPixels, PixelFormat.Format32bppArgb);

    // This overlay stays at a fixed physical offset from the paper's top-left corner.
    // Its transparent upper area is used only for recovery guidance, never over ink.
    public static BoardPose ToolsPose(BoardPose board, BoardSize size) => new(board.LocalToWorld(new(
        (BoardSize.Default.WidthMetres - size.WidthMetres) / 2,
        size.HeightMetres / 2 + (ToolPalette.DockHeightPixels / 2f + ToolPalette.DockGapPixels) / MemoRenderer.PixelsPerMetre, 0)), board.Rotation);

    private static GraphicsPath Rounded(RectangleF r) => UiTheme.Rounded(r, VrTheme.Radius);

    private static void Tile(Graphics g, RectangleF r, bool selected, Color background, Color border)
    {
        VrTheme.Tile(g, r, selected, background, border);
    }

    private void DrawDock(HandwriteEngine engine, FrameResult frame, VrInputStatus status, MemoSession? page, VrPageBrowser? vrPages, bool list)
    {
        using var g = Graphics.FromImage(ToolsImage);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        string? guide = page?.VrFailureMessage ?? page?.Notice ?? (list ? vrPages?.Notice : null);
        if (guide is null && (status.ShowInDock || status.IsError))
            guide = status.Text;
        if (guide is not null)
        {
            bool error = page?.VrFailureMessage is not null || status.IsError;
            using var fill = new SolidBrush(error ? VrTheme.Error : VrTheme.Notice);
            using var border = new Pen(error ? VrTheme.ErrorInk : VrTheme.NoticeInk, 3);
            using var guideInk = new SolidBrush(error ? VrTheme.ErrorInk : VrTheme.NoticeInk);
            g.FillRectangle(fill, 3, 3, Width - 6, 84);
            g.DrawRectangle(border, 3, 3, Width - 6, 84);
            if (page?.Error is not null)
            {
                g.DrawString("保存できません · 筆跡はアプリ内に保持中", _buttonFont, guideInk, 12, 4);
                g.DrawString("操作を終え、トリガーを離して待つと自動再試行（約1秒ごと）。", _smallFont, guideInk, 12, 35);
                g.DrawString("続く場合はPCで保存先を確認し「保存を再試行」。", _smallFont, guideInk, 12, 59);
            }
            else
                g.DrawString(guide, _buttonFont, guideInk, new RectangleF(12, 8, Width - 24, 74));
        }
        DrawCommands(g, engine, frame, page, vrPages, list);
        if (list)
        {
            DrawDockCursor(g, frame);
            return;
        }
        // The tile positions and hit regions stay fixed; unused caption space is transparent.
        VrTheme.PlateFill(g, new(0, 112, Width, 80));
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        int hover = frame.CursorUv is { } uv ? ToolPalette.HitTest(uv, engine.BoardSize) : -1;
        for (int i = 0; i < ToolPalette.ButtonCount; i++)
        {
            var r = ToolPalette.Bounds(i);
            var rect = new RectangleF(r.Left * Width, r.Top * Height + ToolPalette.DockHeightPixels + ToolPalette.DockGapPixels, r.Width * Width, r.Height * Height);
            bool selected = engine.IsPaletteSelected(i);
            var fill = selected ? VrTheme.Accent : VrTheme.Surface;
            Tile(g, rect, selected, fill, VrTheme.Border);
            if (i < 7)
            {
                var inset = RectangleF.Inflate(rect, -4, -4);
                VrTheme.Tile(g, inset, false, VrTheme.Surface, VrTheme.Control);
            }
            if (hover == i)
            {
                using var pen = new Pen(VrTheme.AccentInk, 3);
                using var outline = Rounded(RectangleF.Inflate(rect, 3, 3));
                g.DrawPath(pen, outline);
            }
            if (i < 7)
            {
                float diameter = i < 4 ? 28 : ToolPalette.Widths[i - 4] / ToolPalette.Widths[2] * 28;
                using var ink = new SolidBrush(i < 4 ? ColorTranslator.FromHtml(ToolPalette.Colors[i]) : VrTheme.Text);
                g.FillEllipse(ink, rect.X + (rect.Width - diameter) / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);
            }
            else
            {
                var color = selected ? VrTheme.OnAccent : VrTheme.Text;
                using var labelInk = new SolidBrush(color);
                ToolbarGlyph.Draw(g, i == 7 ? ToolbarIcon.EraseStroke : ToolbarIcon.ErasePart, new(rect.X + 12, rect.Y + (rect.Height - 32) / 2, 32, 32), color, fill);
                g.DrawString(i == 7 ? "線ごと消す" : "部分消し", _smallFont, labelInk, new RectangleF(rect.X + 50, rect.Y, rect.Width - 54, rect.Height), centered);
            }
        }
        DrawDockCursor(g, frame);
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
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var buttons = list ? BoardControls.ListButtons : BoardControls.EditorButtons;
        for (int i = 0; i < buttons.Count; i++)
        {
            var bounds = BoardControls.ButtonBounds(i, list);
            var rect = new RectangleF(bounds.Left * Width, bounds.Top * Height + ToolPalette.DockHeightPixels + ToolPalette.DockGapPixels, bounds.Width * Width, bounds.Height * Height);
            bool enabled = buttons[i] switch
            {
                BoardCommand.Undo => engine.CanUndo && page?.IsBusy != true,
                BoardCommand.Redo => engine.CanRedo && page?.IsBusy != true,
                BoardCommand.PreviousPage => vrPages?.CanNavigate(-1) ?? false,
                BoardCommand.NextPage => vrPages?.CanNavigate(1) ?? false,
                _ => true,
            };
            var label = buttons[i] switch
            {
                BoardCommand.Undo or BoardCommand.Redo => "",
                BoardCommand.NewPage => "新しい\nページ",
                BoardCommand.PageList => "一覧",
                BoardCommand.PreviousPage => "前",
                BoardCommand.NextPage => "次",
                BoardCommand.TogglePin => page?.IsPinned == true ? "ピン解除" : "ピン留め",
                BoardCommand.BackToPage => "ページに戻る",
                BoardCommand.RefreshList => "更新",
                _ => "閉じる",
            };
            bool history = buttons[i] is BoardCommand.Undo or BoardCommand.Redo;
            if (!history)
                VrTheme.Tile(g, rect, page?.IsPinned == true && buttons[i] == BoardCommand.TogglePin,
                    VrTheme.Surface, VrTheme.Border, enabled);
            if (enabled && frame.CursorUv is { } target && BoardControls.HitTest(target, list, engine.BoardSize) == buttons[i])
                DrawHover(g, rect);
            var color = !enabled ? VrTheme.Secondary : buttons[i] == BoardCommand.TogglePin && page?.IsPinned == true ? VrTheme.OnAccent : VrTheme.Text;
            if (history)
                ToolbarGlyph.Draw(g, buttons[i] == BoardCommand.Undo ? ToolbarIcon.Undo : ToolbarIcon.Redo,
                new(rect.X + (rect.Width - 40) / 2, rect.Y + (rect.Height - 40) / 2, 40, 40), color, VrTheme.Plate);
            else
            {
                using var brush = new SolidBrush(color);
                g.DrawString(label, _buttonFont, brush, rect, centered);
            }
        }
    }
}
