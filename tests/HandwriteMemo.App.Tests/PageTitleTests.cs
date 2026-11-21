using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class PageTitleTests
{
    [TestMethod]
    public void Run()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "title-session-" + Guid.NewGuid().ToString("N")));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var origin = DateTimeOffset.UtcNow;
        var page = new MemoSession(engine, folder, origin, _ =>
        {
        });
        double t = 0;
        double Now() => t += .01;
        PageCatalogEntry Entry(Guid id) => page.Catalog.List().Single(e => e.Document.Id == id);
        void Draw()
        {
            Check(page.BeginDesktop(Now()), "begin ink");
            page.DesktopPoint(new(.2f, .4f), true, Now(), DrawingTool.Pen);
            page.DesktopPoint(new(.8f, .6f), true, Now(), DrawingTool.Pen);
            page.DesktopPoint(null, false, Now(), DrawingTool.Pen);
        }
        Check(page.Title == "", "new page title");
        Draw();
        var first = page.Id;
        using var browser = new VrPageBrowser(page);
        var entry = Entry(first);
        Check(page.Rename(entry, "謎解き & メモ", Now()), "rename current page");
        Check(page.Title == "謎解き & メモ" && Entry(first).JsonPath == entry.JsonPath, "title or basename");
        browser.RefreshIfNeeded();
        Check(browser.Entries.Single().Document.Title == page.Title, "VR list title sync");
        page.Undo(Now());
        Check(engine.Strokes.Count == 0 && page.Title == "謎解き & メモ", "ink undo affected title");
        page.Redo(Now());
        Check(engine.Strokes.Count == 1, "ink redo after rename");
        Check(page.SetPinned(true, Now()) && Entry(first).Document.Title == page.Title, "active pin lost title");
        Check(page.NewPage(Now()) && page.Title == "", "new page inherited title");
        Draw();
        var second = page.Id;
        Check(page.Rename(Entry(first), "別ページの名前", Now()) && page.Id == second && page.Title == "", "inactive rename changed active page");
        Check(page.UndoRename(Entry(first), Now()) && Entry(first).Document.Title == "謎解き & メモ", "inactive rename undo");
        Check(page.Duplicate(Entry(first), Now()) && page.Title == "謎解き & メモ" && !page.IsPinned, "duplicate title and pin");
        Check(page.Open(Entry(first), Now()) && engine.CanUndo, "title edit lost page ink history");
        Check(page.Rename(Entry(first), "", Now()) && page.Title == "", "clear title");
        Check(page.UndoRename(Entry(first), Now()) && page.Title == "謎解き & メモ", "undo clear");
        Check(page.BeginDesktop(Now()), "busy input");
        Check(!page.Rename(Entry(first), "割り込み", Now()), "renamed during ink input");
        page.DesktopPoint(null, false, Now(), DrawingTool.Pen);
        var beforeFailure = Entry(first);
        using (var locked = new FileStream(entry.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!page.Rename(beforeFailure, "保存の再試行", Now()) && page.Title == "保存の再試行" && page.Error is not null,
                "save failure did not retain new title");
        Check(page.Save(Now()) && Entry(first).Document.Title == "保存の再試行", "title save retry");
        Check(page.UndoRename(Entry(first), Now()) && page.Title == "謎解き & メモ", "save failure lost rename undo");
        var inactive = Entry(second);
        using (var locked = new FileStream(Path.ChangeExtension(inactive.JsonPath, ".png"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                page.Rename(inactive, "PNG失敗後も戻せる", Now());
                throw new InvalidOperationException("PNG lock ignored");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        Check(page.Id == first && Entry(second).Document.Title == "PNG失敗後も戻せる" && page.CanUndoRename(inactive), "inactive committed JSON lost undo");
        Check(page.UndoRename(Entry(second), Now()) && Entry(second).Document.Title == "", "inactive PNG failure undo");
        var restarted = new MemoSession(new(new(Vector3.Zero, Quaternion.Identity)), folder, origin, _ =>
        {
        });
        Check(restarted.Open(Entry(first), Now()) && restarted.Title == "謎解き & メモ" && !restarted.CanUndoRename(Entry(first)), "restart title persistence/history boundary");
        page.Catalog.MoveToTrash(Entry(second));
        var trash = page.Catalog.List(true).Single(e => e.Document.Id == second);
        Check(!page.Rename(trash, "ごみ箱の変更", Now()), "trash rename accepted");
        // A legacy basename first encountered after session startup must also stay unchanged.
        var imported = new MemoDocument(2, Guid.NewGuid(), origin, origin, BoardSize.Default, []);
        var importStore = new MemoStore(folder);
        var importedFiles = importStore.Save(imported, s => MemoRenderer.WritePng(imported, s));
        string legacyPath = Path.Combine(folder, "legacy-custom-name.json");
        File.Move(importedFiles.JsonPath, legacyPath);
        File.Move(importedFiles.PngPath, Path.ChangeExtension(legacyPath, ".png"));
        Check(page.Rename(Entry(imported.Id), "古いファイル名を保つ", Now()) && Entry(imported.Id).JsonPath == legacyPath, "inactive legacy filename changed");
        page.Catalog.SetPinned(Entry(imported.Id), true, MemoRenderer.WritePng);
        Check(Entry(imported.Id).JsonPath == legacyPath && Entry(imported.Id).Document.Title == "古いファイル名を保つ", "pin lost legacy filename or title");
        Console.WriteLine("PASS: page titles, PC/VR sync, duplicate, separate title/ink histories, failure/retry and restart.");
    }
}
