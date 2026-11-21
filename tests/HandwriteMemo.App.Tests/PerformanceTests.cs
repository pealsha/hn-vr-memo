using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Synthetic measurements keep user pages untouched and make the cache's scaling visible in self-test output.
[STATestClass]
public sealed class PerformanceTests
{
    [TestMethod]
    public void Run()
    {
        const int Frames = 40;
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-023"));
        Directory.CreateDirectory(root);
        VerifyCacheInvalidation();
        var report = new StringBuilder("# Drawing cache measurement\n\n");
        report.AppendLine("Synthetic 1024x768 GDI+ measurements; each existing stroke contains 64 points. Both ink CPU paths reuse one bitmap and raster only paper, grid, and ink; toolbar/overlay UI is excluded in the ink-only columns. Full Draw includes toolbar/dock UI. GPU upload remains a full board texture upload in both cases. Reciprocal timing is capacity, not an observed VR loop rate.");
        foreach (int count in new[] { 300, 1000 })
        {
            var engine = MakeEngine(count);
            using var painter = new BoardPainter();
            using var legacy = new Bitmap(BoardPainter.Width, BoardPainter.Height, PixelFormat.Format32bppArgb);
            painter.DrawInkForMeasurement(engine); // populate completed ink before timing
            Timing oldDraw = Measure(Frames, () => DrawLegacy(legacy, engine));
            Timing cachedInk = Measure(Frames, () => painter.DrawInkForMeasurement(engine));
            Timing fullDraw = Measure(Frames, () =>
            {
                _ = engine.MinimumBoardSize;
                painter.Draw(engine, default, new("PERFORMANCE SYNTHETIC"));
            });
            Timing upload;
            using (var device = new D3DDevice(0))
            using (var texture = new D3DTexture(device, painter.Image.Width, painter.Image.Height))
                upload = Measure(Frames, () => texture.Upload(painter.Image));
            painter.Image.Save(Path.Combine(root, $"performance-{count}-strokes.png"), ImageFormat.Png);
            report.AppendLine($"- {count} strokes: legacy ink redraw mean/p95 {oldDraw.Mean:F3}/{oldDraw.P95:F3} ms; cached ink mean/p95 {cachedInk.Mean:F3}/{cachedInk.P95:F3} ms; full BoardPainter.Draw plus MinimumBoardSize mean/p95 {fullDraw.Mean:F3}/{fullDraw.P95:F3} ms (CPU-only capacity {1000 / fullDraw.Mean:F1} Hz); D3D11 upload mean/p95 {upload.Mean:F3}/{upload.P95:F3} ms.");
            var live = MeasureLiveAppend(count);
            report.AppendLine($"  - while appending a 64-point live stroke over that page: BoardPainter.Draw plus MinimumBoardSize CPU mean/p95 {live.Cpu.Mean:F3}/{live.Cpu.P95:F3} ms; CPU plus D3D11 upload mean/p95 {live.WithUpload.Mean:F3}/{live.WithUpload.P95:F3} ms (CPU+upload reciprocal mean {1000 / live.WithUpload.Mean:F1} Hz). Save/synchronization I/O is excluded.");
        }
        File.WriteAllText(Path.Combine(root, "performance.md"), report.ToString(), new UTF8Encoding(false));
        Console.WriteLine(report.ToString());
    }

