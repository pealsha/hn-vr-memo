using System.Drawing.Imaging;
using System.Numerics;
using System.Text.Json;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class ThumbnailTests
{
    private static readonly DateTimeOffset s_created = new(2026, 10, 18, 3, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Run()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "task-011"));
        var folder = Path.Combine(root, "pages");
        var store = new MemoStore(folder);
        var words = Word(.205f, .18f, .0006f).ToArray();
        MemoDocument[] documents =
        [
            new(2, Guid.Parse("11000000-0000-0000-0000-000000000001"), s_created, s_created.AddMinutes(1), BoardSize.Default, words, true),
            new(2, Guid.Parse("11000000-0000-0000-0000-000000000002"), s_created.AddDays(-1), s_created.AddMinutes(1), BoardSize.Default,
                Japanese(.08f, .065f).Concat(Word(.22f, .2f, .0015f, "#1866C2")).ToArray()),
            new(2, Guid.Parse("11000000-0000-0000-0000-000000000003"), s_created.AddDays(-2), s_created.AddMinutes(1), BoardSize.Default, []),
        ];
        foreach (var doc in documents)
            store.Save(doc, s => MemoRenderer.WritePng(doc, s));
        VerifyRendering(root, documents);
        var originalFiles = Directory.GetFiles(folder).ToDictionary(path => path, File.ReadAllBytes);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, folder, DateTimeOffset.UtcNow, _ =>
        {
        });
        using var window = new DesktopWindow(engine, page, () => 0);
        window.Show();
        Application.DoEvents();
        window.Refresh();
        using (var desktop = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(desktop, new Rectangle(Point.Empty, window.Size));
            desktop.Save(Path.Combine(root, "after-desktop.png"), ImageFormat.Png);
        }
        using var browser = new VrPageBrowser(page);
        browser.Handle(default(FrameResult) with
        {
            Commands = [BoardCommand.PageList]
        }, 0);
        using var painter = new BoardPainter();
        painter.Draw(engine, default, new("SYNTHETIC / NOT HMD EVIDENCE"), page: page, vrPages: browser);
        painter.Image.Save(Path.Combine(root, "after-vr.png"), ImageFormat.Png);
        Check(browser.IsOpen && browser.Entries.Count == 3, "Capture must show the real VR list");
        Check(originalFiles.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "List display must leave saved files unchanged");
        Console.WriteLine("PASS: whole-page crop, sparse ink, empty/dot/edges, fine colors, aspect ratio, v1 read-only and desktop/VR list captures: " + root);
    }

    private static void VerifyRendering(string root, MemoDocument[] documents)
    {
        foreach (var size in new[] { new Size(266, 128), new Size(959, 84) })
        {
            using var sparse = PagePreview.Create(documents[0], size.Width, size.Height);
            var word = Pixels(sparse, Dark);
            Check(word.Width > 150 && word.Height > 45, "Sparse word must remain large and dark in both list sizes");
            using var combined = PagePreview.Create(documents[1], size.Width, size.Height);
            var upper = Pixels(combined, Dark);
            var lower = Pixels(combined, c => c.B > c.R + 60 && c.B > c.G + 30);
            Check(upper.Width > 5 && upper.Height > 5 && lower.Width > 5 && lower.Height > 5,
                "Ink near the top edge and in the rest of the page must both remain visible");
            Check(upper.Right < lower.Left && upper.Bottom < lower.Top,
                "All ink must keep its original relative positions in desktop and VR previews");
            using var blank = PagePreview.Create(documents[2], size.Width, size.Height);
            Check(Pixels(blank, Dark).IsEmpty && !Pixels(blank, c => c.R < 200).IsEmpty,
                "Blank page must have an empty-page hint without dark ink");
            using var japanese = PagePreview.Create(Doc(Japanese(.2f, .18f)), size.Width, size.Height);
            Check(Pixels(japanese, Dark).Width > 100, "Untitled Japanese ink must also be enlarged");
            using var upperOnly = PagePreview.Create(Doc(Japanese(.08f, .065f)), size.Width, size.Height);
            Check(Pixels(upperOnly, Dark).Width > 100, "Ink near the top must use the available preview area");

            var square = Doc([Line("#182634", .0006f, new(.2f,.18f,0), new(.24f,.18f,1),
                new(.24f,.22f,2), new(.2f,.22f,3), new(.2f,.18f,4))]);
            using var squareImage = PagePreview.Create(square, size.Width, size.Height);
            var squareBounds = Pixels(squareImage, Dark);
            Check(Math.Abs(squareBounds.Width - squareBounds.Height) <= 2, "Physical ink aspect ratio must be preserved");
            var crossing = Doc([Line("#182634", .0006f, new(.2f,.09f,0), new(.24f,.09f,1),
                new(.24f,.13f,2), new(.2f,.13f,3), new(.2f,.09f,4))]);
            using var crossingImage = PagePreview.Create(crossing, size.Width, size.Height);
            var crossingBounds = Pixels(crossingImage, Dark);
            Check(Math.Abs(crossingBounds.Width - crossingBounds.Height) <= 2,
                "A square spanning the top and body of the page must remain one square, without splitting or reflow");
            var expanded = new MemoDocument(2, Guid.NewGuid(), s_created, s_created.AddMinutes(1), BoardResize.Maximum,
                [Line("#182634", .0015f, new(.6f, .07f, 0), new(.7f, .07f, 1))]);
            using var expandedImage = PagePreview.Create(expanded, size.Width, size.Height);
            Check(Pixels(expandedImage, Dark).Width > 100, "Ink in the expanded area must use the same whole-page crop");
            foreach (var color in ToolPalette.Colors)
            {
                using var fine = PagePreview.Create(Doc([Line(color, .0001f, new(.01f, .18f, 0), new(.47f, .18f, 1))]), size.Width, size.Height);
                var expected = ColorTranslator.FromHtml(color);
                Check(Pixels(fine, c => Math.Abs(c.R - expected.R) < 70 && Math.Abs(c.G - expected.G) < 70 && Math.Abs(c.B - expected.B) < 70).Width > size.Width * .7f,
                    "A fine line spanning the page must stay visible in its original color");
            }
            foreach (var points in new MemoPoint[][]
            {
                [new(.24f,.18f,0)], // One dot is ink, not an empty page.
                [new(.24f,.12f,0), new(.24f,.3f,1)], // Vertical, no zero-width crop.
                [new(-.1f,.18f,0), new(.6f,.18f,1)], // Crosses page with both endpoints outside.
                [new(.24f,.03f,0), new(.24f,.12f,1)], // Sparse ink spanning the top and body of the page.
                [new(0,.18f,0)], // Dot centred on page edge; keep its visible half.
            })
            {
                using var image = PagePreview.Create(Doc([Line("#182634", .0015f, points)]), size.Width, size.Height);
                Check(!Pixels(image, Dark).IsEmpty, "Dot and crossing/edge strokes must not disappear");
            }
            using var outside = PagePreview.Create(Doc([Line("#182634", .0015f, new MemoPoint(-.1f, .18f, 0))]), size.Width, size.Height);
            Check(Pixels(outside, Dark).IsEmpty, "Fully off-page ink must not become visible in the preview");
            using var reentry = PagePreview.Create(Doc([Line("#182634", .0015f, new(.20f, .12f, 0),
                new(.20f, -.08f, 1), new(.28f, -.08f, 2), new(.28f, .12f, 3))]), size.Width, size.Height);
            Check(Pixels(reentry, Dark).Width > 20 &&
                Enumerable.Range(0, size.Height).All(y => !Dark(reentry.GetPixel(size.Width / 2, y))),
                "A stroke that leaves the page and re-enters must keep the visible runs disconnected");
            using var noPoints = PagePreview.Create(Doc([Line("#182634", .0015f)]), size.Width, size.Height);
            Check(Pixels(noPoints, Dark).IsEmpty, "Empty stroke records must be treated as empty");
        }

        // Exercise the real v1 reader; previewing must neither migrate the source nor alter the saved PNG rendering.
        var legacyFolder = Path.Combine(root, "legacy");
        Directory.CreateDirectory(legacyFolder);
        var legacyPath = Path.Combine(legacyFolder, "legacy.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            id = Guid.Parse("11000000-0000-0000-0000-000000000004"),
            createdUtc = s_created,
            updatedUtc = s_created.AddMinutes(1),
            boardWidthMetres = .48f,
            boardHeightMetres = .36f,
            strokes = documents[0].Strokes.Select(s => new
            {
                startedUtc = s.StartedUtc,
                points = s.Points.Select(p => new { u = p.XMetres / .48f, v = p.YMetres / .36f, elapsedSeconds = p.ElapsedSeconds })
            }),
        });
        File.WriteAllBytes(legacyPath, bytes);
        var legacy = new MemoStore(legacyFolder).Read(legacyPath);
        using var beforePng = new MemoryStream();
        MemoRenderer.WritePng(legacy, beforePng);
        using var preview = PagePreview.Create(legacy, 266, 128);
        Check(Pixels(preview, Dark).Width > 150, "Migrated v1 ink must be enlarged too");
        using var afterPng = new MemoryStream();
        MemoRenderer.WritePng(legacy, afterPng);
        Check(File.ReadAllBytes(legacyPath).SequenceEqual(bytes) && beforePng.ToArray().SequenceEqual(afterPng.ToArray()),
            "Preview must leave v1 source bytes and full-page PNG unchanged");
    }

    private static bool Dark(Color c) => c.R < 90 && c.G < 110 && c.B < 130;
    private static Rectangle Pixels(Bitmap image, Func<Color, bool> matches)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (matches(image.GetPixel(x, y)))
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
    private static MemoStroke Line(string color, float width, params MemoPoint[] points) => new(s_created, points, color, width);
    private static MemoDocument Doc(IEnumerable<MemoStroke> strokes) => new(2, Guid.NewGuid(), s_created, s_created.AddMinutes(1), BoardSize.Default, strokes.ToArray());
    private static IEnumerable<MemoStroke> Word(float x, float y, float width, string color = "#182634")
    {
        // A small handwritten "test" near the centre, occupying about 15% of the page width.
        float[][] paths = [[0,0, 0,1, .3f,1], [-.25f,.3f, .4f,.3f],
            [.65f,.55f, 1.2f,.55f, 1.2f,.3f, .85f,.3f, .65f,.5f, .65f,.9f, .9f,1, 1.2f,.9f],
            [2,.35f, 1.55f,.3f, 1.4f,.5f, 2,.75f, 2, .95f, 1.4f,1],
            [2.55f,0, 2.55f,1, 2.85f,1], [2.3f,.3f, 2.95f,.3f]];
        return Paths(paths, x, y, .022f, width, color);
    }

    private static IEnumerable<MemoStroke> Japanese(float x, float y) => Paths(
        [[.8f,0, .55f,.55f, 0,1], [.05f,.2f, .9f,.85f],
         [1.25f,.1f, 2.3f,.1f], [1.1f,.5f, 2.4f,.5f], [1.6f,.1f, 1.6f,.95f, 2.3f,.95f]],
        x, y, .022f, .0015f, "#182634");

    private static IEnumerable<MemoStroke> Paths(float[][] paths, float x, float y, float scale, float width, string color) =>
        paths.Select(path => new MemoStroke(s_created,
            Enumerable.Range(0, path.Length / 2).Select(i => new MemoPoint(x + path[i * 2] * scale, y + path[i * 2 + 1] * scale, i)).ToArray(), color, width));
}
