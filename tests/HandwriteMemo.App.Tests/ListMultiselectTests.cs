using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class ListMultiselectTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-026", "batch-" + Guid.NewGuid().ToString("N")));
        string folder = Path.Combine(root, "pages");
        var origin = DateTimeOffset.UtcNow;
        var store = new MemoStore(folder);
        var documents = Enumerable.Range(0, 5).Select(i => new MemoDocument(MemoDocument.CurrentFormatVersion,
            Guid.NewGuid(), origin.AddMinutes(-i - 1), origin, BoardSize.Default,
            [new MemoStroke(origin, [new MemoPoint(.05f, .1f, 0), new MemoPoint(.2f, .15f, 0)], "#000000", .0015f)],
            isPinned: false, title: "合成ページ " + i)).ToArray();
        foreach (var document in documents)
            store.Save(document, stream => MemoRenderer.WritePng(document, stream));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        double time = 0;
        double Now() => time += .05;
        var page = new MemoSession(engine, folder, origin, _ =>
        {
        });
        PageCatalogEntry Entry(int i) => page.Catalog.List().Single(e => e.Document.Id == documents[i].Id);
        Check(page.Open(Entry(1), Now()), "Open initial shared page");
        using var window = new DesktopWindow(engine, page, Now);
        window.Show();
        Application.DoEvents();
        var list = window.PagesList;
        var remove = window.RemoveButton;
        void UndoMove()
        {
            list.Focus();
            window.DispatchCommandKey(Keys.Control | Keys.Z);
        }
        var pin = window.PinButton;
        var status = window.StatusLabel;
        void Select(params int[] indices)
        {
            window.SelectPages(indices.Select(index => documents[index].Id));
        }

        using (var locked = new FileStream(Entry(2).JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            window.NextButton.PerformClick();
            Check(page.Id == documents[1].Id && page.LoadError is not null &&
                ((PageCatalogEntry?)list.SelectedItem)?.Document.Id == page.Id, "Failed next-page read retains editor and exclusive selection");
        }
        window.RetryOpenButton.PerformClick();
        Check(page.Id == documents[2].Id && page.LoadError is null, "Navigation read retry opens the failed target");
        Check(page.Open(Entry(1), Now()), "Return after navigation failure");
        window.RefreshCatalog();

        Select(1, 2);
        Check(list.SelectionMode == SelectionMode.MultiExtended && window.Canvas.ReadOnly && window.SelectionCountLabel.Text == "2件選択" && remove.Text == "ゴミ箱へ",
            "Multi-selection displays count and protects canvas");
        window.RefreshCatalog();
        Check(list.SelectedItems.Count == 2, "Catalog reload preserves batch selection");
        var ids = window.Entries.Where(e => list.SelectedItems.Contains(e)).Select(e => e.Document.Id).ToArray();
        pin.PerformClick();
        Check(page.IsPinned && ids.All(id => page.Catalog.List().Single(e => e.Document.Id == id).Document.IsPinned), "Batch pin includes shared page");
        Check(list.SelectedItems.Count == 2 && pin.Text.Contains("ピン解除"), "Pinned sort preserves selection and predicts unpin");
        pin.PerformClick();
        Check(!page.IsPinned && ids.All(id => !page.Catalog.List().Single(e => e.Document.Id == id).Document.IsPinned), "Batch unpin");
        page.Catalog.SetPinned(Entry(2), true, MemoRenderer.WritePng);
        window.RefreshCatalog();
        Check(pin.Text.Contains("ピン留め"), "Mixed pin selection predicts pinning all");
        pin.PerformClick();
        Check(ids.All(id => page.Catalog.List().Single(e => e.Document.Id == id).Document.IsPinned), "Mixed batch pins all");
        pin.PerformClick();

        // A VR stroke and a resize must block the entire move, without ending their input.
        Check(page.BeginDesktop(Now()), "Start existing input owner");
        remove.PerformClick();
        Check(page.Catalog.List(true).Count == 0 && page.Id == documents[1].Id && page.DesktopOwnsInput, "Busy input blocks every move and keeps shared page");
        page.DesktopPoint(null, false, Now(), DrawingTool.Pen);
        Check(page.SetVrResizing(true, Now()), "Start shared resize");
        remove.PerformClick();
        Check(page.Catalog.List(true).Count == 0 && page.IsResizing, "Resize blocks every move");
        page.SetVrResizing(false, Now());

        remove.PerformClick();
        Check(page.Catalog.List(true).Count == 2 && page.Id == documents[3].Id, "Active batch opens the next surviving neighbour");
        Check(list.SelectedItems.Count == 1 && ((PageCatalogEntry)list.SelectedItem!).Document.Id == page.Id && !window.Canvas.ReadOnly,
            "Editor/list follow shared neighbour");
        Check(page.Catalog.List(true).Count == 2 && window.CanUndoTrash, "Moved state and undo survive catalog refresh");
        window.RefreshCatalog();
        Check(page.Catalog.List(true).Count == 2 && window.CanUndoTrash, "Moved state and undo remain available after reload");
        UndoMove();
        Check(page.Catalog.List().Count == 5 && page.Catalog.List(true).Count == 0 && !window.CanUndoTrash, "One command restores exact moved batch");

        // Move targets exclude the open page: its identity, history and ink must stay unchanged.
        Guid retainedId = page.Id;
        var snapshot = page.Snapshot();
        Select(0, 4);
        remove.PerformClick();
        Check(page.Id == retainedId && page.Snapshot().Strokes.SelectMany(s => s.Points).SequenceEqual(snapshot.Strokes.SelectMany(s => s.Points)), "Unrelated move leaves shared page untouched");
        UndoMove();

        Check(page.Open(Entry(4), Now()), "Open last neighbour");
        window.RefreshCatalog();
        Select(4);
        remove.PerformClick();
        Check(page.Id == documents[3].Id, "Last page falls back to previous surviving neighbour");
        UndoMove();

        Select(0, 4);
        remove.PerformClick();
        var lockedTrash = page.Catalog.List(true).Single(e => e.Document.Id == documents[0].Id);
        using (var locked = new FileStream(lockedTrash.JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            UndoMove();
            Check(page.Catalog.List(true).Count == 1 && window.CanUndoTrash &&
                ContextUndoTests.MenuCommand(window, "移動を元に戻す").Text?.Contains("2件") == false &&
                status.Text.Contains("1件を復元できません"), "Partial undo keeps failed target and offers retry");
        }
        UndoMove();
        Check(page.Catalog.List(true).Count == 0 && !window.CanUndoTrash, "Retry undo restores only the remaining target");

        // A denied file in the middle must not hide successful moves or lose the undo action.
        Select(0, 2, 4);
        using (var locked = new FileStream(Entry(2).JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            remove.PerformClick();
            Check(page.Catalog.List(true).Count == 2 && File.Exists(Entry(2).JsonPath), "Partial move keeps failed target in normal list");
            Check(page.Catalog.List(true).Count == 2 && status.Text.Contains("1件をゴミ箱へ移せません") && window.CanUndoTrash,
                "Partial move reports success and failure with undo");
        }
        UndoMove();
        Check(page.Catalog.List(true).Count == 0 && page.Catalog.List().Count == 5, "Undo only restores successful targets");

        // Saving fails before moving the open page: retain dirty ink and all targets.
        Check(page.Open(Entry(1), Now()), "Return to source for save failure");
        window.RefreshCatalog();
        Select(1, 2);
        using (var locked = new FileStream(Entry(1).JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            page.SetPinned(true, Now());
            Check(page.Error is not null, "Inject failed save");
            remove.PerformClick();
            Check(page.Id == documents[1].Id && page.Catalog.List(true).Count == 0 && page.Error is not null, "Failed save blocks batch before moving anything");
        }
        Check(page.Save(Now()), "Save retry recovers");
        window.RefreshCatalog();

        // With all pages selected the shared session becomes a new, unsaved blank page.
        list.Focus();
        Key(window, Keys.Control | Keys.A);
        Check(list.SelectedItems.Count == 5 && window.Canvas.ReadOnly, "Ctrl+A selects normal list");
        Key(window, Keys.F2);
        Check(!window.NameEditor.Visible, "F2 cannot rename a batch");
        Key(window, Keys.Delete);
        Check(page.Catalog.List().Count == 0 && page.Catalog.List(true).Count == 5 && engine.Strokes.Count == 0 &&
            documents.All(d => d.Id != page.Id) && page.VrNeedsTriggerRelease, "Delete all selects shared blank and requires VR trigger release");
        UndoMove();
        Check(page.Catalog.List().Count == 5 && page.Catalog.List(true).Count == 0, "Undo restores all pages without saving blank");
        window.Hide();
        Console.WriteLine("PASS: batch selection, pin, move/undo, neighbour/blank shared session, busy/resize/save protection and partial move failure: " + root);
    }

    private static void Key(DesktopWindow window, Keys keys)
    {
        window.DispatchCommandKey(keys);
    }
}