    private static HandwriteEngine MakeEngine(int count)
    {
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        var strokes = Enumerable.Range(0, count).Select(i => new MemoStroke(DateTimeOffset.UnixEpoch,
            Enumerable.Range(0, 64).Select(j => new MemoPoint(
                (.04f + .90f * j / 63) * .48f,
                (.20f + (i % 100) * .007f + .006f * MathF.Sin(j * .4f + i)) * .36f, j * .01)).ToArray())).ToArray();
        engine.LoadPage(new MemoDocument(2, Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), BoardSize.Default, strokes), 0);
        return engine;
    }

    private readonly record struct Timing(double Mean, double P95);
    private readonly record struct LiveTiming(Timing Cpu, Timing WithUpload);

    private static LiveTiming MeasureLiveAppend(int completedStrokeCount)
    {
        var engine = MakeEngine(completedStrokeCount);
        using var painter = new BoardPainter();
        painter.Draw(engine, default, new("LIVE PERFORMANCE SYNTHETIC"));
        double seconds = completedStrokeCount * .05 + 1;
        int point = 0;
        engine.UpdatePagePoint(new(.12f, .55f), false, seconds += .01);
        engine.UpdatePagePoint(new(.12f, .55f), true, seconds += .01);
        Timing cpu = Measure(64, () =>
        {
            var uv = LivePoint(point++);
            engine.UpdatePagePoint(uv, true, seconds += .01);
            _ = engine.MinimumBoardSize;
            painter.Draw(engine, default, new("LIVE PERFORMANCE SYNTHETIC"));
        });
        using var device = new D3DDevice(0);
        using var texture = new D3DTexture(device, painter.Image.Width, painter.Image.Height);
        Timing withUpload = Measure(64, () =>
        {
            var uv = LivePoint(point++);
            engine.UpdatePagePoint(uv, true, seconds += .01);
            _ = engine.MinimumBoardSize;
            painter.Draw(engine, default, new("LIVE PERFORMANCE SYNTHETIC"));
            texture.Upload(painter.Image);
        });
        engine.UpdatePagePoint(LivePoint(point), false, seconds += .01);
        return new(cpu, withUpload);

        static Vector2 LivePoint(int index) => new(.12f + (index % 64) * .006f, .55f + ((index / 64) % 2) * .01f);
    }

    private static Timing Measure(int frames, Action action)
    {
        action();
        var samples = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            var clock = Stopwatch.StartNew();
            action();
            samples[i] = clock.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        return new(samples.Average(), samples[(int)Math.Ceiling(samples.Length * .95) - 1]);
    }

    private static void VerifyCacheInvalidation()
    {
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        using var cached = new BoardPainter();
        double seconds = 0;
        void DrawAndCompare(string operation)
        {
            cached.Draw(engine, default, new("CACHE CHECK"));
            using var fresh = new BoardPainter();
            fresh.Draw(engine, default, new("CACHE CHECK"));
            if (!SimilarPixels(cached.Image, fresh.Image))
                throw new InvalidOperationException("Ink cache differs after " + operation);
        }
        engine.UpdatePagePoint(new(.25f, .5f), false, seconds += .01);
        engine.UpdatePagePoint(new(.25f, .5f), true, seconds += .01);
        DrawAndCompare("stroke start");
        engine.UpdatePagePoint(new(.45f, .55f), true, seconds += .01);
        DrawAndCompare("live segment");
        engine.UpdatePagePoint(new(.6f, .58f), true, seconds += .01);
        engine.UpdatePagePoint(new(.65f, .60f), true, seconds += .01);
        engine.UpdatePagePoint(new(.45f, .55f), false, seconds += .01);
        DrawAndCompare("stroke finish");
        // Deliberately complete two lines without an intervening paint call, as desktop events can do.
        engine.UpdatePagePoint(new(.35f, .65f), true, seconds += .01);
        engine.UpdatePagePoint(new(.55f, .70f), false, seconds += .01);
        engine.UpdatePagePoint(new(.55f, .68f), true, seconds += .01);
        engine.UpdatePagePoint(new(.65f, .72f), true, seconds += .01);
        engine.UpdatePagePoint(new(.65f, .72f), false, seconds += .01);
        DrawAndCompare("two completed strokes between paints");
        if (!engine.Undo())
            throw new InvalidOperationException("Cache invalidation setup undo failed");
        DrawAndCompare("undo");
        if (!engine.Redo())
            throw new InvalidOperationException("Cache invalidation setup redo failed");
        DrawAndCompare("redo");
        engine.SetEraserMode(EraserMode.Partial);
        engine.UpdatePagePoint(new(.45f, .55f), true, seconds += .01, DrawingTool.Eraser);
        engine.UpdatePagePoint(new(.45f, .55f), false, seconds += .01, DrawingTool.Eraser);
        DrawAndCompare("partial erase");
        engine.ResizeBoard(new(.64f, .44f));
        DrawAndCompare("resize");
        var page = MemoDocument.Capture(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, engine.BoardSize, engine.Strokes);
        engine.LoadPage(page, seconds += .01);
        DrawAndCompare("page load");
    }

    private static bool SimilarPixels(Bitmap first, Bitmap second)
    {
        if (first.Size != second.Size)
            return false;
        var rectangle = new Rectangle(Point.Empty, first.Size);
        var a = first.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var b = second.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(a.Stride) * first.Height;
            var firstBytes = new byte[bytes];
            var secondBytes = new byte[bytes];
            Marshal.Copy(a.Scan0, firstBytes, 0, bytes);
            Marshal.Copy(b.Scan0, secondBytes, 0, bytes);
            int different = 0;
            for (int i = 0; i < bytes; i += 4)
            {
                if (firstBytes.AsSpan(i, 4).SequenceEqual(secondBytes.AsSpan(i, 4)))
                    continue;
                if (++different > 256)
                    return false; // live anti-alias joins may differ by a few edge pixels.
            }
            return true;
        }
        finally
        {
            first.UnlockBits(a);
            second.UnlockBits(b);
        }
    }

    private static void DrawLegacy(Bitmap image, HandwriteEngine engine)
    {
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(UiTheme.Surface);
        using var grid = new Pen(UiTheme.Grid);
        for (int x = 32; x < image.Width; x += 32)
            g.DrawLine(grid, x, 0f, x, image.Height);
        for (int y = 128; y < image.Height; y += 32)
            g.DrawLine(grid, 0, y, image.Width, y);
        foreach (var stroke in engine.Strokes)
        {
            var points = stroke.Points.Select(p => new PointF(p.Uv.X * image.Width, p.Uv.Y * image.Height)).ToArray();
            MemoRenderer.DrawStroke(g, points, stroke.Color, stroke.WidthMetres / engine.BoardSize.WidthMetres * image.Width);
        }
    }
}
