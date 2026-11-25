using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class ContextUndoTests
{
    // Reuse the real keyboard menu in older tests instead of retaining invisible recovery buttons.
    internal static ToolStripMenuItem MenuCommand(DesktopWindow window, string prefix)
    {
        window.PagesList.Focus();
        window.DispatchCommandKey(Keys.Apps);
        var menu = window.PagesList.ContextMenuStrip!;
        Check(menu.Visible, $"Keyboard context menu is available, including an empty list (focus={window.PagesList.ContainsFocus}, items={menu.Items.Count}, editor={window.NameEditor.Visible})");
        var item = Command(menu, prefix);
        menu.Close();
        return item;
    }

    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-030"));
        string folder = Path.Combine(root, "synthetic-" + Guid.NewGuid().ToString("N"));
        var origin = DateTimeOffset.UtcNow;
        var store = new MemoStore(folder);
        var documents = Enumerable.Range(0, 5).Select(i => new MemoDocument(MemoDocument.CurrentFormatVersion,
            Guid.NewGuid(), origin.AddMinutes(-i - 1), origin, BoardSize.Default,
            [new MemoStroke(origin.AddSeconds(-2), [new MemoPoint(.05f, .1f, 0), new MemoPoint(.2f, .15f, 1)], "#182634", .0015f)],
            isPinned: false, title: "メモ " + (i + 1))).ToArray();
        foreach (var document in documents)
            store.Save(document, stream => MemoRenderer.WritePng(document, stream));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        double time = 0;
        double Now() => time += .05;
        var page = new MemoSession(engine, folder, origin, _ =>
        {
        });
        PageCatalogEntry Entry(int i) => page.Catalog.List().Single(e => e.Document.Id == documents[i].Id);
        Check(page.Open(Entry(0), Now()), "Open initial page");
        foreach (int i in new[] { 3, 4 })
            page.Catalog.MoveToTrash(Entry(i));
        using var window = new DesktopWindow(engine, page, Now);
        window.Show();
        Application.DoEvents();
        var list = window.PagesList;
        Capture(window, root, "list-normal");
        Check(!MenuCommand(window, "移動を元に戻す").Enabled, "Undo without a move is disabled on a page");
        var menu = BackgroundMenu(window);
        Check(!Command(menu, "移動を元に戻す").Enabled && Command(menu, "新しいページ").Enabled &&
            Command(menu, "すべて選択").Enabled && Command(menu, "更新").Enabled, "Normal background menu has applicable commands");
        Check(!menu.Items.Cast<ToolStripItem>().Any(i => i.Text is "開く" or "ゴミ箱へ移動"), "Background never targets a selected page");
        Capture(menu, root, "menu-list-background-no-undo");
        menu.Close();
        Check(!Descendants(window).OfType<Button>().Any(b => b.Text.StartsWith("移動を元に戻す") || b.Text == "復元したページを開く"),
            "No transient buttons remain in the form");

        // Create real ink history on the shared page, independently of the movable targets.
        var paper = window.Canvas.PaperBounds;
        Point At(float x, float y) => new((int)(paper.Left + x * paper.Width), (int)(paper.Top + y * paper.Height));
        window.Canvas.BeginInput(At(.3f, .4f));
        window.Canvas.MoveInput(At(.5f, .4f));
        window.Canvas.EndInput();
        int ink = engine.Strokes.Count;
        Check(engine.CanUndo && ink == 2, "Create undoable drawing");
        void Move(params int[] indices)
        {
            window.SelectPages(indices.Select(i => documents[i].Id));
            window.RemoveButton.PerformClick();
        }

        Move(1, 2);
        Check(page.Catalog.List(true).Count == 4 && window.CanUndoTrash && page.Id == documents[0].Id, "Move has the same exact batch and shared page");
        Check(!window.StatusLabel.Text.Contains("移しました"), "Move result is shown by the list rather than a success notice");
        Capture(window, root, "list-after-move");
        menu = RowMenu(window, 0);
        Check(Command(menu, "移動を元に戻す").Text?.Contains("2件") == true && Command(menu, "移動を元に戻す").ShortcutKeyDisplayString == "Ctrl+Z",
            "Row menu exposes exact move count and shortcut");
        Capture(menu, root, "menu-list-page-after-move");
        Command(menu, "移動を元に戻す").PerformClick();
        menu.Close();
        Check(page.Catalog.List().Count == 3 && page.Catalog.List(true).Count == 2 && engine.Strokes.Count == ink, "Row menu restores only the latest batch, leaving ink");

        Move(1, 2);
        menu = BackgroundMenu(window);
        Check(Command(menu, "移動を元に戻す").Enabled, "Background exposes the same available undo");
        Capture(menu, root, "menu-list-background-after-move");
        Command(menu, "移動を元に戻す").PerformClick();
        menu.Close();
        Check(page.Catalog.List().Count == 3 && page.Catalog.List(true).Count == 2, "Background menu restores the exact batch");

        Move(1);
        Check(page.SetVrResizing(true, Now()), "Start shared VR resizing");
        Check(!MenuCommand(window, "移動を元に戻す").Enabled, "Busy input disables move undo in the menu");
        list.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(page.Catalog.List(true).Count == 3 && engine.Strokes.Count == ink && page.IsResizing, "Busy list undo leaves files, ink and input untouched");
        page.SetVrResizing(false, Now());
        window.Canvas.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(engine.Strokes.Count == ink - 1 && window.CanUndoTrash && page.Catalog.List(true).Count == 3, "Canvas Ctrl+Z undoes ink and retains trash move");
        window.DispatchCommandKey(Keys.Control | Keys.Y);
        Check(engine.Strokes.Count == ink, "Canvas Ctrl+Y redoes the same ink");
        list.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(!window.CanUndoTrash && page.Catalog.List(true).Count == 2 && engine.Strokes.Count == ink, "List Ctrl+Z restores move without undoing ink");
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(engine.Strokes.Count == ink, "List Ctrl+Z with no move never falls through to ink undo");

        Move(1);
        window.RemoveButton.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(!window.CanUndoTrash && engine.Strokes.Count == ink, "Catalog action button Ctrl+Z restores files, not ink");
        Move(1);
        window.SelectPages([documents[0].Id]);
        list.Focus();
        window.DispatchCommandKey(Keys.F2);
        Check(window.NameEditor.Visible && window.NameEditor.ContainsFocus, "Rename editor owns focus");
        window.NameEditor.SelectionStart = window.NameEditor.TextLength;
        window.NameEditor.SelectionLength = 0;
        _ = SendMessage(window.NameEditor.Handle, 0x0102, new IntPtr('X'), new IntPtr(1)); // WM_CHAR, only this editor.
        Check(window.NameEditor.CanUndo && window.NameEditor.Text == documents[0].Title + "X", "Native editor records synthetic typing");
        // The form must leave text shortcuts unhandled for the native editor to process.
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(window.CanUndoTrash && page.Catalog.List(true).Count == 3 && engine.Strokes.Count == ink, "Text editor Ctrl+Z never restores files or ink");
        window.NameEditor.Undo();
        Check(window.NameEditor.Text == documents[0].Title, "Native text undo still restores text");
        window.DispatchCommandKey(Keys.Escape);
        Check(!window.NameEditor.Visible, "Esc exits rename");
        list.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);

        // Background Select All must preserve navigation and select all, even if all were already selected.
        Guid current = page.Id;
        menu = BackgroundMenu(window);
        Command(menu, "すべて選択").PerformClick();
        menu.Close();
        Check(list.SelectedItems.Count == 3 && page.Id == current, "Background Select All does not navigate");
        menu = BackgroundMenu(window);
        Command(menu, "すべて選択").PerformClick();
        menu.Close();
        Check(list.SelectedItems.Count == 3, "Select All never toggles off");
        window.RemoveButton.PerformClick();
        Check(list.Items.Count == 0 && window.CanUndoTrash, "All pages can be moved, leaving an empty list");
        Capture(window, root, "list-empty-after-move");
        menu = BackgroundMenu(window);
        Capture(menu, root, "menu-empty-list-after-move");
        menu.Close();
        list.Focus();
        window.DispatchCommandKey(Keys.Shift | Keys.F10);
        Check(list.ContextMenuStrip!.Visible, "Shift+F10 opens the menu on an empty list");
        list.ContextMenuStrip.Close();
        list.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(list.Items.Count == 3 && engine.Strokes.Count == 0, "Empty list Ctrl+Z restores files without altering the new blank");

        window.DispatchCommandKey(Keys.Control | Keys.Tab);
        Capture(window, root, "trash-normal");
        menu = RowMenu(window, 0);
        Capture(menu, root, "menu-trash-page");
        menu.Close();
        menu = BackgroundMenu(window);
        Check(Command(menu, "ゴミ箱を空にする").Enabled && !menu.Items.Cast<ToolStripItem>().Any(i => i.Text == "新しいページ"), "Trash background offers Empty Trash rather than a new page");
        Capture(menu, root, "menu-trash-background");
        menu.Close();
        // Latest move can also be undone from the trash tab.
        window.DispatchCommandKey(Keys.Control | Keys.Tab);
        Move(1);
        window.DispatchCommandKey(Keys.Control | Keys.Tab);
        Capture(window, root, "trash-after-move");
        menu = RowMenu(window, 0);
        Capture(menu, root, "menu-trash-page-after-move");
        menu.Close();
        menu = BackgroundMenu(window);
        Capture(menu, root, "menu-trash-background-after-move");
        menu.Close();
        list.Focus();
        window.DispatchCommandKey(Keys.Control | Keys.Z);
        Check(page.Catalog.List(true).Count == 2 && !window.CanUndoTrash, "Trash list Ctrl+Z uses the same undo");
        list.SelectedIndex = 0;
        Guid restoredId = ((PageCatalogEntry)list.SelectedItem!).Document.Id;
        using (var locked = new FileStream(((PageCatalogEntry)list.SelectedItem!).JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            window.RestoreButton.PerformClick();
            Check(window.StatusLabel.Text.Contains("1件を復元できません"), "Restore failure identifies the remaining target");
            menu = BackgroundMenu(window);
            Check(window.StatusLabel.Text.Contains("1件を復元できません") && VisibleButton(window, "詳細"), "Opening a trash background menu never clears an operation error");
            Capture(menu, root, "menu-trash-background-restore-error");
            menu.Close();
            Capture(window, root, "error-restore");
        }
        window.RestoreButton.PerformClick();
        Check(page.Catalog.List().Any(e => e.Document.Id == restoredId) && !window.StatusLabel.Text.Contains("復元しました"), "Restore is visible through catalog state without redundant notice");
        Capture(window, root, "trash-after-restore");
        menu = BackgroundMenu(window);
        Capture(menu, root, "menu-trash-background-after-restore");
        Command(menu, "復元したページを開く").PerformClick();
        menu.Close();
        Check(page.Id == restoredId && window.Entries.All(e => !e.IsTrash) && !window.Canvas.ReadOnly, "Restored-page menu opens the existing last restored target");

        // Both error routes stay on the form, with the retained work and exact target in Details.
        using (var locked = new FileStream(page.Catalog.List().Single(e => e.Document.Id == page.Id).JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            page.SetPinned(true, Now());
            window.RefreshCatalog();
            Check(page.Error is not null && VisibleButton(window, "保存を再試行") && VisibleButton(window, "詳細"), "Save failure retains retry and details");
            Capture(window, root, "error-save");
        }
        Descendants(window).OfType<Button>().Single(b => b.Text == "保存を再試行").PerformClick();
        Check(page.Error is null, "Save retry recovers");
        var readTarget = window.Entries.First(e => e.Document.Id != page.Id);
        using (var locked = new FileStream(readTarget.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            list.ClearSelected();
            list.SelectedIndex = window.Entries.ToList().FindIndex(e => e.Document.Id == readTarget.Document.Id);
            Check(page.LoadError is not null && VisibleButton(window, "読み込みを再試行") && VisibleButton(window, "詳細"), "Read failure retains retry and details");
            Capture(window, root, "error-load");
        }
        window.RetryOpenButton.PerformClick();
        Check(page.LoadError is null && page.Id == readTarget.Document.Id, "Read retry opens exact target");
        window.Size = new(800, 620);
        Application.DoEvents();
        Capture(window, root, "list-minimum");
        window.Hide();
        Console.WriteLine("PASS: row/background/empty-list menus, contextual undo, text/ink routing, restore shortcut, quiet success and visible error recovery: " + root);
    }

    private static bool VisibleButton(Control window, string text) => Descendants(window).OfType<Button>().Any(b => b.Text == text && b.Visible);
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static ToolStripMenuItem Command(ContextMenuStrip menu, string prefix) => menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text?.StartsWith(prefix, StringComparison.Ordinal) == true);

    private static ContextMenuStrip RowMenu(DesktopWindow window, int index)
    {
        var list = window.PagesList;
        list.TopIndex = index;
        var row = list.GetItemRectangle(index);
        return MouseMenu(list, new(16, row.Top + 20));
    }

    private static ContextMenuStrip BackgroundMenu(DesktopWindow window)
    {
        // A tall host leaves visible blank space below the synthetic rows.
        window.Size = new(1180, 1040);
        Application.DoEvents();
        var list = window.PagesList;
        list.TopIndex = Math.Max(0, list.Items.Count - 1);
        var point = new Point(16, list.ClientSize.Height - 5);
        Check(list.IndexFromPoint(point) < 0, "Synthetic background point is outside every row");
        return MouseMenu(list, point);
    }

    private static ContextMenuStrip MouseMenu(ListBox list, Point point)
    {
        _ = SendMessage(list.Handle, 0x0204, new IntPtr(2), Position(point)); // WM_RBUTTONDOWN
        var screen = list.PointToScreen(point);
        _ = SendMessage(list.Handle, 0x007b, list.Handle, Position(screen)); // WM_CONTEXTMENU
        Application.DoEvents();
        var menu = list.ContextMenuStrip!;
        Check(menu.Visible, "Targeted right click opens a context menu");
        return menu;
    }

    private static IntPtr Position(Point point) => new((ushort)point.X | (long)(ushort)point.Y << 16);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void Capture(Control control, string root, string name)
    {
        Directory.CreateDirectory(root);
        control.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
    }
}
