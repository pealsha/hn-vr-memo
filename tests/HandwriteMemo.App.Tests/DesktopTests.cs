using System.Diagnostics;
using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class DesktopTests
{
    [TestMethod]
    public void LaunchAndMousePolicy()
    {
        Check(AppHost.ShouldOpenDesktop(desktopRequested: false, backgroundLaunch: false),
            "Manual launch opens the desktop regardless of SteamVR state");
        Check(AppHost.ShouldOpenDesktop(desktopRequested: true, backgroundLaunch: false),
            "Explicit desktop launch opens the window");
        Check(AppHost.ShouldOpenDesktop(desktopRequested: true, backgroundLaunch: true),
            "Explicit desktop takes priority over background launch");
        Check(MemoCanvas.AcceptMouse(true, false, 0), "Real mouse still works while pen hovers");
        Check(!MemoCanvas.AcceptMouse(true, false, 0xFF515700), "Native pen's promoted mouse is not duplicate ink");
        Check(MemoCanvas.AcceptMouse(false, false, 0xFF515700), "Mouse-only tablet driver remains supported");
        Check(!MemoCanvas.AcceptMouse(true, true, 0), "Mouse cannot interrupt active pen contact");
    }

    [TestMethod]
    public void SharedInput()
    {
        var (root, folder, store, sample, engine, origin, session, Now, window, entry) = CreateFixture();
        using var ownedWindow = window;
        // One memory page, two input surfaces. Opening it during VR ink must keep the active line.
        var vrStart = new Vector3((.38f - .5f) * .48f, (.5f - .58f) * .36f, .05f);
        session.VrPoint(new(vrStart, true), false, .15f, Now(), DrawingTool.Pen);
        session.VrPoint(new(vrStart, true), true, .15f, Now(), DrawingTool.Pen);
        Check(session.Open(entry, Now()) && engine.IsWriting, "Opening same page must preserve live VR ink");
        Check(!session.BeginDesktop(Now()), "Desktop must wait for active VR stroke");
        session.VrPoint(new(vrStart + new Vector3(.04f, 0, 0), true), true, .15f, Now(), DrawingTool.Pen);
        session.VrPoint(new(vrStart, true), false, .15f, Now(), DrawingTool.Pen);
        Check(session.Notice is null, "Completed stroke clears the wait notice so the next VR input guide is visible");
        int afterVr = engine.Strokes.Count;
        Check(afterVr == sample.Strokes.Count + 1, "VR line appended");
        var canvas = window.Canvas;
        var r = canvas.PaperBounds;
        Point At(float x, float y) => new((int)(r.Left + x * r.Width), (int)(r.Top + y * r.Height));
        canvas.BeginInput(At(.12f, .73f));
        // VR polling and tracking interruptions must leave an ongoing desktop line intact.
        session.VrPoint(new(vrStart, true), true, .15f, Now(), DrawingTool.Pen);
        session.InterruptVr(Now());
        canvas.MoveInput(At(.23f, .60f));
        canvas.MoveInput(At(.35f, .73f));
        canvas.MoveInput(At(.45f, .60f));
        canvas.EndInput();
        Check(engine.Strokes.Count == afterVr + 1, "Desktop line preserved while VR polls");
        var saved = store.Read(entry.JsonPath);
        Check(saved.Strokes.Count == sample.Strokes.Count + 2 && saved.IsPinned, "Both edits persisted with pin");
        Check(saved.Strokes[0].StartedUtc == sample.Strokes[0].StartedUtc, "Original stroke timestamp retained");
        Check(saved.Strokes[0].Points.SequenceEqual(sample.Strokes[0].Points), "Original page coordinates and times retained");
        // A complete PC click may occur between two VR frames, while the VR trigger remains held.
        session.VrPoint(new(vrStart, false), true, .15f, Now(), DrawingTool.Pen);
        Check(session.BeginDesktop(Now()), "Short desktop input starts");
        session.DesktopPoint(null, false, Now(), DrawingTool.Pen);
        session.VrPoint(new(vrStart, true), true, .15f, Now(), DrawingTool.Pen);
        Check(!engine.IsWriting && engine.Strokes.Count == saved.Strokes.Count, "Held VR trigger must not rearm after short desktop input");
        session.VrPoint(new(vrStart, true), false, .15f, Now(), DrawingTool.Pen);
        window.RefreshCatalog();
        window.Refresh();
        Application.DoEvents();
        Capture(window, root, "03-written");
    }

    [TestMethod]
    public void ResizeAndHistoricalErase()
    {
        var (root, folder, store, sample, engine, origin, session, Now, window, entry) = CreateFixture();
        using var ownedWindow = window;
        var toolbar = Descendants(window).OfType<DesktopToolbar>().Single();
        var groups = toolbar.Controls.Cast<Control>().ToArray();
        if (Screen.FromControl(window).WorkingArea.Width >= window.Width - toolbar.Width + toolbar.Padding.Horizontal + groups.Sum(group => group.Width))
            Check(groups.All(group => group.Top == groups[0].Top && group.Right <= toolbar.ClientSize.Width),
                "The startup window fits the whole toolbar on one row when the display is wide enough");
        PrepareEdits(engine, session, window, Now);
        var saved = store.Read(entry.JsonPath);
        window.Size = new(800, 620);
        Application.DoEvents();
        window.Refresh();
        Check(groups[^1].Top > groups[0].Top, "The page actions wrap when the window is narrowed");
        Capture(window, root, "04-small");
        CheckMapping(window.Canvas);
        CheckWidthSamples(window);
        CheckCatalogHeader(window);
        // Logical size, so the large buttons and the taller header are checked at every display scale.
        var largeSize = new Size(window.LogicalToDeviceUnits(1540), window.LogicalToDeviceUnits(1020));
        window.Size = largeSize;
        Application.DoEvents();
        window.Refresh();
        // Windows clamps top-level form width to the display; large-button transitions are also tested directly below.
        if (Screen.FromControl(window).WorkingArea.Width >= largeSize.Width)
            Check(toolbar.LargeButtons, "The large window uses the large buttons");
        Capture(window, root, "05-large");
        CheckMapping(window.Canvas);
        CheckWidthSamples(window);
        CheckCatalogHeader(window);
        // Reopen into another engine, erase and undo historical ink, and save without changing its clock.
        var reopened = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        reopened.LoadPage(saved, 1);
        reopened.SetEraserMode(EraserMode.Partial);
        reopened.UpdatePagePoint(new(.25f, .22f), false, 2, DrawingTool.Eraser);
        reopened.UpdatePagePoint(new(.25f, .22f), true, 3, DrawingTool.Eraser);
        reopened.UpdatePagePoint(null, false, 4, DrawingTool.Eraser);
        var cut = MemoDocument.Capture(saved.Id, saved.CreatedUtc, origin, reopened.BoardSize, reopened.Strokes);
        Check(cut.Strokes.Count >= saved.Strokes.Count, "Partial eraser splits historical line");
        Check(reopened.Undo(), "Historical erasure undo");
        Check(reopened.Strokes.Count == saved.Strokes.Count, "Undo retained every original and added line");
    }

    [TestMethod]
    public void ToolbarLargeButtonThresholds()
    {
        using var toolbar = new DesktopToolbar { Width = 1200 };
        var tile = new ToolbarTile("ペン", ToolbarIcon.Pen);
        var action = new Button { Text = "新しいページ" };
        toolbar.AddGroup("道具", tile, action);

        toolbar.UpdateButtonMetrics(1079, 96);
        Check(!toolbar.LargeButtons && toolbar.TileSide == 36 && toolbar.ActionHeight == 32,
            "Editor width below 1080px keeps standard toolbar metrics");

        toolbar.UpdateButtonMetrics(1080, 96);
        toolbar.PerformLayout();
        Check(toolbar.LargeButtons && toolbar.TileSide == 48 && toolbar.ActionHeight == 48,
            "Editor width at 1080px switches to large toolbar metrics");
        Check(tile.Width == 48 && tile.Height == 48 && action.MinimumSize.Height == 48,
            "Layout applies large metrics to tiles and action buttons");

        toolbar.UpdateButtonMetrics(1040, 96);
        Check(toolbar.LargeButtons, "Hysteresis band preserves large buttons during resize");

        toolbar.UpdateButtonMetrics(1000, 96);
        Check(!toolbar.LargeButtons && toolbar.TileSide == 36 && toolbar.ActionHeight == 32,
            "Editor width at 1000px returns to standard toolbar metrics");

        toolbar.UpdateButtonMetrics(1619, 144);
        Check(!toolbar.LargeButtons, "Scaled 150% DPI waits until 1620px to enter large mode");
        toolbar.UpdateButtonMetrics(1620, 144);
        Check(toolbar.LargeButtons && toolbar.TileSide == 72 && toolbar.ActionHeight == 72,
            "Scaled 150% DPI enters large mode at 1620px with scaled button sizes");
    }

    [TestMethod]
    public void ToolControls()
    {
        var (root, folder, store, sample, engine, origin, session, Now, window, entry) = CreateFixture();
        using var ownedWindow = window;
        PrepareEdits(engine, session, window, Now);
        var saved = store.Read(entry.JsonPath);
        // Run real window controls for navigation, pinning, trash and restore.
        Button Button(string text) => Descendants(window).OfType<Button>().Single(b => b.Text == text || b.AccessibleName == text);
        Button("元に戻す").PerformClick();
        Check(engine.Strokes.Count == saved.Strokes.Count - 1 && Button("やり直す").Enabled, "Desktop undo offers redo");
        Button("やり直す").PerformClick();
        Check(engine.Strokes.Count == saved.Strokes.Count && !Button("やり直す").Enabled, "Desktop redo restores saved ink and availability");
        Check(store.Read(entry.JsonPath).Strokes.Count == saved.Strokes.Count, "Desktop redo autosaved");
        engine.SetPenStyle(ToolPalette.Colors[2], ToolPalette.Widths[2]);
        engine.SetEraserMode(EraserMode.Partial);
        window.RefreshCatalog();
        Check(((ToolbarTile)Button("太")).Selected, "Desktop width follows shared VR selection");
        Check(((ToolbarTile)Button("青")).Selected, "Desktop color selection persists without focus");
        var part = (ToolbarTile)Button("部分消し");
        part.PerformClick();
        engine.SetEraserMode(EraserMode.Stroke);
        window.RefreshCatalog();
        Check(((ToolbarTile)Button("線ごと消す")).Selected, "Active desktop eraser follows shared mode");
        part.PerformClick();
        Check(engine.EraserMode == EraserMode.Partial, "Desktop eraser selection updates shared mode immediately");
        Button("ペン").PerformClick();
        Check(((ToolbarTile)Button("ペン")).Selected && !part.Selected && !((ToolbarTile)Button("線ごと消す")).Selected &&
            engine.EraserMode == EraserMode.Partial,
            "PC pen remains exclusive without changing the shared VR eraser mode");
        engine.SetEraserMode(EraserMode.Stroke);
        window.RefreshCatalog();
        Check(((ToolbarTile)Button("ペン")).Selected && !part.Selected && !((ToolbarTile)Button("線ごと消す")).Selected,
            "VR eraser mode changes do not select a desktop eraser while the PC uses the pen");
    }

    [TestMethod]
    public void NavigationTrashAndSaveFailure()
    {
        var (root, folder, store, sample, engine, origin, session, Now, window, entry) = CreateFixture();
        using var ownedWindow = window;
        var pageList = Descendants(window).OfType<ListBox>().Single();
        Check(window.SelectionCountLabel.Text == "", "Ordinary list view has an empty header");
        window.SelectButton.PerformClick();
        Check(window.SelectionCountLabel.Text == $"{pageList.SelectedItems.Count}件選択", "Selection mode shows the count");
        window.SelectButton.PerformClick();
        pageList.SetSelected(0, true);
        pageList.SetSelected(1, true);
        Check(window.SelectionCountLabel.Text == "2件選択", "Multiple selection shows the count outside selection mode");
        pageList.ClearSelected();
        PrepareEdits(engine, session, window, Now);
        var saved = store.Read(entry.JsonPath);
        Button Button(string text) => Descendants(window).OfType<Button>().Single(b => b.Text == text || b.AccessibleName == text);
        var canvas = window.Canvas;
        var r = canvas.PaperBounds;
        Point At(float x, float y) => new((int)(r.Left + x * r.Width), (int)(r.Top + y * r.Height));
        Button("★ ピン解除").PerformClick();
        Check(!session.IsPinned, "Desktop unpin control");
        Button("☆ ピン留め").PerformClick();
        Check(session.IsPinned, "Desktop pin control");
        var fromVr = window.Entries.First(e => e.Document.Id != session.Id);
        Check(session.Open(fromVr, Now()), "Shared page opened outside desktop selection");
        Thread.Sleep(150);
        Application.DoEvents();
        var sharedList = Descendants(window).OfType<ListBox>().Single();
        Check(((PageCatalogEntry?)sharedList.SelectedItem)?.Document.Id == session.Id, "Desktop timer follows VR page without manual refresh");
        Button("☆ ピン留め").PerformClick();
        Check(store.Read(fromVr.JsonPath).IsPinned, "Desktop pin acts on page opened by VR");
        Button("★ ピン解除").PerformClick();
        Check(session.Open(entry, Now()), "Return to original shared page");
        Thread.Sleep(150);
        Application.DoEvents();
        Button("次のページ").PerformClick();
        Check(session.Id != sample.Id, "Desktop next page");
        Button("前のページ").PerformClick();
        Check(session.Id == sample.Id, "Desktop previous page");
        Button("ゴミ箱へ").PerformClick();
        Check(session.Id != sample.Id && session.Catalog.List(true).Any(e => e.Document.Id == sample.Id), "Desktop trash control");
        var trash = Descendants(window).OfType<RadioButton>().Single(b => b.Text == "ゴミ箱");
        trash.Checked = true;
        var list = Descendants(window).OfType<ListBox>().Single();
        list.SelectedIndex = 0;
        Check(window.SelectionCountLabel.Text == "", "Ordinary trash view has an empty header");
        Application.DoEvents();
        window.Refresh();
        Capture(window, root, "06-trash");
        int beforePreview = engine.Strokes.Count;
        canvas.BeginInput(At(.2f, .5f));
        canvas.EndInput();
        Check(engine.Strokes.Count == beforePreview, "Trash preview is read only");
        Button("復元する").PerformClick();
        Check(session.Catalog.List().Any(e => e.Document.Id == sample.Id), "Desktop restore control");
        Check(trash.Checked && ContextUndoTests.MenuCommand(window, "復元したページを開く").Enabled, "Restore stays in trash with open shortcut");
        ContextUndoTests.MenuCommand(window, "復元したページを開く").PerformClick();
        Check(!trash.Checked && session.Id == sample.Id, "Restored page opens directly");
        var restored = session.Catalog.List().Single(e => e.Document.Id == sample.Id);
        Check(session.Open(restored, Now()), "Open restored page");
        window.RefreshCatalog();
        using (var locked = new FileStream(restored.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = canvas.PaperBounds;
            canvas.BeginInput(At(.7f, .7f));
            canvas.MoveInput(At(.8f, .7f));
            canvas.EndInput();
            Check(session.Error is not null, "Desktop save failure displayed");
            Button("★ ピン解除").PerformClick();
            Check(!session.IsPinned && Button("☆ ピン留め").Enabled, "Failed unpin still shows the retained in-memory pin state");
            Button("☆ ピン留め").PerformClick();
            Check(session.IsPinned && Button("★ ピン解除").Enabled, "Failed pin still offers the matching inverse action");
            Button("新しいページ").PerformClick();
            Check(session.Id == sample.Id && engine.Strokes.Count > beforePreview, "Failed save blocks desktop new page without losing ink");
        }
        Button("保存を再試行").PerformClick();
        Check(session.Error is null, "Desktop save retry button");
    }

    [TestMethod]
    public void ReadFailureRetry()
    {
        var (root, folder, store, sample, engine, origin, session, Now, window, entry) = CreateFixture();
        using var ownedWindow = window;
        Button Button(string text) => Descendants(window).OfType<Button>().Single(b => b.Text == text || b.AccessibleName == text);
        var list = Descendants(window).OfType<ListBox>().Single();
        string broken = Path.Combine(folder, "broken.json");
        File.WriteAllText(broken, "{broken");
        window.RefreshCatalog();
        Check(File.Exists(broken) && Descendants(window).OfType<Label>().Any(l => l.Text.Contains("broken.json")), "Unreadable page identified and retained");
        File.Move(broken, Path.Combine(root, "broken-preserved.txt"));
        window.RefreshCatalog();
        Check(!Descendants(window).OfType<Label>().Any(l => l.Text.Contains("読めないページ")), "Refresh clears resolved warning");
        var readTarget = window.Entries.First(e => e.Document.Id != session.Id);
        var retainedId = session.Id;
        using (var locked = new FileStream(readTarget.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // SelectedIndex adds to a MultiExtended selection; model an unmodified single click.
            list.ClearSelected();
            list.SelectedIndex = window.Entries.ToList().FindIndex(e => e.Document.Id == readTarget.Document.Id);
            Check(session.LoadError is not null && session.Id == retainedId && Button("読み込みを再試行").Visible, "Read failure retains current page and offers retry");
            using var errorDetails = window.CreateDetailsWindow();
            Check(errorDetails.Controls.OfType<TextBox>().Single().Text.Contains(readTarget.JsonPath), "Read details identify complete target path");
        }
        Button("読み込みを再試行").PerformClick();
        Check(session.LoadError is null && session.Id == readTarget.Document.Id &&
            list.SelectedItem is PageCatalogEntry active && active.Document.Id == session.Id, "Read retry updates shared page and list selection");
    }

    [TestMethod]
    public void Duplication()
    {
        var (root, folder, store, sample, engine, origin, session, Now, window, entry) = CreateFixture();
        using var ownedWindow = window;
        var list = Descendants(window).OfType<ListBox>().Single();
        // Copying creates independent files and history, without rewriting the source page.
        var sourceEntry = session.Catalog.List().Single(e => e.Document.Id == sample.Id);
        var originalBytes = File.ReadAllBytes(sourceEntry.JsonPath);
        Check(session.Duplicate(sourceEntry, Now()), "Duplicate a different page");
        window.RefreshCatalog();
        var duplicate = session.Snapshot();
        Check(duplicate.Id != sample.Id && !duplicate.IsPinned && duplicate.CreatedUtc >= origin,
            "Duplicate has a new identity, current creation time and no pin");
        Check(duplicate.BoardSize == sourceEntry.Document.BoardSize && duplicate.Strokes.Count == sourceEntry.Document.Strokes.Count,
            "Duplicate preserves page size and stroke count");
        foreach (var (a, b) in duplicate.Strokes.Zip(sourceEntry.Document.Strokes))
            Check(a.Color == b.Color && a.WidthMetres == b.WidthMetres &&
                a.Points.Select(p => (p.XMetres, p.YMetres)).SequenceEqual(b.Points.Select(p => (p.XMetres, p.YMetres))),
                "Duplicate preserves every coordinate, color and width");
        Check(originalBytes.SequenceEqual(File.ReadAllBytes(sourceEntry.JsonPath)), "Duplicate leaves source bytes unchanged");
        Check(!engine.CanUndo && !engine.CanRedo && ((PageCatalogEntry?)list.SelectedItem)?.Document.Id == session.Id,
            "Duplicate is selected with independent empty edit history");
        CheckMapping(window.Canvas);
        var activeDuplicate = session.Catalog.List().Single(e => e.Document.Id == session.Id);
        Check(session.Duplicate(activeDuplicate, Now()), "Duplicate current page");
        var emptyId = session.Id;
        Check(session.NewPage(Now()) && session.SetPinned(true, Now()), "Save blank source for duplication");
        var blank = session.Catalog.List().Single(e => e.Document.Id == session.Id);
        Check(session.Duplicate(blank, Now()) && engine.Strokes.Count == 0 && !session.IsPinned,
            "Blank page duplicates without copied pin");
        Check(session.Id != emptyId, "Each duplicate is independent");
    }

    [TestMethod]
    public void HostActivation()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "desktop-" + Guid.NewGuid().ToString("N")));
        var folder = Path.Combine(root, "pages");
        Directory.CreateDirectory(root);
        // Exercise the actual host with OpenVR explicitly absent and logs isolated to artifacts.
        Check(new AppHost(new AppOptions { SaveFolder = folder }, .18, false, true, null,
            Path.Combine(root, "logs"), disableVr: true).Run() == 0, "No-VR startup host");
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset);
        using var activateTimer = new System.Windows.Forms.Timer { Interval = 40 };
        var host = new AppHost(new AppOptions { SaveFolder = folder }, 5, false, false, activation,
            Path.Combine(root, "logs"), disableVr: true, background: true);
        bool checkedHidden = false, checkedShown = false;
        activateTimer.Tick += (_, _) =>
        {
            bool visible = Application.OpenForms.Cast<Form>().Any(f => f.Visible && f.Text == "HnMemo");
            if (!checkedHidden)
            {
                Check(!visible, "Background launch is hidden");
                checkedHidden = true;
                activation.Set();
            }
            else
            {
                Check(visible, "Second launch activation opens desktop");
                checkedShown = true;
                activateTimer.Stop();
                host.RequestStop();
            }
        };
        activateTimer.Start();
        Check(host.Run() == 0 && checkedHidden && checkedShown,
            "Activation signal host");
        Console.WriteLine("PASS: desktop no-VR host and activation: " + root);
    }

    private static (string Root, string Folder, MemoStore Store, MemoDocument Sample, HandwriteEngine Engine, DateTimeOffset Origin, MemoSession Session, Func<double> Now,
        DesktopWindow Window, PageCatalogEntry Entry) CreateFixture()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "desktop-" + Guid.NewGuid().ToString("N")));
        var folder = Path.Combine(root, "pages");
        Directory.CreateDirectory(root);
        var store = new MemoStore(folder);
        var old = DateTimeOffset.UtcNow.AddDays(-2);
        var sample = new MemoDocument(2, Guid.NewGuid(), old, old.AddSeconds(20), BoardSize.Default,
        [
            Line(old, "#182634", .0015f, new(.06f,.065f,0), new(.09f,.08f,1), new(.12f,.065f,2), new(.15f,.08f,3), new(.18f,.065f,4)),
            Line(old.AddSeconds(5), "#1866C2", .0025f, new(.08f,.16f,5), new(.12f,.24f,6), new(.16f,.16f,7)),
            Line(old.AddSeconds(9), "#182634", .0015f, new(.21f,.20f,9), new(.29f,.20f,10)),
            Line(old.AddSeconds(12), "#182634", .0015f, new(.25f,.16f,12), new(.25f,.24f,13)),
        ], true);
        store.Save(sample, s => MemoRenderer.WritePng(sample, s));
        for (int i = 1; i <= 4; i++)
        {
            var created = old.AddHours(i);
            var doc = new MemoDocument(2, Guid.NewGuid(), created, created.AddSeconds(20), BoardSize.Default,
                [Line(created, i % 2 == 0 ? "#C73535" : "#182634", .0015f,
                    new(.04f,.07f,0), new(.13f,.08f,1), new(.22f,.065f,2), new(.31f,.08f,3))]);
            store.Save(doc, s => MemoRenderer.WritePng(doc, s));
        }
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var origin = DateTimeOffset.UtcNow;
        var session = new MemoSession(engine, folder, origin, _ =>
        {
        });
        var clock = Stopwatch.StartNew();
        Func<double> now = () => clock.Elapsed.TotalSeconds;
        var window = new DesktopWindow(engine, session, now);
        try
        {
            window.Show();
            Application.DoEvents();
            Capture(window, root, "01-list");
            var entry = session.Catalog.List().Single(e => e.Document.Id == sample.Id);
            Check(session.Open(entry, now()), "Open saved page");
            window.RefreshCatalog();
            window.Refresh();
            Application.DoEvents();
            Capture(window, root, "02-view");
            return (root, folder, store, sample, engine, origin, session, now, window, entry);
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    private static void PrepareEdits(HandwriteEngine engine, MemoSession session, DesktopWindow window, Func<double> now)
    {
        var vrStart = new Vector3((.38f - .5f) * .48f, (.5f - .58f) * .36f, .05f);
        session.VrPoint(new(vrStart, true), false, .15f, now(), DrawingTool.Pen);
        session.VrPoint(new(vrStart, true), true, .15f, now(), DrawingTool.Pen);
        session.VrPoint(new(vrStart + new Vector3(.04f, 0, 0), true), true, .15f, now(), DrawingTool.Pen);
        session.VrPoint(new(vrStart, true), false, .15f, now(), DrawingTool.Pen);
        var canvas = window.Canvas;
        var r = canvas.PaperBounds;
        Point At(float x, float y) => new((int)(r.Left + x * r.Width), (int)(r.Top + y * r.Height));
        canvas.BeginInput(At(.12f, .73f));
        canvas.MoveInput(At(.23f, .60f));
        canvas.MoveInput(At(.35f, .73f));
        canvas.MoveInput(At(.45f, .60f));
        canvas.EndInput();
        window.RefreshCatalog();
        window.Refresh();
        Application.DoEvents();
    }

    private static MemoStroke Line(DateTimeOffset started, string color, float width, params MemoPoint[] points) => new(started, points, color, width);
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void CheckWidthSamples(DesktopWindow window)
    {
        string[] names = ["細", "中", "太"];
        var toolbar = Descendants(window).OfType<DesktopToolbar>().Single();
        var tiles = Descendants(toolbar).OfType<ToolbarTile>().ToArray();
        Check(tiles.All(b => b.Size == tiles[0].Size), "Tools, colors, widths and history retain the same tile size");
        float available = Math.Min(tiles[0].ClientSize.Width, tiles[0].ClientSize.Height) - 10 * tiles[0].DeviceDpi / 96f;
        float scale = Math.Min(4, available / window.Canvas.InkWidthPixels(ToolPalette.Widths.Max(), BoardSize.Default));
        for (int i = 0; i < names.Length; i++)
        {
            var button = tiles.Single(b => b.AccessibleName == names[i]);
            float expected = window.Canvas.InkWidthPixels(ToolPalette.Widths[i], BoardSize.Default) * scale;
            using var image = new Bitmap(button.Width, button.Height);
            button.DrawToBitmap(image, button.ClientRectangle);
            int y = image.Height / 2;
            int blackPixels = Enumerable.Range(0, image.Width).Count(x =>
            {
                var pixel = image.GetPixel(x, y);
                return pixel.R < 100 && pixel.G < 100 && pixel.B < 100;
            });
            Check(Math.Abs(blackPixels - expected) <= 2,
                "Painted width circles preserve the shared scale (at most 4x) at each window size");
            Check(expected <= available + .01f, "Width circles fit inside the common tile without clipping");
        }
    }
    [TestMethod]
    public void SettingsGearAlignment()
    {
        foreach (int size in new[] { 32, 48 })
        {
            using var button = new ToolbarTile("設定", ToolbarIcon.Settings)
            {
                Size = new(size, size),
                ForeColor = Color.Magenta
            };
            using var image = new Bitmap(size, size);
            button.DrawToBitmap(image, button.ClientRectangle);

            int left = size, top = size, right = -1, bottom = -1;
            for (int y = 4; y < size - 4; y++)
                for (int x = 4; x < size - 4; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    // Include antialiased ink while excluding the neutral tile and border.
                    if (pixel.R - pixel.G < 96 || pixel.B - pixel.G < 96)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }

            Check(right - left + 1 >= size * .45f && bottom - top + 1 >= size * .45f,
                $"Settings gear scales with the {size}px button");
            Check(Math.Abs(left - (size - 1 - right)) <= 1 && Math.Abs(top - (size - 1 - bottom)) <= 1,
                $"Settings gear is centered in the {size}px button (ink bounds {left},{top}–{right},{bottom})");
        }
    }

    private static void CheckMapping(MemoCanvas canvas)
    {
        var r = canvas.PaperBounds;
        var uv = canvas.PagePoint(new(r.Left + r.Width * .37f, r.Top + r.Height * .63f));
        Check(uv.HasValue && Vector2.Distance(uv.Value, new(.37f, .63f)) < 1e-5f, "Resize must retain page coordinates");
        Check(canvas.PagePoint(new(r.Left - 1, r.Top)) is null, "Margin must not draw");
    }
    private static void CheckCatalogHeader(DesktopWindow window)
    {
        var controls = Descendants(window).ToArray();
        var tabs = controls.OfType<CatalogTab>().ToArray();
        var reload = controls.OfType<Button>().Single(button => button.AccessibleName == "更新");
        Check(tabs.Length == 2 && tabs.All(tab => tab.Parent == tabs[0].Parent && tab.Size == tabs[0].Size && tab.Top == tabs[0].Top) &&
            tabs[0].Parent!.Controls.Count == 2, "Only the two equal tabs share the navigation row");
        var select = window.SelectButton;
        Check(reload.Parent == select.Parent && reload.Top == select.Top && reload.Height == select.Height &&
            reload.Width == reload.Height && reload.Right < select.Left && select.Right <= select.Parent!.ClientSize.Width - select.Parent.Padding.Right,
            "Square refresh command and selection fit together below the tabs");
        var settings = window.SettingsButton;
        var header = settings.Parent!;
        Check(header.Parent == window && header.Dock == DockStyle.Top && tabs[0].Parent!.Parent == header &&
            settings.AccessibleName == "設定" && settings.Width == settings.Height && settings.Right == header.ClientSize.Width - settings.Margin.Right &&
            header.ClientRectangle.Contains(settings.Bounds), "The settings gear stays at the top right of the common navigation row");
    }
    private static void Capture(Form form, string folder, string name)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }
}
