using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class VrPageTests
{
    [TestMethod]
    public void GalleryNavigation()
    {
        var (root, rig, store) = CreateFixture();
        using var input = rig;
        var browser = rig.Browser;
        using var painter = new BoardPainter();
        void Capture(string name)
        {
            painter.Draw(rig.Engine, rig.Frame, VrInputStatus.ForPage(rig.Page), page: rig.Page, vrPages: browser);
            painter.Image.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }
        var initialId = rig.Page.Id;
        rig.Point(.5f, .5f, false);
        Check(browser.Entries.Select(e => e.Document.Id).SequenceEqual(rig.Page.Catalog.List().Select(e => e.Document.Id)), "VR must use catalog order");
        Capture("01-editor");

        // LIST is one fresh press. Sliding that held press onto a card must not open it.
        var listButton = rig.Button(BoardCommand.PageList);
        rig.Point(listButton.X, listButton.Y, true);
        Check(browser.IsOpen, "LIST opens browser");
        rig.Point(.17f, .4f, true);
        Check(browser.IsOpen && rig.Page.Id == initialId && rig.Engine.Strokes.Count == 0, "Held LIST trigger cannot open a card or draw");
        rig.Point(.17f, .4f, false);
        Capture("02-list-first");
        // Card margins, card gaps and page-count label are inert, including eraser input.
        foreach (var uv in new[] { new Vector2(.2f, .01f), new(.3f, .84f), new(.3375f, .42f), new(.5f, .92f) })
        {
            rig.Click(uv, DrawingTool.Eraser);
            Check(browser.IsOpen && rig.Page.Id == initialId && rig.Engine.Strokes.Count == 0, "List gaps cannot act as ink, erase or hidden buttons");
        }
        rig.Point(.3375f, .42f, true);
        rig.Point(.17f, .4f, true);
        Check(browser.IsOpen, "A press started in a gap cannot slide into a card");
        rig.Point(.17f, .4f, false);

        rig.Command(BoardCommand.NextPage);
        Check(browser.Screen == 1, "List next screen");
        Capture("03-list-second");
        rig.Command(BoardCommand.NextPage);
        Check(browser.Screen == 2 && browser.ScreenCount == 3, "List final partial screen");
        Capture("04-list-last");
        rig.Command(BoardCommand.NextPage);
        Check(browser.Screen == 2, "List last screen does not wrap");
        rig.Click(new(.825f, .5f));
        Check(browser.IsOpen, "Empty final list row is inert");
        rig.Command(BoardCommand.PreviousPage);
        rig.Command(BoardCommand.PreviousPage);
        rig.Command(BoardCommand.PreviousPage);
        Check(browser.Screen == 0, "List first screen does not wrap");
    }

    [TestMethod]
    public void ReopenAndNavigation()
    {
        var (root, rig, store) = CreateFixture();
        using var input = rig;
        var browser = rig.Browser;
        using var painter = new BoardPainter();
        void Capture(string name)
        {
            painter.Draw(rig.Engine, rig.Frame, VrInputStatus.ForPage(rig.Page), page: rig.Page, vrPages: browser);
            painter.Image.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }
        rig.Point(.5f, .5f, false);
        rig.Command(BoardCommand.PageList);
        // Reopen an old page with the actual projected VR input; held selection cannot become ink.
        var selected = browser.Entries[0];
        var original = selected.Document;
        rig.Point(.17f, .4f, true);
        Check(!browser.IsOpen && rig.Page.Id == original.Id && rig.Engine.Strokes.Count == original.Strokes.Count, "Card opens historical editable ink");
        Check(rig.Page.VrNeedsTriggerRelease, "Opened page exposes its trigger release guard to the guide");
        rig.Point(.7f, .7f, true);
        Check(rig.Engine.Strokes.Count == original.Strokes.Count, "Held selection cannot draw on reopened page");
        rig.Point(.7f, .7f, false);
        Check(!rig.Page.VrNeedsTriggerRelease, "Guide clears when the trigger is released");
        Capture("05-reopened-pinned");
        rig.Draw(new(.6f, .65f), new(.7f, .72f), new(.8f, .65f));
        var saved = store.Read(selected.JsonPath);
        Check(saved.Strokes.Count == original.Strokes.Count + 1, "Reopened VR page saves appended ink");
        Check(saved.Strokes[0].StartedUtc == original.Strokes[0].StartedUtc && saved.Strokes[0].Points.SequenceEqual(original.Strokes[0].Points),
            "VR reopening keeps old stroke coordinates and clock");
        Capture("06-written");
        rig.Command(BoardCommand.TogglePin);
        Check(!rig.Page.IsPinned && !store.Read(selected.JsonPath).IsPinned, "VR unpin is saved");
        Capture("07-unpinned");
        rig.Command(BoardCommand.TogglePin);
        Check(rig.Page.IsPinned && store.Read(selected.JsonPath).IsPinned, "VR pin is saved");
        Check(browser.Entries.Select(e => e.Document.Id).SequenceEqual(rig.Page.Catalog.List().Select(e => e.Document.Id)), "Pin uses shared catalog ordering");

        // Page navigation follows that same order, preserves edits, and stops at each end.
        var ordered = rig.Page.Catalog.List();
        rig.Command(BoardCommand.PreviousPage);
        Check(rig.Page.Id == ordered[0].Document.Id, "First editor page does not wrap");
        rig.Command(BoardCommand.NextPage);
        Check(rig.Page.Id == ordered[1].Document.Id, "Editor next follows catalog");
        Capture("08-next-page");
        rig.Command(BoardCommand.PreviousPage);
        Check(rig.Page.Id == original.Id && rig.Engine.Strokes.Count == saved.Strokes.Count, "Editor previous restores appended ink");
        Check(rig.Page.Open(ordered[^1], rig.Now()), "Open final page for boundary check");
        rig.Command(BoardCommand.NextPage);
        Check(rig.Page.Id == ordered[^1].Document.Id, "Last editor page does not wrap");
        rig.Command(BoardCommand.NewPage);
        Check(rig.Engine.Strokes.Count == 0 && !browser.IsOpen, "VR NEW starts blank without removing saved pages");
        var blankId = rig.Page.Id;
        rig.Click(new(.2f, -.005f));
        Check(rig.Page.Id == blankId && rig.Engine.Strokes.Count == 0, "Editor gap between paper and toolbar is inert too");
        rig.Command(BoardCommand.PreviousPage);
        Check(rig.Page.Id == blankId, "Blank page previous is inert");
        rig.Command(BoardCommand.NextPage);
        Check(rig.Page.Id == ordered[0].Document.Id, "Blank page next opens first saved page");

        rig.Command(BoardCommand.PageList);
        rig.Click(new(.5f, .52f));
        Check(rig.Page.Id == ordered[1].Document.Id, "Second card row selects its own page");
        rig.Command(BoardCommand.PageList);
        rig.Click(new(.825f, .5f));
        Check(rig.Page.Id == ordered[2].Document.Id, "Third card row selects its own page");
        Check(rig.Page.Open(ordered[0], rig.Now()), "Return to historical page for tool checks");
    }

    [TestMethod]
    public void EraserAndInputArbitration()
    {
        var (root, rig, store) = CreateFixture();
        using var input = rig;
        var browser = rig.Browser;
        using var painter = new BoardPainter();
        void Capture(string name)
        {
            painter.Draw(rig.Engine, rig.Frame, VrInputStatus.ForPage(rig.Page), page: rig.Page, vrPages: browser);
            painter.Image.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }
        rig.Point(.5f, .5f, false);
        rig.Command(BoardCommand.PageList);
        var selected = browser.Entries[0];
        rig.Click(new(.17f, .4f));
        rig.Draw(new(.6f, .65f), new(.7f, .72f), new(.8f, .65f));
        var ordered = rig.Page.Catalog.List();
        // Tool operations stay in Core. A list selection with the eraser never erases on release.
        rig.Command(BoardCommand.PageList);
        rig.Point(.17f, .4f, false, DrawingTool.Eraser);
        rig.Point(.17f, .4f, true, DrawingTool.Eraser);
        int beforeErase = rig.Engine.Strokes.Count;
        rig.Point(.25f, .5f, true, DrawingTool.Eraser);
        Check(rig.Engine.Strokes.Count == beforeErase, "Held eraser selection cannot erase historical ink");
        rig.Point(.25f, .5f, false, DrawingTool.Eraser);
        rig.Click(new(.25f, .5f), DrawingTool.Eraser);
        Check(rig.Engine.Strokes.Count == beforeErase - 1 && store.Read(selected.JsonPath).Strokes.Count == beforeErase - 1, "Historical ink erased and saved through Core");
        rig.Command(BoardCommand.Undo);
        Check(rig.Engine.Strokes.Count == beforeErase, "Shared UNDO restores historical erasure");
        rig.Command(BoardCommand.Redo);
        Check(rig.Engine.Strokes.Count == beforeErase - 1, "VR REDO reapplies historical erasure");
        Check(store.Read(selected.JsonPath).Strokes.Count == beforeErase - 1, "VR REDO autosaves immediately");
        rig.Command(BoardCommand.Undo);

        // Tracking/handover interruption must also disarm gallery commands, not just ink.
        rig.Command(BoardCommand.PageList);
        rig.Point(.17f, .4f, true, tracked: false);
        rig.Point(.17f, .4f, true);
        Check(browser.IsOpen, "Tracking loss blocks held gallery selection");
        rig.Point(.17f, .4f, false);
        rig.Engine.RequireTriggerRelease();
        rig.Point(.17f, .4f, true);
        Check(browser.IsOpen, "Handover release guard also blocks gallery selection");
        rig.Point(.17f, .4f, false);

        // A PC stroke while the gallery is visible remains writable, exclusively owns input and saves.
        Check(rig.Page.BeginDesktop(rig.Now()), "PC can write while VR displays list");
        rig.Page.DesktopPoint(new(.55f, .6f), true, rig.Now(), DrawingTool.Pen);
        rig.Point(.17f, .4f, true);
        Check(browser.IsOpen && rig.Page.DesktopOwnsInput, "VR list cannot interrupt desktop stroke");
        rig.Page.DesktopPoint(new(.65f, .6f), true, rig.Now(), DrawingTool.Pen);
        rig.Page.DesktopPoint(null, false, rig.Now(), DrawingTool.Pen);
        rig.Point(.17f, .4f, true);
        Check(browser.IsOpen, "Held VR trigger remains blocked after PC release");
        rig.Point(.17f, .4f, false);
        Capture("09-list-pc-updated");
        Check(store.Read(selected.JsonPath).Strokes.Count == beforeErase + 1, "PC edit while list open saved to same page");
        Check(rig.Page.Open(ordered[1], rig.Now()) && !browser.IsOpen, "PC opening a page returns VR to shared editor");
    }

    [TestMethod]
    public void FailureRecovery()
    {
        var (root, rig, store) = CreateFixture();
        using var input = rig;
        var browser = rig.Browser;
        using var painter = new BoardPainter();
        void Capture(string name)
        {
            painter.Draw(rig.Engine, rig.Frame, VrInputStatus.ForPage(rig.Page), page: rig.Page, vrPages: browser);
            painter.Image.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }
        rig.Point(.5f, .5f, false);
        var ordered = rig.Page.Catalog.List();
        rig.Page.Open(ordered[1], rig.Now());
        // A read error leaves the current page and list in place, without altering the selected file.
        rig.Command(BoardCommand.PageList);
        var target = browser.Entries[0];
        var currentId = rig.Page.Id;
        using (var locked = new FileStream(target.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            rig.Click(new(.17f, .4f));
            Check(browser.IsOpen && rig.Page.Id == currentId && rig.Page.LoadError is not null, "Failed reopen preserves current page and list");
        }
        Capture("10-open-error");
        rig.Command(BoardCommand.RefreshList);
        rig.Click(new(.17f, .4f));
        Check(rig.Page.Id == target.Document.Id && !browser.IsOpen, "Refresh permits reopen retry");

        // A failed save must block every destructive page replacement and LIST, and retain live ink.
        using (var locked = new FileStream(target.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            rig.Draw(new(.6f, .78f), new(.8f, .78f));
            int retained = rig.Engine.Strokes.Count;
            Check(rig.Page.Error is not null, "Save failure surfaced");
            foreach (var command in new[] { BoardCommand.PageList, BoardCommand.NextPage, BoardCommand.NewPage })
            {
                rig.Command(command);
                Check(rig.Page.Id == target.Document.Id && rig.Engine.Strokes.Count == retained && !browser.IsOpen, "Save failure blocks " + command);
            }
            Capture("11-save-error");
        }
        Check(rig.Page.Save(rig.Now()) && rig.Page.Error is null, "Failed VR save can recover");
    }

    [TestMethod]
    public void TrashAndRestart()
    {
        var (root, rig, store) = CreateFixture();
        using var input = rig;
        var browser = rig.Browser;
        using var painter = new BoardPainter();
        void Capture(string name)
        {
            painter.Draw(rig.Engine, rig.Frame, VrInputStatus.ForPage(rig.Page), page: rig.Page, vrPages: browser);
            painter.Image.Save(Path.Combine(root, name + ".png"), ImageFormat.Png);
        }
        rig.Point(.5f, .5f, false);
        rig.Command(BoardCommand.PageList);
        var selected = browser.Entries[0];
        rig.Click(new(.17f, .4f));
        rig.Draw(new(.6f, .65f), new(.7f, .72f), new(.8f, .65f));
        var ordered = rig.Page.Catalog.List();
        // PC trash/restore invalidation refreshes the list; corrupt files are reported, never removed.
        rig.Command(BoardCommand.PageList);
        var trashed = browser.Entries[^1];
        rig.Page.Catalog.MoveToTrash(trashed);
        rig.Page.NotifyCatalogChanged();
        rig.Point(.5f, .5f, false);
        Check(!browser.Entries.Any(e => e.Document.Id == trashed.Document.Id), "Desktop trash invalidation reaches VR");
        rig.Page.Catalog.Restore(rig.Page.Catalog.List(true).Single());
        rig.Page.NotifyCatalogChanged();
        File.WriteAllText(Path.Combine(rig.Folder, "broken.json"), "{broken");
        rig.Command(BoardCommand.RefreshList);
        Check(browser.Entries.Count == 8 && browser.Notice is not null && File.Exists(Path.Combine(rig.Folder, "broken.json")), "Unreadable JSON reported alongside valid pages without deletion");
        Capture("12-list-unreadable");

        // Startup intentionally remains blank, even after pinning/editing a historical page.
        using var restart = new Input(rig.Folder);
        Check(restart.Engine.Strokes.Count == 0 && restart.Page.Id != rig.Page.Id && restart.Page.Catalog.List().Count == 8, "Restart starts blank and retains historical pages");
    }

    [TestMethod]
    public void EmptyPageAndGuide()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "vr-pages-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        using var painter = new BoardPainter();
        using var empty = new Input(Path.Combine(root, "empty"));
        empty.Command(BoardCommand.PageList);
        Check(empty.Browser.IsOpen && empty.Browser.Entries.Count == 0, "Empty list opens normally");
        painter.Draw(empty.Engine, empty.Frame, VrInputStatus.ForPage(empty.Page), page: empty.Page, vrPages: empty.Browser);
        painter.Image.Save(Path.Combine(root, "13-empty.png"), ImageFormat.Png);
        empty.Command(BoardCommand.BackToPage);
        Check(!empty.Browser.IsOpen && empty.Engine.Strokes.Count == 0, "BACK returns empty page without saving phantom ink");
        empty.Click(ToolPalette.Center(8));
        Check(empty.Page.VrEraserPickupHint && empty.Engine.EraserMode == EraserMode.Partial, "Pen selecting an erase mode requests the physical eraser");
        empty.Draw(new(.2f, .5f), new(.8f, .5f));
        Check(empty.Engine.Strokes.Count == 1 && empty.Page.VrEraserPickupHint, "Erase setting must not turn the held pen into an eraser");
        var pose = new BoardPose(Vector3.Zero, Quaternion.Identity);
        var scene = new GrabScene(pose, BoardResize.PenDock(pose, BoardSize.Default));
        var left = new HandInput(scene.PenPose, true, true, false, true);
        var right = new HandInput(new(new Vector3(2, 0, 0), Quaternion.Identity), true, false, false, true);
        var grabbed = scene.Update(new(left, right, true, false));
        string Guide(GrabFrameResult result) => VrInputGuide.Status(empty.Engine, empty.Page, scene, result, left, right, false, true).Text;
        Check(Guide(grabbed).Contains("消しゴム"), "Actual VR guide explains the eraser pickup");
        empty.Engine.RequireTriggerRelease();
        Check(Guide(grabbed).Length == 0 && empty.Page.VrNeedsTriggerRelease,
            "Trigger guard still takes priority over the tool hint, without displaying release instructions");
        scene.Update(new(left with
        {
            IsTracked = false
        }, right, true, false));
        var blocked = scene.Update(new(left, right, true, false));
        Check(scene.NeedsGripRelease(HandSide.Left) && Guide(blocked).Contains("左手のグリップ"), "Tracking return explains which grip must be released");
        empty.Point(.5f, .5f, false, DrawingTool.Eraser);
        empty.Click(new(.5f, .5f), DrawingTool.Eraser);
        Check(!empty.Page.VrEraserPickupHint && empty.Engine.Strokes.Count == 2, "Physical eraser clears the pickup hint and uses the existing partial erase");
        Console.WriteLine("PASS: VR empty page, eraser pickup and input guide: " + root);
    }

    private static (string Root, Input Rig, MemoStore Store) CreateFixture()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "vr-pages-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var rig = new Input(Path.Combine(root, "pages"));
        var store = new MemoStore(rig.Folder);
        var oldest = DateTimeOffset.UtcNow.AddDays(-8);
        for (int i = 0; i < 8; i++)
        {
            var doc = Sample(oldest.AddDays(i), i + 1, i == 0 || i == 2);
            store.Save(doc, s => MemoRenderer.WritePng(doc, s));
        }
        return (root, rig, store);
    }

    private static MemoDocument Sample(DateTimeOffset created, int number, bool pinned)
    {
        var lines = new List<MemoStroke>();
        void Line(params Vector2[] points) => lines.Add(new MemoStroke(created, points.Select((p, i) =>
            new MemoPoint(p.X * .48f, p.Y * .36f, i * .1)).ToArray(), number % 2 == 0 ? "#2464C4" : "#182634", .0015f));
        // Synthetic handwriting: P and a distinct count of tally marks; no font/OCR in the product.
        Line(new(.06f, .26f), new(.06f, .18f), new(.12f, .18f), new(.13f, .20f), new(.12f, .22f), new(.06f, .22f));
        for (int i = 0; i < number; i++)
            Line(new(.20f + i * .07f, .18f), new(.18f + i * .07f, .26f));
        Line(new(.15f, .5f), new(.35f, .5f));
        Line(new(.45f, .40f), new(.55f, .60f));
        Line(new(.55f, .40f), new(.45f, .60f));
        Line(new(.65f, .48f), new(.78f, .48f));
        Line(new(.65f, .54f), new(.78f, .54f));
        Line(new(.84f, .44f), new(.88f, .40f), new(.88f, .60f));
        return new(2, Guid.NewGuid(), created, created.AddSeconds(10), BoardSize.Default, lines, pinned);
    }

    private sealed class Input : IDisposable
    {
        private double _seconds;
        public string Folder { get; }
        public HandwriteEngine Engine { get; } = new(new(Vector3.Zero, Quaternion.Identity));
        public MemoSession Page { get; }
        public FrameResult Frame { get; private set; }
        public Input(string folder)
        {
            Folder = folder;
            Page = new(Engine, folder, DateTimeOffset.UtcNow, _ =>
            {
            });
            Browser = new VrPageBrowser(Page);
        }
        public VrPageBrowser Browser { get; }
        public void Dispose() => Browser.Dispose();
        public double Now() => _seconds += .02;
        public void Point(float x, float y, bool down, DrawingTool tool = DrawingTool.Pen, bool tracked = true)
        {
            Browser.RefreshIfNeeded();
            double seconds = Now();
            Frame = Page.VrPoint(new(new((x - .5f) * Engine.BoardSize.WidthMetres, (.5f - y) * Engine.BoardSize.HeightMetres, .05f), tracked), down, .15f, seconds, tool, Browser.IsOpen);
            Browser.Handle(Frame, seconds);
        }
        public void Click(Vector2 uv, DrawingTool tool = DrawingTool.Pen)
        {
            Point(uv.X, uv.Y, false, tool);
            Point(uv.X, uv.Y, true, tool);
            Point(uv.X, uv.Y, false, tool);
        }
        public Vector2 Button(BoardCommand command)
        {
            bool list = Browser.IsOpen;
            if (list && command is BoardCommand.PreviousPage or BoardCommand.NextPage)
                return new(command == BoardCommand.PreviousPage ? .13f : .87f, .925f);
            var buttons = list ? BoardControls.ListButtons : BoardControls.EditorButtons;
            int index = buttons.ToList().IndexOf(command);
            if (index < 0)
                throw new InvalidOperationException("Button unavailable: " + command);
            var r = BoardControls.ButtonBounds(index, list);
            return new(r.Left + r.Width / 2, r.Top + r.Height / 2);
        }
        public void Command(BoardCommand command) => Click(Button(command));
        public void Draw(params Vector2[] points)
        {
            Point(points[0].X, points[0].Y, false);
            foreach (var p in points)
                Point(p.X, p.Y, true);
            Point(points[^1].X, points[^1].Y, false);
        }
    }
}
