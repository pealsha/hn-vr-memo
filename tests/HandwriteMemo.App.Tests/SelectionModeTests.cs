using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class SelectionModeTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-029"));
        string folder = Path.Combine(root, "synthetic-" + Guid.NewGuid().ToString("N"));
        var origin = DateTimeOffset.UtcNow;
        var store = new MemoStore(folder);
        var documents = Enumerable.Range(0, 7).Select(i => new MemoDocument(MemoDocument.CurrentFormatVersion,
            Guid.NewGuid(), origin.AddMinutes(-i - 1), origin, BoardSize.Default,
            [new MemoStroke(origin.AddSeconds(-2), [new MemoPoint(.05f, .1f, 0), new MemoPoint(.2f, .15f + i * .02f, 1)], "#182634", .0015f)],
            isPinned: i == 0, title: "選択モードの確認 " + (i + 1))).ToArray();
        foreach (var document in documents)
            store.Save(document, stream => MemoRenderer.WritePng(document, stream));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        double time = 0;
        double Now() => time += .05;
        var page = new MemoSession(engine, folder, origin, _ =>
        {
        });
        PageCatalogEntry Entry(int i) => page.Catalog.List().Single(e => e.Document.Id == documents[i].Id);
        Check(page.Open(Entry(0), Now()), "Open initial shared page");
        foreach (int i in new[] { 4, 5, 6 })
            page.Catalog.MoveToTrash(Entry(i));
        using var window = new DesktopWindow(engine, page, Now);
        window.Show();
        Application.DoEvents();
        var list = window.PagesList;
        Guid current = page.Id;

        foreach (bool trash in new[] { false, true })
        {
            if (trash)
                window.DispatchCommandKey(Keys.Control | Keys.Tab);
            string tab = trash ? "trash" : "list";
            window.Size = new(1180, 840);
            Application.DoEvents();
            Check(!window.IsSelectionMode && window.SelectButton.Text == "選択", "Normal mode on " + tab);
            Capture(window, root, tab + "-01-normal");
            Button batchAction = trash ? window.RestoreButton : window.PinButton;
            int normalListTop = list.Top;
            var normalSelectBounds = window.SelectButton.Bounds;
            var normalActionPosition = window.PointToClient(batchAction.PointToScreen(Point.Empty));
            window.SelectButton.PerformClick();
            Check(list.Top == normalListTop && window.SelectButton.Bounds == normalSelectBounds &&
                window.PointToClient(batchAction.PointToScreen(Point.Empty)) == normalActionPosition,
                "Entering selection keeps the rows, Select/Done and the bottom actions in place on " + tab);
            Check(window.IsSelectionMode && list.SelectedItems.Count == 0 && window.Canvas.ReadOnly &&
                window.SelectionCountLabel.Text == "0件選択", "Enter starts empty and read-only on " + tab);
            Check(!(trash ? window.RestoreButton.Enabled || window.DeleteButton.Enabled : window.PinButton.Enabled || window.RemoveButton.Enabled),
                "Zero selections disable batch actions on " + tab);
            Capture(window, root, tab + "-02-select-zero");
            ClickRow(list, 1, .5f, 30);
            Check(list.SelectedItems.Count == 1 && page.Id == current && window.Canvas.ReadOnly, "A thumbnail click checks without opening on " + tab);
            ClickRow(list, 1, .9f, 148);
            Check(list.SelectedItems.Count == 0, "A title click toggles the same row off on " + tab);
            ClickRow(list, 0, .03f, 80);
            ClickRow(list, 1, .9f, 148);
            Check(list.SelectedItems.Count == 2 && window.SelectionCountLabel.Text == "2件選択" && page.Id == current,
                "Unmodified clicks add selections on " + tab);
            window.RefreshCatalog();
            Check(list.SelectedItems.Count == 2, "Reload retains checks on " + tab);
            Capture(window, root, tab + "-03-select-multiple");
            int topBeforeAll = list.TopIndex;
            window.SelectAllButton.PerformClick();
            Check(list.SelectedItems.Count == list.Items.Count && window.SelectAllButton.Text == "すべて解除" && page.Id == current,
                "Select all uses visible tab only on " + tab);
            Check(list.TopIndex == topBeforeAll, "Select all retains scroll position on " + tab);
            Capture(window, root, tab + "-04-select-all");
            window.Size = new(800, 620);
            Application.DoEvents();
            Capture(window, root, tab + "-05-minimum-all");
            CheckSelectionLayout(window);
            window.SelectAllButton.PerformClick();
            Check(list.SelectedItems.Count == 0 && window.SelectAllButton.Text == "すべて選択", "Clear all keeps mode on " + tab);
            ClickRow(list, 0);
            ClickRow(list, 1);
            window.Size = new(1920, 1040);
            Application.DoEvents();
            Capture(window, root, tab + "-06-wide-multiple");
            CheckSelectionLayout(window);
            int wideSelectingTop = list.Top;
            window.SelectAllButton.Focus();
            window.DispatchCommandKey(Keys.Escape);
            Check(list.Top == wideSelectingTop, "Leaving selection with large buttons keeps the rows in place on " + tab);
            Check(!window.IsSelectionMode && list.SelectionMode == SelectionMode.MultiExtended && !window.DeleteButton.Visible,
                "Esc returns to normal from a button on " + tab);
            Check(trash ? list.SelectedItems.Count == 0 : ((PageCatalogEntry?)list.SelectedItem)?.Document.Id == current,
                "Exit clears batch and restores shared page selection on " + tab);
            window.SelectButton.PerformClick();
            ClickRow(list, 0);
            window.SelectButton.PerformClick();
            Check(!window.IsSelectionMode, "Done exits on " + tab);
        }

        // Switching tabs always ends the mode and never carries checks between different collections.
        window.SelectButton.PerformClick();
        ClickRow(list, 0);
        window.DispatchCommandKey(Keys.Control | Keys.Tab);
        Check(!window.IsSelectionMode && list.Items.Count == 4 && page.Id == current, "Tab change ends mode without opening a page");
        window.Size = new(1180, 840);
        Application.DoEvents();
        window.SelectButton.PerformClick();
        window.SelectPages([documents[1].Id, documents[2].Id]);
        window.PinButton.PerformClick();
        Check(window.IsSelectionMode && list.SelectedItems.Count == 2 && window.PinButton.Text.Contains("ピン解除") &&
            new[] { 1, 2 }.All(i => Entry(i).Document.IsPinned) && page.Id == current, "Pin uses checked targets and preserves checks after sort");
        window.PinButton.PerformClick();
        Check(new[] { 1, 2 }.All(i => !Entry(i).Document.IsPinned), "Unpin uses the same checked targets");

        using (var locked = new FileStream(Entry(2).JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            window.RemoveButton.PerformClick();
            Check(window.IsSelectionMode && list.SelectedItems.Count == 1 &&
                ((PageCatalogEntry)list.SelectedItem!).Document.Id == documents[2].Id && window.StatusLabel.Text.Contains("1件をゴミ箱へ移せません"),
                "Partial move keeps only the failed target checked");
        }
        window.RemoveButton.PerformClick();
        Check(window.IsSelectionMode && list.SelectedItems.Count == 0 && window.CanUndoTrash &&
            page.Catalog.List(true).Any(e => e.Document.Id == documents[2].Id) && page.Id == current,
            "Move retry stays in empty mode, with undo available");
        Capture(window, root, "action-01-moved-with-undo");
        ContextUndoTests.MenuCommand(window, "移動を元に戻す").PerformClick();
        Check(window.IsSelectionMode && list.SelectedItems.Count == 0 && page.Catalog.List().Count == 3,
            "Undo restores the latest successful move without checking restored pages");
        // Restore the earlier successful target as well; undo still means the latest move only.
        var earlier = page.Catalog.List(true).Single(e => e.Document.Id == documents[1].Id);
        page.Catalog.Restore(earlier);
        window.RefreshCatalog();

        // Move every normal page including the shared current one. The neighbouring/blank logic is reused.
        window.SelectAllButton.PerformClick();
        window.RemoveButton.PerformClick();
        Check(window.IsSelectionMode && list.Items.Count == 0 && list.SelectedItems.Count == 0 &&
            !window.SelectAllButton.Enabled && !window.RemoveButton.Enabled && !window.PinButton.Enabled &&
            documents.All(d => d.Id != page.Id) && engine.Strokes.Count == 0 && page.VrNeedsTriggerRelease,
            "Moving all enters the shared blank and retains zero-selection mode");
        window.RefreshCatalog();
        Check(list.SelectedItems.Count == 0, "Refresh of empty mode does not select the shared blank");
        ContextUndoTests.MenuCommand(window, "移動を元に戻す").PerformClick();
        Check(list.Items.Count == 4 && list.SelectedItems.Count == 0 && window.IsSelectionMode, "Undo all retains unchecked normal list");

        window.DispatchCommandKey(Keys.Control | Keys.Tab);
        window.SelectButton.PerformClick();
        window.SelectAllButton.PerformClick();
        var firstTrash = page.Catalog.List(true)[0];
        using (var locked = new FileStream(firstTrash.JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            window.RestoreButton.PerformClick();
            Check(window.IsSelectionMode && list.SelectedItems.Count == 1 &&
                ((PageCatalogEntry)list.SelectedItem!).Document.Id == firstTrash.Document.Id &&
                window.StatusLabel.Text.Contains("1件を復元できません"), "Partial restore retains only failed check");
        }
        window.RestoreButton.PerformClick();
        Check(window.IsSelectionMode && list.Items.Count == 0 && !window.RestoreButton.Enabled && !window.DeleteButton.Enabled &&
            page.Catalog.List().Any(e => e.Document.Id == firstTrash.Document.Id), "Restore retry stays in trash with restored page and disabled actions");
        Capture(window, root, "action-02-restored-empty-trash");

        // Repopulate trash using only synthetic pages, then exercise the actual modal confirmation.
        foreach (int i in new[] { 4, 5, 6 })
            page.Catalog.MoveToTrash(Entry(i));
        window.RefreshCatalog();
        window.SelectAllButton.PerformClick();
        ConfirmDeletion(window, root, cancel: true);
        Check(window.IsSelectionMode && list.SelectedItems.Count == 3 && page.Catalog.List(true).Count == 3,
            "Cancelling permanent deletion preserves all checks");
        var deleteTarget = page.Catalog.List(true)[1];
        using (var locked = new FileStream(deleteTarget.JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ConfirmDeletion(window, root, cancel: false);
            Check(window.IsSelectionMode && list.SelectedItems.Count == 1 &&
                ((PageCatalogEntry)list.SelectedItem!).Document.Id == deleteTarget.Document.Id &&
                window.StatusLabel.Text.Contains("1件を完全に削除できません"), "Partial deletion retains failed check with error");
        }
        ConfirmDeletion(window, root, cancel: false);
        Check(window.IsSelectionMode && list.Items.Count == 0 && !window.DeleteButton.Enabled &&
            window.StatusLabel.Text.Contains("1件を完全に削除しました"), "Permanent delete retry leaves result and zero checks");
        Capture(window, root, "action-03-deleted-empty-trash");

        // External shared-page changes in the normal list retain explicit checks; they cannot become an action target.
        window.DispatchCommandKey(Keys.Control | Keys.Tab);
        window.SelectButton.PerformClick();
        window.SelectPages([documents[1].Id, documents[2].Id]);
        Check(page.Open(Entry(3), Now()), "Simulate a VR-side shared page change");
        window.RefreshCatalog();
        Application.DoEvents();
        Check(window.IsSelectionMode && list.SelectedItems.Count == 2 &&
            list.SelectedItems.Cast<PageCatalogEntry>().All(e => e.Document.Id != page.Id), "Shared-page change preserves checked action targets");
        window.SelectPages([documents[1].Id]);
        list.Focus();
        window.DispatchCommandKey(Keys.F2);
        Check(!window.NameEditor.Visible && page.Id == documents[3].Id, "F2 cannot rename a checked page in selection mode");
        window.DispatchCommandKey(Keys.Control | Keys.A);
        Check(list.SelectedItems.Count == list.Items.Count && page.Id == documents[3].Id, "Ctrl+A also works in selection mode without navigation");
        window.SelectButton.PerformClick();
        int openIndex = window.Entries.ToList().FindIndex(e => e.Document.Id == documents[1].Id);
        ClickRow(list, openIndex);
        Check(page.Id == documents[1].Id && !window.Canvas.ReadOnly, "Normal click still opens a single page after exit");
        window.Hide();
        Console.WriteLine("PASS: Task 029 selection mode, native row toggle, counts/all/clear, exit/tab, batch pin/move/undo/restore/delete, partial failures and layouts: " + root);
    }

    private static void CheckSelectionLayout(DesktopWindow window)
    {
        Control[] actions = [window.SelectButton, window.SelectAllButton, window.SelectionCountLabel,
            window.PinButton, window.RemoveButton, window.RestoreButton, window.DeleteButton];
        foreach (Control control in actions.Where(c => c.Visible))
        {
            Check(control.Parent!.ClientRectangle.Contains(control.Bounds), "Selection action fits parent: " + control.Text);
            int width = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine).Width;
            Check(width <= control.ClientSize.Width, "Selection text fits without wrapping or clipping: " + control.Text);
            foreach (Control other in actions.Where(c => c.Visible && c != control && c.Parent == control.Parent))
                Check(!control.Bounds.IntersectsWith(other.Bounds), "Selection controls never overlap");
        }
    }

    private static void ConfirmDeletion(DesktopWindow window, string root, bool cancel)
    {
        bool seen = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            if (Application.OpenForms.OfType<TrashDeleteDialog>().FirstOrDefault() is not { } dialog)
                return;
            timer.Stop();
            seen = true;
            Check(dialog.AcceptButton == dialog.CancelButton && ((Button)dialog.CancelButton!).DialogResult == DialogResult.Cancel,
                "Permanent delete confirmation defaults to Cancel");
            Capture(dialog, root, cancel ? "dialog-cancel-default" : "dialog-confirm-delete");
            if (cancel)
                ((Button)dialog.CancelButton!).PerformClick();
            else
                Descendants(dialog).OfType<Button>().Single(b => b.DialogResult == DialogResult.OK).PerformClick();
        };
        timer.Start();
        window.DeleteButton.PerformClick();
        Check(seen, "Visible delete button opens the existing confirmation");
    }

    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));

    private static void Capture(Control control, string root, string name)
    {
        Directory.CreateDirectory(root);
        control.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
    }

    internal static void ClickRow(ListBox list, int index, float across = .5f, int down = 30)
    {
        list.TopIndex = Math.Min(index, list.TopIndex);
        var row = list.GetItemRectangle(index);
        int x = row.Left + (int)(row.Width * across), y = row.Top + down;
        var position = new IntPtr((ushort)x | (long)(ushort)y << 16);
        // Messages go only to this synthetic host's ListBox, never to the real input queue.
        _ = SendMessage(list.Handle, 0x0201, new IntPtr(1), position);
        _ = SendMessage(list.Handle, 0x0202, IntPtr.Zero, position);
        Application.DoEvents();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
