using System.Diagnostics;
using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class ResizeTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-012"));
        string folder = Path.Combine(root, "pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new MemoStore(folder);
        var old = DateTimeOffset.UtcNow.AddDays(-1);
        MemoStroke Line(string color, float width, params Vector2[] points) => new(old,
            points.Select((p, i) => new MemoPoint(p.X, p.Y, i * .1)).ToArray(), color, width);
        var sample = new MemoDocument(2, Guid.NewGuid(), old, old.AddMinutes(1), BoardSize.Default,
        [
            Line("#182634", .0015f, new(.04f,.09f), new(.04f,.065f), new(.065f,.065f), new(.07f,.07f), new(.065f,.078f), new(.04f,.078f)),
            Line("#182634", .0015f, new(.095f,.065f), new(.085f,.09f)),
            Line("#C53939", .003f, new(.08f,.17f), new(.30f,.17f)),
            Line("#2464C4", .00075f, new(.08f,.22f), new(.30f,.22f)),
            Line("#23834B", .0015f, new(.10f,.27f), new(.13f,.25f), new(.16f,.27f), new(.19f,.25f)),
        ], true);
        store.Save(sample, s => MemoRenderer.WritePng(sample, s));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var clock = Stopwatch.StartNew();
        double Now() => clock.Elapsed.TotalSeconds;
        var page = new MemoSession(engine, folder, DateTimeOffset.UtcNow, _ =>
        {
        });
        Check(page.Open(page.Catalog.List().Single(), Now()), "Open standard sample");
        using var browser = new VrPageBrowser(page);
        using var painter = new BoardPainter();
        CheckOverlayGeometry();
        using var window = new DesktopWindow(engine, page, Now);
        window.Show();
        Application.DoEvents();
        Capture("standard", BoardSize.Default);
        using var reference = (Bitmap)painter.Image.Clone();
        using var referenceTools = (Bitmap)painter.ToolsImage.Clone();
        using var standardPng = MemoRenderer.Render(page.Snapshot());
        Check(page.SetVrResizing(true, Now()), "Begin VR resize");
        Check(!page.BeginDesktop(Now()) && !page.CanNavigate(), "PC input and navigation wait for resize release");
        Check(page.Resize(new(.64f, .44f)), "Resize intermediate page");
        page.SetVrResizing(false, Now());
        Capture("expanded", new(.64f, .44f));
        CheckControls(reference, painter.Image);
        Compare(referenceTools, painter.ToolsImage, new(0, 91, 1024, ToolPalette.DockHeightPixels - 91), new(0, 91));
        using var expanded = (Bitmap)painter.Image.Clone();
        Check(page.SetVrResizing(true, Now()), "Begin second resize");
        Check(page.Resize(BoardResize.Maximum), "Resize maximum page");
        page.SetVrResizing(false, Now());
        Capture("maximum", BoardResize.Maximum);
        CheckControls(reference, painter.Image);
        Compare(referenceTools, painter.ToolsImage, new(0, 91, 1024, ToolPalette.DockHeightPixels - 91), new(0, 91));
        SaveComparison([reference, expanded, painter.Image], painter.ToolsImage, Path.Combine(root, "comparison-physical.png"));
        using (var png = MemoRenderer.Render(page.Snapshot()))
        {
            Check(png.Width == 1707 && png.Height == 1280, "Maximum PNG preserves physical density");
            // Measure the two real rendered line thicknesses at the same physical location.
            foreach (var (yMetres, color) in new[] { (.17f, "#C53939"), (.22f, "#2464C4") })
            {
                int Width(Bitmap image, BoardSize size) => Enumerable.Range((int)(yMetres / size.HeightMetres * image.Height) - 10, 21)
                    .Count(y => image.GetPixel((int)(.15f / size.WidthMetres * image.Width), y).ToArgb() == ColorTranslator.FromHtml(color).ToArgb());
                Check(Math.Abs(Width(standardPng, BoardSize.Default) - Width(png, BoardResize.Maximum)) <= 1, "Rendered stroke thickness stays fixed");
            }
            png.Save(Path.Combine(root, "page-maximum.png"), ImageFormat.Png);
        }

        // Real shared session: PC input prevents resizing, then VR writes in the newly added area.
        var canvas = window.Canvas;
        var r = canvas.PaperBounds;
        Point At(float u, float v) => new((int)(r.Left + u * r.Width), (int)(r.Top + v * r.Height));
        var mapped = canvas.PagePoint(At(.9f, .9f));
        Check(mapped is { } p && p.X > .89f && p.Y * .6f > .50f, "PC maps enlarged page, including lower-right ink");
        int count = engine.Strokes.Count;
        canvas.BeginInput(At(.78f, .80f));
        Check(page.DesktopOwnsInput && !page.SetVrResizing(true, Now()), "Resize cannot mutate an active PC stroke");
        canvas.MoveInput(At(.9f, .84f));
        canvas.EndInput();
        Check(engine.Strokes.Count == count + 1, "PC writes in expanded area");
        Vector3 Tip(float x, float y) => new(x - .4f, .3f - y, .05f);
        page.VrPoint(new(Tip(.7f, .4f), true), true, .15f, Now(), DrawingTool.Pen);
        Check(engine.Strokes.Count == count + 1, "Held VR trigger waits after PC input");
        page.VrPoint(new(Tip(.7f, .4f), true), false, .15f, Now(), DrawingTool.Pen);
        page.VrPoint(new(Tip(.7f, .4f), true), true, .15f, Now(), DrawingTool.Pen);
        page.VrPoint(new(Tip(.75f, .43f), true), true, .15f, Now(), DrawingTool.Pen);
        page.VrPoint(new(Tip(.75f, .43f), true), false, .15f, Now(), DrawingTool.Pen);
        Check(engine.Strokes.Count == count + 2, "VR writes in expanded area after PC release");
        Capture("written-maximum", BoardResize.Maximum);
        var savedEntry = page.Catalog.List().Single(e => e.Document.Id == sample.Id);
        var saved = store.Read(savedEntry.JsonPath);
        Check(saved.BoardSize == BoardResize.Maximum && saved.IsPinned && saved.Strokes.Count == count + 2, "Size and both surfaces persisted");
        for (int i = 0; i < sample.Strokes.Count; i++)
            for (int j = 0; j < sample.Strokes[i].Points.Count; j++)
            {
                var a = sample.Strokes[i].Points[j];
                var b = saved.Strokes[i].Points[j];
                Check(Math.Abs(a.XMetres - b.XMetres) < 1e-6f && Math.Abs(a.YMetres - b.YMetres) < 1e-6f && a.ElapsedSeconds == b.ElapsedSeconds,
                    "Original physical ink and time remain unchanged");
            }
        Check(page.NewPage(Now()) && engine.BoardSize == BoardSize.Default, "New pages reset dimensions");
        // Open the enlarged page through actual VR LIST hit testing on a standard board.
        browser.Handle(default(FrameResult) with
        {
            Commands = [BoardCommand.PageList]
        }, Now());
        browser.Handle(new(ContactState.Proximity, new(.175f, .5f), [], [BoardCommand.OpenPage], false), Now());
        Check(engine.BoardSize == BoardResize.Maximum && page.Id == saved.Id, "VR reopens saved dimensions");
        browser.Handle(default(FrameResult) with
        {
            Commands = [BoardCommand.PageList]
        }, Now());
        painter.Draw(engine, default, new("SYNTHETIC / NOT HMD EVIDENCE"), page: page, vrPages: browser);
        painter.Image.Save(Path.Combine(root, "list-maximum.png"), ImageFormat.Png);
        CheckControlsList(reference, painter.Image);
        browser.ReturnToPage();
        using (var preview = PagePreview.Create(saved, 959, 84))
            preview.Save(Path.Combine(root, "thumbnail-maximum.png"), ImageFormat.Png);
        var reopened = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var nextSession = new MemoSession(reopened, folder, DateTimeOffset.UtcNow, _ =>
        {
        });
        Check(nextSession.Open(nextSession.Catalog.List().Single(), 0) && reopened.BoardSize == saved.BoardSize && reopened.Strokes.Count == saved.Strokes.Count,
            "New PC/VR session reopens the same size and ink");
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.8f, .36f), new BoardSize(.48f, .6f), BoardResize.Maximum })
        {
            var head = new BoardPose(new(0, 1.6f, 0), Quaternion.Identity);
            var placed = VrGeometry.PlaceBoard(head, new AppOptions(), size);
            Check(placed.WorldToLocal(head.Position).Z > 0, "Enlarged recenter faces HMD");
            foreach (float x in new[] { -size.WidthMetres / 2, size.WidthMetres / 2 })
                foreach (float y in new[] { -size.HeightMetres / 2, size.HeightMetres / 2 })
                {
                    var corner = placed.LocalToWorld(new(x, y, 0)) - head.Position;
                    Check(corner.Z < 0 && Math.Abs(MathF.Atan2(corner.X, -corner.Z)) < .8f && Math.Abs(MathF.Atan2(corner.Y, -corner.Z)) < .8f,
                        "All corners within 46 degrees of forward");
                }
        }
        // Exercise texture allocation at all actual sizes, as used by the VR host.
        using (var device = new D3DDevice(0))
            foreach (var size in new[] { BoardSize.Default, new BoardSize(.64f, .44f), BoardResize.Maximum, BoardSize.Default })
            {
                using var image = MemoRenderer.Render(new(2, Guid.NewGuid(), old, old, size, []));
                using var texture = new D3DTexture(device, image.Width, image.Height);
                texture.Upload(image);
            }
        window.Hide();
        Console.WriteLine("PASS: Task 012 constant-size UI/ink pixels, resized PNGs/GPU, save/reopen, VR+PC input exclusion, large page thumbnails and screenshots: " + root);

        void Capture(string name, BoardSize expected)
        {
            Check(engine.BoardSize == expected, "Capture page dimensions");
            painter.Draw(engine, default, new("SYNTHETIC / NOT HMD EVIDENCE"), page: page, vrPages: browser);
            painter.Image.Save(Path.Combine(root, "board-" + name + ".png"), ImageFormat.Png);
            using (var png = MemoRenderer.Render(page.Snapshot()))
                png.Save(Path.Combine(root, "page-" + name + ".png"), ImageFormat.Png);
            SaveScene(painter.Image, painter.ToolsImage, false, false, Path.Combine(root, "scene-" + name + ".png"));
            SaveScene(painter.Image, painter.ToolsImage, true, false, Path.Combine(root, "resize-candidate-" + name + ".png"));
            painter.Draw(engine, default, new("GRIP: MOVE BOARD / SYNTHETIC"), true, page);
            SaveScene(painter.Image, painter.ToolsImage, false, true, Path.Combine(root, "move-candidate-" + name + ".png"));
            painter.Draw(engine, default, new("SYNTHETIC / NOT HMD EVIDENCE"), page: page, vrPages: browser);
            window.RefreshCatalog();
            Application.DoEvents();
            window.Refresh();
            using var desktop = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(desktop, new(Point.Empty, window.Size));
            desktop.Save(Path.Combine(root, "desktop-" + name + ".png"), ImageFormat.Png);
        }
    }

    private static void SaveComparison(Bitmap[] pages, Bitmap tools, string path)
    {
        using var image = new Bitmap(1060, 930);
        using var g = Graphics.FromImage(image);
        using var font = new Font("Segoe UI", 19, FontStyle.Regular, GraphicsUnit.Pixel);
        g.Clear(Color.White);
        string[] names = ["48 x 36 cm", "64 x 44 cm", "80 x 60 cm"];
        for (int i = 0; i < pages.Length; i++)
        {
            int y = i * 310;
            g.DrawString(names[i] + " / toolbar, 3mm ink, palette / original pixels", font, Brushes.Black, 12, y + 4);
            Draw(new(0, 0, 1024, 80), y + 34);
            Draw(new(0, 346, 1024, 30), y + 118);
            g.DrawImage(tools, new Rectangle(12, y + 152, 1024, tools.Height - 91), new Rectangle(0, 91, 1024, tools.Height - 91), GraphicsUnit.Pixel);
            void Draw(Rectangle source, int top) => g.DrawImage(pages[i], new Rectangle(12, top, source.Width, source.Height), source, GraphicsUnit.Pixel);
        }
        image.Save(path, ImageFormat.Png);
    }

    private static void CheckControls(Bitmap standard, Bitmap resized)
    {
        // Only the paper's outer 8px corner follows its resized right edge; the controls
        // (including Close) remain inside the unchanged physical panel.
        Compare(standard, resized, new(0, 0, 1016, 8), Point.Empty);
        Compare(standard, resized, new(0, 8, 1024, 72), new(0, 8));
        // The independent palette texture is compared separately at each size.
    }
    private static void CheckControlsList(Bitmap standard, Bitmap list)
    {
        Check(list.Width == 1707 && list.Height == 1280 && list.GetPixel(25, 180) != standard.GetPixel(25, 180), "Large list rendered in standard physical panel");
    }
    private static void Compare(Bitmap a, Bitmap b, Rectangle region, Point target)
    {
        for (int y = 0; y < region.Height; y++)
            for (int x = 0; x < region.Width; x++)
                Check(a.GetPixel(region.X + x, region.Y + y) == b.GetPixel(target.X + x, target.Y + y), "Toolbar/palette exact pixel dimensions");
    }
    private static void CheckOverlayGeometry()
    {
        var board = new BoardPose(new(.7f, 1.2f, -.4f), Quaternion.CreateFromYawPitchRoll(.7f, -.3f, .2f));
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.64f, .44f), BoardResize.Maximum })
        {
            var dock = BoardPainter.ToolsPose(board, size);
            var bottomLeft = dock.LocalToWorld(new(-BoardSize.Default.WidthMetres / 2,
                -ToolPalette.DockHeightPixels / 2f / MemoRenderer.PixelsPerMetre, 0));
            var expected = board.LocalToWorld(new(-size.WidthMetres / 2,
                size.HeightMetres / 2 + ToolPalette.DockGapPixels / MemoRenderer.PixelsPerMetre, 0));
            Check(Vector3.Distance(bottomLeft, expected) < 1e-6f, "Rotated dock stays above paper top-left with the same gap");
            var handle = new BoardPose(board.LocalToWorld(BoardResize.HandleLocal(size)), board.Rotation);
            var visual = VrOverlaySprites.ResizeVisualPose(handle);
            var arrowCenter = visual.LocalToWorld(new((BoardResize.HandleWidthMetres - VrOverlaySprites.ResizeVisualWidthMetres) / 2, 0, 0));
            Check(Vector3.Distance(arrowCenter, handle.Position) < 1e-6f, "Painted resize arrow center matches actual grab center");
        }
    }

    // Same-size raster compositing of the paper, dock and separate resize overlay.
    private static void SaveScene(Bitmap board, Bitmap tools, bool resize, bool moving, string path)
    {
        int gap = (int)(BoardResize.HandleOffsetMetres * MemoRenderer.PixelsPerMetre);
        int width = (int)(BoardResize.HandleWidthMetres * MemoRenderer.PixelsPerMetre);
        using var image = new Bitmap(board.Width + gap + width, board.Height + gap + width + tools.Height + 8);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.FromArgb(57, 65, 74));
        g.DrawImageUnscaled(board, 0, tools.Height + 8);
        g.DrawImageUnscaled(tools, 0, 0);
        using var handle = VrOverlaySprites.MakeResizeHandle(resize, !moving);
        g.DrawImage(handle, new Rectangle(board.Width + gap - width / 2, tools.Height + 8 + board.Height + gap - width / 2, width, width));
        image.Save(path, ImageFormat.Png);
    }
}
