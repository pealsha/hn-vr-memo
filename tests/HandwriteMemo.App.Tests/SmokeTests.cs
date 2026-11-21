using System.Diagnostics;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HandwriteMemo.Core;
using Vortice.Direct3D11;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void RenderingAndGpu()
    {
        Directory.CreateDirectory("artifacts");
        // A nontrivial transform catches accidental row/column transposes in the actual OpenVR glue.
        var pose = new BoardPose(new Vector3(.7f, 1.2f, -.4f), Quaternion.CreateFromYawPitchRoll(.7f, -.3f, .2f));
        var roundTrip = VrGeometry.FromOpenVr(VrGeometry.ToOpenVr(pose));
        var testPoint = new Vector3(.14f, -.07f, .02f);
        if (Vector3.Distance(roundTrip.LocalToWorld(testPoint), pose.LocalToWorld(testPoint)) > 1e-5f)
            throw new InvalidOperationException("OpenVR transform round-trip failed.");
        var placed = VrGeometry.PlaceBoard(new BoardPose(Vector3.Zero, Quaternion.Identity), new AppOptions());
        if (placed.Position.Z >= 0 || placed.WorldToLocal(Vector3.Zero).Z <= 0)
            throw new InvalidOperationException("Board must be in front of and face the HMD.");
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        double time = 0;
        FrameResult frame = default;
        void Stroke(params Vector2[] points)
        {
            foreach (var (point, pressed) in points.Select(p => (p, true)).Prepend((points[0], false)).Append((points[^1], false)))
            {
                frame = engine.UpdateProjectedTip(new TipSample(new Vector3((point.X - .5f) * .48f, (.5f - point.Y) * .36f, .05f), true), pressed, .15f, time += .012);
            }
        }
        // Synthetic x² + y² = 1, used only to inspect rendering, not presented as a VR writing result.
        Stroke(new(.14f, .45f), new(.23f, .62f));
        Stroke(new(.23f, .45f), new(.14f, .62f));
        Stroke(new(.25f, .38f), new(.27f, .36f), new(.29f, .38f), new(.25f, .43f), new(.30f, .43f));
        Stroke(new(.34f, .53f), new(.42f, .53f));
        Stroke(new(.38f, .49f), new(.38f, .57f));
        Stroke(new(.47f, .45f), new(.51f, .53f), new(.56f, .45f));
        Stroke(new(.51f, .53f), new(.48f, .65f));
        Stroke(new(.58f, .38f), new(.60f, .36f), new(.62f, .38f), new(.58f, .43f), new(.63f, .43f));
        Stroke(new(.68f, .50f), new(.77f, .50f));
        Stroke(new(.68f, .56f), new(.77f, .56f));
        Stroke(new(.84f, .47f), new(.88f, .44f), new(.88f, .62f));
        using var painter = new BoardPainter();
        painter.Draw(engine, frame, new("SYNTHETIC TEST / NOT VR EVIDENCE"));
        painter.Image.Save("artifacts/smoke-board.png", ImageFormat.Png);
        painter.Draw(engine, frame, new("IN REACH / SYNTHETIC TEST"), true);
        painter.Image.Save("artifacts/smoke-board-highlight.png", ImageFormat.Png);
        painter.Draw(engine, frame, new("SYNTHETIC TEST / NOT VR EVIDENCE"));
        using var dashboard = VrOverlaySprites.MakeDashboard();
        dashboard.Save("artifacts/smoke-dashboard.png", ImageFormat.Png);
        using var pen = PenPainter.MakePen();
        pen.Save("artifacts/smoke-pen.png", ImageFormat.Png);
        using var highlightedPen = PenPainter.MakePen(true);
        highlightedPen.Save("artifacts/smoke-pen-highlight.png", ImageFormat.Png);
        if (Vector3.Distance(PenPainter.FacePose(pose, 4).Position, pose.LocalToWorld(GrabScene.PenTipLocal)) > 1e-5f)
            throw new InvalidOperationException("Pen end face does not match drawing tip.");
        _ = AppOptions.Load(Path.Combine(AppContext.BaseDirectory, "settings.example.json"));
        using var device = new D3DDevice(0);
        using var texture = new D3DTexture(device, BoardPainter.Width, BoardPainter.Height);
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
            texture.Upload(painter.Image);
        Console.WriteLine($"GPU upload: 1000 updates, {elapsed.Elapsed.TotalMilliseconds / 1000:F3} ms/update. This excludes OpenVR/compositor/HMD latency.");
        var description = texture.Texture.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = device.Device.CreateTexture2D(description);
        device.Context.CopyResource(staging, texture.Texture);
        var mapped = device.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            // Check distinct R/B + alpha values, not just a white pixel: catches channel swaps.
            Color expected = painter.Image.GetPixel(2, 2);
            var pixel = new byte[4];
            Marshal.Copy(mapped.DataPointer + (int)mapped.RowPitch * 2 + 8, pixel, 0, 4);
            if (!pixel.SequenceEqual(new byte[] { expected.B, expected.G, expected.R, expected.A }))
                throw new InvalidOperationException("GPU readback color or alpha mismatch.");
        }
        finally
        {
            device.Context.Unmap(staging, 0);
        }
        Console.WriteLine("PASS: OpenVR matrix conversion, board placement, synthetic ink rendering, D3D11 repeated upload/readback BGRA. No SteamVR session was started.");
    }

    [TestMethod]
    public void CheckHandLandmarks()
    {
        var options = new AppOptions { LeftPalmOffset = new(.02f, -.01f, .03f) };
        var grip = new BoardPose(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        var palm = HandLandmarks.Palm(grip, options.LeftPalmOffset);
        if (Vector3.Distance(palm.Position, new Vector3(1.01f, 2.02f, 3.03f)) > 1e-5f ||
            Vector3.Distance(HandLandmarks.FingerTip(palm, options), palm.LocalToWorld(options.RightIndexTipOffset.Vector)) > 1e-5f)
            throw new InvalidOperationException("Palm and gesture landmarks must share the rotated grip reference frame.");
        foreach (var controller in new[] { "oculus_touch", "knuckles", "vive_controller" })
        {
            using var binding = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "input", $"bindings_{controller}.json")));
            var poses = binding.RootElement.GetProperty("bindings").GetProperty("/actions/memo").GetProperty("poses");
            foreach (var hand in new[] { "left", "right" })
                if (!poses.EnumerateArray().Any(p => p.GetProperty("path").GetString() == $"/user/hand/{hand}/pose/grip" &&
                    p.GetProperty("output").GetString() == "/actions/memo/in/handpose"))
                    throw new InvalidOperationException($"Missing {controller}/{hand} grip-pose binding.");
        }
        Console.WriteLine("PASS: common grip frame for palm/finger/wrist and both-hand grip pose bindings.");
    }

    [TestMethod]
    public void CheckGestureOptions()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "gesture-config-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        AppOptions Read(string json)
        {
            File.WriteAllText(path, json);
            return AppOptions.Load(path);
        }

        var previous = Read("""
            {"LeftWristOffset":null,"ShowHandMarkers":true,"HeadTapOffset":null,"WristGestureEnabled":false,"GestureDwellSeconds":0.8,"UnknownKey":12}
            """);
        if (previous.GestureMode != ToggleGestureMode.WristButton)
            throw new InvalidOperationException("Unknown settings keys changed the default gesture.");
        previous.Save(path);
        using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var settings = saved.RootElement;
            foreach (string key in new[] { "LeftWristOffset", "ShowHandMarkers", "HeadTapOffset", "WristGestureEnabled", "GestureDwellSeconds", "UnknownKey" })
                if (settings.TryGetProperty(key, out _))
                    throw new InvalidOperationException("Obsolete key was saved: " + key);
        }

        foreach (var (jsonValue, name, expected) in new[]
        {
            ("\"WristButton\"", "WristButton", ToggleGestureMode.WristButton),
            ("\"WristDoubleTap\"", "WristButton", ToggleGestureMode.WristButton),
            ("\"HeadDoubleTap\"", "WristButton", ToggleGestureMode.WristButton),
            ("\"BackOfHand\"", "WristButton", ToggleGestureMode.WristButton),
            ("0", "WristButton", ToggleGestureMode.WristButton),
            ("1", "WristButton", ToggleGestureMode.WristButton),
            ("3", "WristButton", ToggleGestureMode.WristButton),
            ("\"Disabled\"", "Disabled", ToggleGestureMode.Disabled),
            ("2", "Disabled", ToggleGestureMode.Disabled)
        })
        {
            var options = Read("{\"GestureMode\":" + jsonValue + "}");
            options.Save(path);
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            if (options.GestureMode != expected || saved.RootElement.GetProperty("GestureMode").GetString() != name)
                throw new InvalidOperationException("Gesture mode was not normalized: " + jsonValue);
        }

        foreach (string invalid in new[] { "\"Typo\"", "\"2\"", "-1", "4", "2147483648", "1.5", "null", "true" })
        {
            try
            {
                Read("{\"GestureMode\":" + invalid + "}");
                throw new InvalidOperationException("Invalid gesture mode accepted: " + invalid);
            }
            catch (JsonException)
            {
            }
        }
    }

    [TestMethod]
    public void CheckLogRotation()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "log-rotation-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var when = DateTime.UtcNow.AddDays(-2);
        string[] oldLogs = Enumerable.Range(0, 25).Select(index => Path.Combine(folder, $"memo-test-{index:D2}.log")).ToArray();
        foreach (var (path, index) in oldLogs.Select((path, index) => (path, index)))
        {
            File.WriteAllText(path, "earlier log");
            File.SetLastWriteTimeUtc(path, when.AddMinutes(index));
        }
        var otherFile = Path.Combine(folder, "notes.log");
        File.WriteAllText(otherFile, "keep");

        using var log = new RunLog(folder);
        var retained = Directory.GetFiles(folder, "memo-*.log");
        if (retained.Length != 20 || oldLogs.Take(6).Any(File.Exists) || !oldLogs.Skip(6).All(File.Exists) ||
            File.ReadAllText(otherFile) != "keep")
            throw new InvalidOperationException("Log rotation did not retain the current log and the 19 latest previous logs.");
    }

    [TestMethod]
    public void CheckVersionTwoRendering()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "v2-render-" + Guid.NewGuid().ToString("N")));
        var store = new MemoStore(folder);
        var uv = new[] { new Vector2(.125f, .25f), new Vector2(.5f, .625f), new Vector2(.875f, .375f) };
        var dotUv = new Vector2(.625f, .75f);
        // Independent v1 compatibility reference, including its fixed pixel width.
        using var reference = new Bitmap(BoardPainter.Width, BoardPainter.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(reference))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(248, 247, 242));
            using var pen = new Pen(Color.FromArgb(24, 38, 52), 3.2f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            };
            g.DrawLines(pen, uv.Select(p => new PointF(p.X * reference.Width, p.Y * reference.Height)).ToArray());
            using var brush = new SolidBrush(pen.Color);
            g.FillEllipse(brush, dotUv.X * reference.Width - 1.6f, dotUv.Y * reference.Height - 1.6f, 3.2f, 3.2f);
        }
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.72f, .24f) })
        {
            var origin = DateTimeOffset.UnixEpoch;
            object StrokeWire(Vector2[] points) => new
            {
                startedUtc = origin.AddSeconds(1),
                points = points.Select((p, i) => new { u = p.X, v = p.Y, elapsedSeconds = (double)i + 1 }).ToArray()
            };
            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                formatVersion = 1,
                id = Guid.NewGuid(),
                createdUtc = origin,
                updatedUtc = origin.AddSeconds(4),
                boardWidthMetres = size.WidthMetres,
                boardHeightMetres = size.HeightMetres,
                strokes = new[] { StrokeWire(uv), StrokeWire([dotUv]) }
            }));
            var converted = store.Read(path);
            var files = store.Save(converted, stream => MemoRenderer.WritePng(converted, stream));
            using var actual = new Bitmap(files.PngPath);
            // Export at constant physical density: nonstandard v1 boards change pixel
            // dimensions while retaining their original physical ink and page size.
            using var physicalReference = new Bitmap(actual.Width, actual.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(physicalReference))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(248, 247, 242));
                float width = 3.2f * actual.Width / reference.Width;
                using var pen = new Pen(Color.FromArgb(24, 38, 52), width)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                };
                g.DrawLines(pen, uv.Select(p => new PointF(p.X * actual.Width, p.Y * actual.Height)).ToArray());
                using var brush = new SolidBrush(pen.Color);
                g.FillEllipse(brush, dotUv.X * actual.Width - width / 2, dotUv.Y * actual.Height - width / 2, width, width);
            }
            int differentPixels = 0;
            int maxDifference = 0;
            for (int y = 0; y < actual.Height; y++)
                for (int x = 0; x < actual.Width; x++)
                {
                    var a = physicalReference.GetPixel(x, y);
                    var b = actual.GetPixel(x, y);
                    // Nonstandard dimensions have a different floating-point/raster rounding path.
                    int tolerance = size == BoardSize.Default ? 0 : 1;
                    maxDifference = Math.Max(maxDifference, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                    if (Math.Abs(a.R - b.R) > tolerance || Math.Abs(a.G - b.G) > tolerance || Math.Abs(a.B - b.B) > tolerance)
                        differentPixels++;
                }
            // GDI+ rounds subpixel end caps differently after the metre-coordinate roundtrip.
            // Standard pages stay pixel-identical; other sizes allow at most four antialias edge pixels.
            if (differentPixels > (size == BoardSize.Default ? 0 : 4) || maxDifference > (size == BoardSize.Default ? 0 : 32))
                throw new InvalidOperationException($"v1 to v2 PNG appearance changed at {differentPixels} pixels for {size}.");
        }
        // Check v2 style and finite off-board points, while retaining the fixed full-page canvas.
        var time = DateTimeOffset.UnixEpoch;
        var styled = new MemoDocument(2, Guid.NewGuid(), time, time, BoardSize.Default,
            [new MemoStroke(time, [new(-.1f, .18f, 0), new(.6f, .18f, 0)], "#FF0000", .006f)]);
        using var output = new MemoryStream();
        MemoRenderer.WritePng(styled, output);
        output.Position = 0;
        using var styledImage = new Bitmap(output);
        if (styledImage.GetPixel(512, 384).ToArgb() != Color.Red.ToArgb() ||
            styledImage.GetPixel(512, 389).ToArgb() != Color.Red.ToArgb() ||
            styledImage.Width != 1024 || styledImage.Height != 768)
            throw new InvalidOperationException("Stored style or off-board line rendering failed.");
        reference.Save(Path.Combine(folder, "legacy-reference.png"), ImageFormat.Png);
        styledImage.Save(Path.Combine(folder, "v2-style-and-outside.png"), ImageFormat.Png);
        Console.WriteLine("PASS: migrated v1 physical ink at two page sizes and constant PNG density; v2 style and off-board clipping.");
    }

    [TestMethod]
    public void CheckSaving()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "save-test-" + Guid.NewGuid().ToString("N")));
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        var origin = DateTimeOffset.UtcNow;
        var session = new MemoSession(engine, folder, origin, Console.WriteLine);
        var frame = engine.UpdateProjectedTip(new TipSample(new Vector3(0, 0, .05f), true), true, .15f, 1);
        session.Observe(frame, 1);
        var store = new MemoStore(folder);
        var first = Directory.GetFiles(folder, "*.json").Single();
        var snapshot = store.Read(first);
        if (snapshot.FormatVersion != 2 || snapshot.Strokes[0].Color != "#182634" ||
            MathF.Abs(snapshot.Strokes[0].WidthMetres - .0015f) > 1e-7f ||
            Vector2.Distance(new(snapshot.Strokes[0].Points[0].XMetres, snapshot.Strokes[0].Points[0].YMetres), new(.24f, .18f)) > 1e-6f)
            throw new InvalidOperationException("New app pages must save v2 physical coordinates and the existing ink style.");
        if (store.Read(first).Strokes.Count != 1 || !File.Exists(Path.ChangeExtension(first, ".png")))
            throw new InvalidOperationException("Active stroke was not automatically saved.");
        frame = engine.UpdateProjectedTip(new TipSample(new Vector3(.03f, 0, .05f), true), true, .15f, 2.1);
        session.Observe(frame, 2.1);
        if (store.Read(first).Strokes[0].Points.Count != 2)
            throw new InvalidOperationException("Ongoing stroke checkpoint missing.");
        if (!session.NewPage(2.2) || engine.Strokes.Count != 0 || store.Read(first).Strokes.Count != 1)
            throw new InvalidOperationException("New page must retain the previous page.");
        engine.UpdateProjectedTip(new TipSample(new Vector3(0, 0, .05f), true), false, .15f, 2.3);
        frame = engine.UpdateProjectedTip(new TipSample(new Vector3(.1f, 0, .05f), true), true, .15f, 3.3);
        session.Observe(frame, 3.3);
        if (Directory.GetFiles(folder, "*.json").Length != 2)
            throw new InvalidOperationException("Second page must use another filename.");
        // Lock the destination to emulate an inaccessible disk file. New page must fail closed.
        var second = Directory.GetFiles(folder, "*.json").Single(p => p != first);
        using (var locked = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            frame = engine.UpdateProjectedTip(new TipSample(new Vector3(.12f, 0, .05f), true), true, .15f, 4.4);
            session.Observe(frame, 4.4);
            if (session.NewPage(4.5) || engine.Strokes.Count != 1 || session.Error is null)
                throw new InvalidOperationException("Save failure must retain the current page.");
        }
        if (!session.Save(4.6) || session.Error is not null)
            throw new InvalidOperationException("Saving must recover when destination becomes writable.");
        using var png = Image.FromFile(Path.ChangeExtension(first, ".png"));
        if (png.Width != BoardPainter.Width || png.Height != BoardPainter.Height)
            throw new InvalidOperationException("Saved PNG dimensions invalid.");
        Console.WriteLine("PASS: autosave active stroke, periodic checkpoint, new page retention, save failure protection/retry, PNG decoding.");
    }

    [TestMethod]
    public void CheckPenTools()
    {
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        double seconds = 0;
        for (int color = 0; color < ToolPalette.Colors.Count; color++)
            for (int width = 0; width < ToolPalette.Widths.Count; width++)
            {
                engine.SetPenStyle(ToolPalette.Colors[color], ToolPalette.Widths[width]);
                float x = (80 + 256 * color) / 1024f, y = (256 + 128 * width) / 768f;
                engine.UpdatePagePoint(new(x, y), false, seconds += .01);
                engine.UpdatePagePoint(new(x, y), true, seconds += .01);
                engine.UpdatePagePoint(new(x + .125f, y), true, seconds += .01);
                engine.UpdatePagePoint(new(x + .125f, y), false, seconds += .01);
            }
        var doc = MemoDocument.Capture(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, engine.BoardSize, engine.Strokes);
        using var output = new MemoryStream();
        MemoRenderer.WritePng(doc, output);
        output.Position = 0;
        using var png = new Bitmap(output);
        using var board = new BoardPainter();
        board.Draw(engine, new FrameResult(ContactState.None, null, [], [], false), new("4 COLORS / 3 WIDTHS / SYNTHETIC"));
        for (int color = 0; color < ToolPalette.Colors.Count; color++)
        {
            int previous = 0;
            for (int width = 0; width < ToolPalette.Widths.Count; width++)
            {
                int x = 144 + 256 * color, y = 256 + 128 * width;
                int expected = ColorTranslator.FromHtml(ToolPalette.Colors[color]).ToArgb();
                int pngWidth = Enumerable.Range(y - 8, 17).Count(row => png.GetPixel(x, row).ToArgb() == expected);
                int boardWidth = Enumerable.Range(y - 8, 17).Count(row => board.Image.GetPixel(x, row).ToArgb() == expected);
                if (png.GetPixel(x, y).ToArgb() != expected || board.Image.GetPixel(x, y).ToArgb() != expected ||
                    pngWidth <= previous || boardWidth != pngWidth)
                    throw new InvalidOperationException("Live board/PNG color or three distinct widths disagree.");
                previous = pngWidth;
            }
        }
        board.Image.Save("artifacts/pen-tools-board.png", ImageFormat.Png);
        png.Save("artifacts/pen-tools-export.png", ImageFormat.Png);
        var cursor = engine.UpdatePagePoint(new(.5f, .65f), false, seconds += .01, DrawingTool.Eraser);
        board.Draw(engine, cursor, new("ERASER RANGE / SYNTHETIC"));
        board.Image.Save("artifacts/pen-tools-eraser-cursor.png", ImageFormat.Png);
        for (int face = 0; face < EraserPainter.FaceCount; face++)
        {
            using var image = EraserPainter.MakeFace(face);
            image.Save($"artifacts/eraser-face-{face}.png", ImageFormat.Png);
            if (Math.Abs(image.Width / (float)image.Height - EraserPainter.FaceWidthMetres(face) / EraserPainter.FaceHeightMetres(face)) > .001)
                throw new InvalidOperationException("Eraser texture aspect ratio disagrees with physical face.");
        }
        using var selected = EraserPainter.MakeFace(0, true);
        selected.Save("artifacts/eraser-highlight.png", ImageFormat.Png);
        var pose = new BoardPose(new Vector3(1, 2, 3), Quaternion.CreateFromYawPitchRoll(.3f, -.7f, .4f));
        if (Vector3.Distance(EraserPainter.FacePose(pose, 4).Position, pose.LocalToWorld(GrabScene.EraserTipLocal)) > 1e-6f)
            throw new InvalidOperationException("Eraser top face does not match the projected tip.");
        Console.WriteLine("PASS: all 4 colors x 3 widths on live board and PNG; eraser face geometry and cursor preview.");
    }

    [TestMethod]
    public void CheckEraserSaving()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "eraser-save-" + Guid.NewGuid().ToString("N")));
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        var origin = DateTimeOffset.UtcNow;
        var session = new MemoSession(engine, folder, origin, Console.WriteLine);
        double seconds = 0;
        void Input(float x, float y, bool down = false, DrawingTool tool = DrawingTool.Pen)
        {
            var frame = engine.UpdatePagePoint(new(x, y), down, seconds += .1, tool);
            session.Observe(frame, seconds);
        }
        engine.SetPenStyle(ToolPalette.Colors[1], ToolPalette.Widths[2]);
        Input(.1f, .5f, true);
        Input(.9f, .5f, true);
        Input(.9f, .5f);
        var store = new MemoStore(folder);
        var path = Directory.GetFiles(folder, "*.json").Single();
        var original = store.Read(path);
        engine.SetEraserMode(EraserMode.Partial);
        Input(.5f, .5f, false, DrawingTool.Eraser);
        Input(.5f, .5f, true, DrawingTool.Eraser);
        Input(.5f, .5f, false, DrawingTool.Eraser);
        var partial = store.Read(path);
        if (partial.Strokes.Count != 2 || partial.Strokes.Any(s => s.Color != ToolPalette.Colors[1] || s.WidthMetres != .003f))
            throw new InvalidOperationException("Partial erasure or fragment style was not automatically saved.");
        var undoBounds = BoardControls.ButtonBounds(0, false);
        float undoX = undoBounds.Left + undoBounds.Width / 2, undoY = undoBounds.Top + undoBounds.Height / 2;
        Input(undoX, undoY, true, DrawingTool.Eraser);
        Input(undoX, undoY, false, DrawingTool.Eraser);
        if (store.Read(path).Strokes.Count != 1)
            throw new InvalidOperationException("Eraser undo was not saved.");
        engine.SetEraserMode(EraserMode.Stroke);
        Input(.5f, .5f, true, DrawingTool.Eraser);
        Input(.5f, .5f, false, DrawingTool.Eraser);
        if (store.Read(path).Strokes.Count != 0)
            throw new InvalidOperationException("Erasing the last ink must save the empty page.");
        using (var empty = new Bitmap(Path.ChangeExtension(path, ".png")))
            if (empty.GetPixel(512, 384).ToArgb() != Color.FromArgb(248, 247, 242).ToArgb())
                throw new InvalidOperationException("Erased ink remains in PNG.");
        Input(undoX, undoY, true, DrawingTool.Eraser);
        Input(undoX, undoY, false, DrawingTool.Eraser);
        var restored = store.Read(path);
        if (!restored.Strokes[0].Points.SequenceEqual(original.Strokes[0].Points) || restored.Strokes[0].Color != original.Strokes[0].Color)
            throw new InvalidOperationException("Saved undo did not restore the exact original.");
        if (File.ReadAllText(path).Contains("worldNames"))
            throw new InvalidOperationException("App saved retired worldNames.");
        Console.WriteLine("PASS: partial/whole erase autosave, empty-page PNG, exact undo restoration and app JSON without worldNames.");
    }
}
