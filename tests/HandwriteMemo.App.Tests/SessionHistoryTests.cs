using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class SessionHistoryTests
{
    [TestMethod]
    public void Run()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "history-" + Guid.NewGuid().ToString("N")));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var origin = DateTimeOffset.UtcNow;
        var page = new MemoSession(engine, folder, origin, _ =>
        {
        });
        double t = 0;
        double Now() => t += .02;
        void Draw(float y)
        {
            Check(page.BeginDesktop(Now()), "desktop starts");
            page.DesktopPoint(new(.2f, y), true, Now(), DrawingTool.Pen);
            page.DesktopPoint(new(.8f, y), true, Now(), DrawingTool.Pen);
            page.DesktopPoint(null, false, Now(), DrawingTool.Pen);
        }
        PageCatalogEntry Entry(Guid id) => page.Catalog.List().Single(e => e.Document.Id == id);
        Draw(.4f);
        Draw(.6f);
        var first = page.Id;
        page.Undo(Now());
        page.Undo(Now());
        Check(page.NewPage(Now()), "new page");
        Draw(.7f);
        var second = page.Id;
        Check(page.Open(Entry(first), Now()) && engine.Strokes.Count == 0 && engine.CanRedo, "return retains redo");
        page.Redo(Now());
        page.Redo(Now());
        Check(engine.Strokes.Count == 2 && !engine.CanRedo, "redo twice after navigation");
        Check(page.Open(Entry(second), Now()) && engine.CanUndo && !engine.CanRedo, "other page retains independent undo");
        page.Undo(Now());
        Check(page.Open(Entry(first), Now()) && engine.Strokes.Count == 2, "other page undo leaves first unchanged");
        engine.SetEraserMode(EraserMode.Partial);
        Check(page.BeginDesktop(Now()), "eraser starts");
        page.DesktopPoint(new(.5f, .4f), true, Now(), DrawingTool.Eraser);
        page.DesktopPoint(null, false, Now(), DrawingTool.Eraser);
        int erasedCount = engine.Strokes.Count;
        page.Undo(Now());
        Check(page.Open(Entry(second), Now()) && engine.CanRedo, "second page redo retained");
        Check(page.Open(Entry(first), Now()), "return to erased page");
        page.Redo(Now());
        Check(engine.Strokes.Count == erasedCount, "partial erasure redo after multiple visits");
        page.Undo(Now());
        Check(page.SetVrResizing(true, Now()), "resize starts");
        Check(page.Resize(new(.8f, .6f), Now()), "resize changes size");
        page.SetVrResizing(false, Now());
        Check(page.Open(Entry(second), Now()) && page.Open(Entry(first), Now()), "navigate resized page");
        page.Redo(Now());
        Check(engine.BoardSize == new BoardSize(.8f, .6f) && engine.Strokes.Count == erasedCount, "history retains resized coordinates");
        Check(Math.Abs(engine.Strokes[0].Points[0].Uv.X * .8f - .096f) < 1e-5f, "history uses physical coordinates");
        page.Undo(Now());
        var target = Entry(second);
        using (var locked = new FileStream(target.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!page.Open(target, Now()) && page.Id == first && engine.CanRedo, "load failure retains current history");
        Check(page.RetryOpen(Now()) && page.Id == second, "load retry");
        Check(page.Open(Entry(first), Now()) && engine.CanRedo, "history survives failed load and retry");
        Draw(.65f);
        Check(!engine.CanRedo, "only the edited page loses its redo branch");
        Check(page.Open(Entry(second), Now()) && engine.CanRedo, "other page redo survives branch edit");
        var restarted = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var restartedPage = new MemoSession(restarted, folder, origin, _ =>
        {
        });
        Check(restartedPage.Open(Entry(first), Now()) && !restarted.CanUndo && !restarted.CanRedo, "restart loads ink without history");
        Console.WriteLine("PASS: per-page session undo/redo, erasure, resizing, load failure/retry and restart boundary.");
    }
}
