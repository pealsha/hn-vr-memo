using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BoardToolbarTests
{
    private static readonly string s_root = Path.GetFullPath("artifacts/task-039");

    [TestMethod]
    public void Images()
    {
        string output = Path.Combine(s_root, "after");
        Directory.CreateDirectory(output);
        var origin = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var folder = Path.Combine(output, "pages-" + Guid.NewGuid().ToString("N"));
        var store = new MemoStore(folder);
        var sample = new MemoDocument(2, Guid.Parse("65d871a6-2682-431d-a7ca-f04a19755493"), origin, origin.AddMinutes(1), BoardSize.Default,
        [
            Line("#182634", .0015f, new(.02f, .065f), new(.02f, .09f), new(.06f, .09f), new(.06f, .065f)),
            Line("#C53939", .003f, new(.08f, .17f), new(.30f, .17f)),
            Line("#2464C4", .00075f, new(.08f, .22f), new(.30f, .22f)),
            Line("#23834B", .0015f, new(.10f, .27f), new(.13f, .25f), new(.16f, .27f), new(.19f, .25f)),
        ], true, "既存のページ");
        store.Save(sample, stream => MemoRenderer.WritePng(sample, stream));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, folder, origin.AddMinutes(2), _ => { });
        using var browser = new VrPageBrowser(page);
        Check(page.Open(page.Catalog.List().Single(), 0), "Open isolated existing page");
        using var painter = new BoardPainter();
        foreach (var (name, size) in new[] { ("standard", BoardSize.Default), ("expanded", new BoardSize(.64f, .44f)), ("maximum", BoardResize.Maximum) })
        {
            engine.ResizeBoard(size);
            Capture(name, false);
            Capture(name + "-left", true);
            var document = MemoDocument.Capture(sample.Id, origin, origin.AddMinutes(2), engine.BoardSize, engine.Strokes);
            using var png = MemoRenderer.Render(document);
            png.Save(Path.Combine(output, "page-" + name + ".png"), ImageFormat.Png);
        }
        Console.WriteLine("PASS: board/dock/list scenes at standard, expanded, maximum, both handedness.");

        MemoStroke Line(string color, float width, params Vector2[] points) => new(origin,
            points.Select((p, i) => new MemoPoint(p.X, p.Y, i * .1)).ToArray(), color, width);

        void Capture(string name, bool left)
        {
            painter.Draw(engine, default, VrInputStatus.ForPage(page), page: page, vrPages: browser);
            painter.Image.Save(Path.Combine(output, "board-" + name + ".png"), ImageFormat.Png);
            painter.ToolsImage.Save(Path.Combine(output, "dock-" + name + ".png"), ImageFormat.Png);
            SaveScene(painter.Image, painter.ToolsImage, left, Path.Combine(output, "scene-" + name + ".png"));
            browser.Handle(default(FrameResult) with { Commands = [BoardCommand.PageList] }, 1);
            painter.Draw(engine, default, VrInputStatus.ForPage(page), page: page, vrPages: browser);
            SaveScene(painter.Image, painter.ToolsImage, left, Path.Combine(output, "list-" + name + ".png"), showHandle: false);
            browser.ReturnToPage();
        }
    }

    internal static void SaveScene(Bitmap board, Bitmap dock, bool left, string path, bool showHandle = true)
    {
        int offset = (int)(BoardResize.HandleOffsetMetres * MemoRenderer.PixelsPerMetre);
        int handleWidth = (int)(BoardResize.HandleWidthMetres * MemoRenderer.PixelsPerMetre);
        int margin = offset + handleWidth;
        using var image = new Bitmap(board.Width + margin * 2, board.Height + dock.Height + 8 + margin);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.FromArgb(57, 65, 74));
        g.DrawImageUnscaled(dock, margin, 0);
        g.DrawImageUnscaled(board, margin, dock.Height + 8);
        if (!showHandle)
        {
            image.Save(path, ImageFormat.Png);
            return;
        }
        using var handle = VrOverlaySprites.MakeResizeHandle(highlighted: true);
        if (left)
            handle.RotateFlip(RotateFlipType.RotateNoneFlipX);
        int x = left ? margin - offset : margin + board.Width + offset;
        g.DrawImage(handle, new Rectangle(x - handleWidth / 2, dock.Height + 8 + board.Height + offset - handleWidth / 2, handleWidth, handleWidth));
        image.Save(path, ImageFormat.Png);
    }
}
