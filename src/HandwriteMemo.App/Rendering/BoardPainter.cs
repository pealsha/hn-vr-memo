using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class BoardPainter : IDisposable
{
    public const int Width = BoardMetrics.ReferenceWidthPixels;
    public const int Height = BoardMetrics.ReferenceHeightPixels;
    private readonly Font _buttonFont = new(UiTheme.FontFamily, 26, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _smallFont = new(UiTheme.FontFamily, 20, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font _listFont = new(UiTheme.FontFamily, 28, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Dictionary<string, Bitmap> _previews = new(StringComparer.OrdinalIgnoreCase);
    private long _previewRevision = -1;
    private int _previewScreen = -1;
    public Bitmap Image { get; private set; } = new(Width, Height, PixelFormat.Format32bppArgb);

    public void Draw(HandwriteEngine engine, FrameResult frame, VrInputStatus status, bool highlighted = false, MemoSession? page = null, VrPageBrowser? vrPages = null)
    {
        var pixels = MemoRenderer.PixelSize(engine.BoardSize);
        if (Image.Size != pixels)
        {
            Image.Dispose();
            Image = new(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        }
        int pageWidth = Image.Width, pageHeight = Image.Height;
        using var g = Graphics.FromImage(Image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        bool list = page is not null && vrPages?.IsOpen == true;
        using var paperShape = UiTheme.Rounded(new RectangleF(0, 0, pageWidth, pageHeight), list ? VrTheme.PlateRadius : UiDesign.PaperRadius);
        g.SetClip(paperShape);
        using (var background = new SolidBrush(VrTheme.Plate))
            g.FillPath(background, paperShape);
        if (list)
            DrawPageList(g, page!, vrPages!, frame);
        else
        {
            DrawCachedInk(g, engine);
        }
        if (frame.CursorUv is { } uv && uv.Y >= 0 && frame.Contact != ContactState.None)
        {
            var color = frame.Contact == ContactState.Contact ? UiDesign.Ink : UiDesign.AccentInk;
            using var cursor = new Pen(color, 3);
            float x = uv.X * pageWidth, y = uv.Y * pageHeight;
            float rx = !list && frame.Tool == DrawingTool.Eraser ? ToolPalette.EraserRadiusMetres / engine.BoardSize.WidthMetres * pageWidth : 9;
            float ry = !list && frame.Tool == DrawingTool.Eraser ? ToolPalette.EraserRadiusMetres / engine.BoardSize.HeightMetres * pageHeight : 9;
            g.DrawEllipse(cursor, x - rx, y - ry, rx * 2, ry * 2);
            g.DrawLine(cursor, x - 5, y, x + 5, y);
            g.DrawLine(cursor, x, y - 5, x, y + 5);
        }
        if (highlighted)
        {
            using var border = new Pen(VrTheme.Accent, 12);
            using var shape = UiTheme.Rounded(new RectangleF(6, 6, pageWidth - 12, pageHeight - 12), UiDesign.PaperRadius);
            g.DrawPath(border, shape);
        }
        DrawDock(engine, frame, status, page, vrPages, list);
    }

    private void DrawPageList(Graphics g, MemoSession page, VrPageBrowser browser, FrameResult frame)
    {
        // Reference pixels keep the physical card size aligned with BoardControls.ReferenceUv when the board grows.
        using var textBrush = new SolidBrush(VrTheme.Text);
        if (_previewRevision != browser.Revision || _previewScreen != browser.Screen)
        {
            ClearPreviews();
            foreach (var entry in browser.Entries.Skip(browser.Screen * BoardControls.PagesPerScreen).Take(BoardControls.PagesPerScreen))
            {
                var card = BoardControls.CardBounds(0);
                _previews[entry.JsonPath] = PagePreview.Create(entry.Document, (int)(card.Width * Width) - 20, (int)(card.Height * Height) - 140);
            }
            _previewRevision = browser.Revision;
            _previewScreen = browser.Screen;
        }
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        if (browser.Entries.Count == 0)
        {
            g.DrawString("保存済みのページはありません", _listFont, textBrush, new RectangleF(0, 290, Width, 60), centered);
        }
        for (int row = 0; row < BoardControls.PagesPerScreen; row++)
        {
            int index = browser.Screen * BoardControls.PagesPerScreen + row;
            if (index >= browser.Entries.Count)
                break;
            var entry = browser.Entries[index];
            var r = BoardControls.CardBounds(row);
            var rect = new RectangleF(r.Left * Width, r.Top * Height, r.Width * Width, r.Height * Height);
            bool current = entry.Document.Id == page.Id;
            using var cardInk = new SolidBrush(current ? VrTheme.OnAccent : VrTheme.Text);
            using var cardSecondary = new SolidBrush(current ? VrTheme.OnAccent : VrTheme.Secondary);
            using var actionInk = new SolidBrush(current ? VrTheme.OnAccent : VrTheme.AccentInk);
            VrTheme.Tile(g, rect, current, VrTheme.Surface, VrTheme.Border);
            if (frame.CursorUv is { } uv && BoardControls.CardAt(uv, page.BoardSize) == row)
                DrawHover(g, rect);
            float previewHeight = rect.Height - 140;
            g.DrawImage(_previews[entry.JsonPath], new RectangleF(rect.Left + 10, rect.Top + 10, rect.Width - 20, previewHeight));
            float x = rect.Left + 16;
            bool live = entry.Document.Id == page.Id;
            string name = live ? page.Title : entry.Document.Title;
            using var nameFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            if (name.Length == 0)
                g.DrawString(entry.Document.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd\nHH:mm"), _listFont, cardInk, x, rect.Top + previewHeight + 25);
            else
            {
                g.DrawString(live ? page.DisplayName : entry.Document.DisplayName, _listFont, cardInk,
                    new RectangleF(x, rect.Top + previewHeight + 25, rect.Width - 32, 38), nameFormat);
                g.DrawString($"作成 {entry.Document.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm}", _smallFont, cardSecondary,
                    new RectangleF(x, rect.Top + previewHeight + 66, rect.Width - 32, 30), nameFormat);
            }
            g.DrawString((entry.Document.IsPinned ? "★ ピン留め  " : "") + (current ? "編集中" : "開く"), _buttonFont,
                actionInk,
                new RectangleF(x, rect.Bottom - 43, rect.Width - 32, 36));
        }
        var previous = new RectangleF(Width * .02f, Height * .87f, Width * .22f, Height * .11f);
        var next = new RectangleF(Width * .76f, Height * .87f, Width * .22f, Height * .11f);
        DrawListButton(previous, "前", browser.CanNavigate(-1), BoardCommand.PreviousPage, ToolbarIcon.PreviousPage);
        DrawListButton(next, "次", browser.CanNavigate(1), BoardCommand.NextPage, ToolbarIcon.NextPage);
        g.DrawString($"{browser.Screen + 1} / {browser.ScreenCount}   ·   {browser.Entries.Count} ページ", _listFont, textBrush,
            new RectangleF(Width * .25f, Height * .87f, Width * .5f, Height * .11f), centered);
        void DrawListButton(RectangleF rect, string label, bool enabled, BoardCommand command, ToolbarIcon icon)
        {
            VrTheme.Tile(g, rect, false, VrTheme.Surface, VrTheme.Border, enabled);
            if (enabled && frame.CursorUv is { } uv && BoardControls.HitTest(uv, true, page.BoardSize) == command)
                DrawHover(g, rect);
            DrawButtonContent(g, rect, enabled ? VrTheme.Text : VrTheme.Secondary, VrTheme.Surface, label, icon);
        }
    }

    private void ClearPreviews()
    {
        foreach (var image in _previews.Values)
            image.Dispose();
        _previews.Clear();
    }

    private static void DrawHover(Graphics g, RectangleF rect)
    {
        using var border = new Pen(VrTheme.Accent, 2);
        using var path = UiTheme.Rounded(rect, VrTheme.Radius);
        g.DrawPath(border, path);
    }

    public void Dispose()
    {
        ClearPreviews();
        DisposeInkCache();
        Image.Dispose();
        ToolsImage.Dispose();
        _buttonFont.Dispose();
        _smallFont.Dispose();
        _listFont.Dispose();
    }
}
