using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BoardToolbarRecoveryTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath("artifacts/task-039/after");
        Directory.CreateDirectory(root);
        string run = Path.Combine(root, "recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        string blocked = Path.Combine(run, "blocked-folder");
        var page = new MemoSession(engine, blocked, DateTimeOffset.UnixEpoch, _ => { });
        page.VrPoint(new(new(0, 0, .03f), true), true, .15f, 1, DrawingTool.Pen);
        page.VrPoint(new(new(.03f, 0, .03f), true), false, .15f, 2, DrawingTool.Pen);
        using var locked = new FileStream(Directory.GetFiles(blocked, "*.json").Single(), FileMode.Open, FileAccess.Read, FileShare.None);
        page.VrPoint(new(new(.05f, .05f, .03f), true), true, .15f, 3, DrawingTool.Pen);
        page.VrPoint(new(new(.08f, .05f, .03f), true), false, .15f, 4, DrawingTool.Pen);
        Check(page.Error is not null && engine.Strokes.Count == 2, "Save failure keeps actual ink");
        using var painter = new BoardPainter();
        painter.Draw(engine, default, VrInputStatus.ForPage(page), page: page);
        using var failedBoard = (Bitmap)painter.Image.Clone();
        painter.ToolsImage.Save(Path.Combine(root, "dock-save-error.png"), ImageFormat.Png);
        BoardToolbarTests.SaveScene(painter.Image, painter.ToolsImage, false, Path.Combine(root, "scene-save-error.png"));
        Check(painter.ToolsImage.GetPixel(10, 10).A > 0, "Failure is visible above external toolbar");
        locked.Dispose();
        Check(page.Save(5) && page.Error is null, "Save retries after releasing the isolated file lock");
        painter.Draw(engine, default, VrInputStatus.ForPage(page), page: page);
        Check(painter.ToolsImage.GetPixel(10, 10).A == 0, "Successful retry clears notice");
        for (int y = 0; y < failedBoard.Height; y++)
            for (int x = 0; x < failedBoard.Width; x++)
                Check(failedBoard.GetPixel(x, y) == painter.Image.GetPixel(x, y), "Error and retry must never paint over paper");
        BoardToolbarTests.SaveScene(painter.Image, painter.ToolsImage, false, Path.Combine(root, "scene-save-recovered.png"));

        string gallery = Path.Combine(run, "gallery");
        var store = new MemoStore(gallery);
        for (int i = 0; i < 4; i++)
        {
            var date = DateTimeOffset.UnixEpoch.AddDays(i);
            var doc = new MemoDocument(2, Guid.NewGuid(), date, date.AddSeconds(1), BoardSize.Default,
                [new MemoStroke(date, [new MemoPoint(.04f, .02f, 0), new MemoPoint(.36f, .30f, 1)], ToolPalette.Colors[i])], i == 0,
                i == 1 ? "長いページ名を途中で省略してもカードの外にはみ出さない" : "");
            store.Save(doc, stream => MemoRenderer.WritePng(doc, stream));
        }
        var listEngine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var listPage = new MemoSession(listEngine, gallery, DateTimeOffset.UnixEpoch, _ => { });
        using var browser = new VrPageBrowser(listPage);
        Check(listPage.Open(listPage.Catalog.List().First(), 0), "Open gallery page");
        browser.Handle(default(FrameResult) with { Commands = [BoardCommand.PageList] }, 1);
        foreach (var (name, size) in new[] { ("standard", BoardSize.Default), ("expanded", new BoardSize(.64f, .44f)), ("maximum", BoardResize.Maximum) })
        {
            listEngine.ResizeBoard(size);
            painter.Draw(listEngine, default, VrInputStatus.ForPage(listPage), page: listPage, vrPages: browser);
            BoardToolbarTests.SaveScene(painter.Image, painter.ToolsImage, false, Path.Combine(root, "gallery-" + name + ".png"), showHandle: false);
        }
        File.WriteAllText(Path.Combine(gallery, "unreadable.json"), "invalid JSON");
        browser.Handle(default(FrameResult) with { Commands = [BoardCommand.RefreshList] }, 2);
        Check(browser.Notice is not null, "Unreadable list entry reports recovery");
        painter.Draw(listEngine, default, VrInputStatus.ForPage(listPage), page: listPage, vrPages: browser);
        BoardToolbarTests.SaveScene(painter.Image, painter.ToolsImage, false, Path.Combine(root, "gallery-read-error.png"), showHandle: false);
        Console.WriteLine("PASS: actual save failure/retry, notice outside paper, unchanged paper pixels; three-card gallery and read-error images.");
    }
}
