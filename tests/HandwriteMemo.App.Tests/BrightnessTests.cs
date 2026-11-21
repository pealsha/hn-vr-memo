using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BrightnessTests
{
    private static readonly Queue<VREvent_t> s_events = [];
    private static readonly List<(ulong Handle, float R, float G, float B)> s_colors = [];

    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-024", "automated"));
        Directory.CreateDirectory(root);
        string config = Path.Combine(root, "settings.json");
        File.WriteAllText(config, "{}");
        Check(AppOptions.Load(config).BoardBrightness == 1, "legacy config defaults to 100%");
        foreach (string value in new[] { "0.59", "1.01", "-1" })
        {
            File.WriteAllText(config, "{\"BoardBrightness\":" + value + "}");
            try
            {
                AppOptions.Load(config);
                throw new InvalidOperationException("Invalid brightness accepted");
            }
            catch (ArgumentException)
            {
            }
        }
        var initial = new AppOptions
        {
            SaveFolder = Path.Combine(root, "pages"),
            Handedness = HandSide.Left,
            RightIndexTipOffset = new(.012f, -.07f, -.035f)
        };
        initial.Save(config);
        using var log = new RunLog(Path.Combine(root, "logs"));
        var settings = new AppSettingsService(initial, log.Event, config);
        var calibration = new WristCalibrationController(settings, log.Event);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, initial.SaveFolder, DateTimeOffset.UnixEpoch, _ =>
        {
        });
        page.DesktopPoint(new(.3f, .4f), false, .01, DrawingTool.Pen);
        page.DesktopPoint(new(.3f, .4f), true, .02, DrawingTool.Pen);
        page.DesktopPoint(new(.6f, .7f), true, .03, DrawingTool.Pen);
        page.DesktopPoint(new(.6f, .7f), false, .04, DrawingTool.Pen);
        // Paper, dock, arrow, body faces, tips, dashboard/tab and wrist button.
        var handles = Enumerable.Range(10, 24).Select(i => (ulong)i).Append(42UL).ToList();
        using var app = new VrOverlayHost(settings, calibration, engine, page, log, () => 1, () => { });
        app.InitializeEventProcessing(new FakeVrSystem(), new FakeVrOverlay(s_events, 42)
        {
            ColorChanged = (handle, r, g, b) => s_colors.Add((handle, r, g, b))
        }, 42, handles);
        byte[] Png()
        {
            using var stream = new MemoryStream();
            MemoRenderer.WritePng(page.Snapshot(), stream);
            return stream.ToArray();
        }
        using var desktop = new DesktopWindow(engine, page, () => 1);
        desktop.ClientSize = new(1164, 801);
        _ = desktop.Handle;
        desktop.PerformLayout();
        using var beforeDesktop = new Bitmap(desktop.Canvas.Width, desktop.Canvas.Height);
        desktop.Canvas.DrawToBitmap(beforeDesktop, new(Point.Empty, beforeDesktop.Size));
        byte[] png = Png();
        long revision = page.Revision;
        Guid id = page.Id;
        s_colors.Clear();
        settings.ChangeBrightness(initial.BoardBrightness);
        CheckColors(1);
        void Click(int index)
        {
            var rect = DashboardControls.Bounds(index);
            s_events.Enqueue(new()
            {
                eventType = (uint)EVREventType.VREvent_MouseMove,
                data = new()
                {
                    mouse = new()
                    {
                        cursorIndex = 1,
                        x = rect.Left + rect.Width / 2,
                        y = DashboardControls.Height - rect.Top - rect.Height / 2
                    }
                }
            });
            s_events.Enqueue(new()
            {
                eventType = (uint)EVREventType.VREvent_MouseButtonDown,
                data = new()
                {
                    mouse = new()
                    {
                        cursorIndex = 1,
                        button = 1
                    }
                }
            });
            app.PollEvents();
        }
        s_colors.Clear();
        Click(4);
        CheckColors(.8f);
        Check(settings.Current.BoardBrightness == .8f && AppOptions.Load(config).BoardBrightness == .8f, "dashboard saves 80%");
        Check(AppOptions.Load(config).Handedness == HandSide.Left && AppOptions.Load(config).RightIndexTipOffset == initial.RightIndexTipOffset,
            "brightness preserves handedness and calibration");
        s_colors.Clear();
        Click(5);
        CheckColors(.6f);
        Check(settings.Current.BoardBrightness == .6f && AppOptions.Load(config).BoardBrightness == .6f, "dashboard saves 60%");
        CheckUnchangedContent();
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            s_colors.Clear();
            Click(3);
            Check(s_colors.Count == 0 && settings.Current.BoardBrightness == .6f && AppOptions.Load(config).BoardBrightness == .6f,
                "failed dashboard save retains both display and stored value");
            Check(settings.Notice is string, "VR receives settings failure message");
        }
        s_colors.Clear();
        Click(3);
        CheckColors(1);
        Check(AppOptions.Load(config).BoardBrightness == 1 && settings.Notice is null,
            "retry after unlocking config recovers and clears failure");
        CheckUnchangedContent();
        s_events.Clear();
        s_colors.Clear();
        Console.WriteLine("PASS: Task 024 legacy/validated brightness, actual dashboard dispatch, RGB calls on every overlay, settings lock failure/retry, PNG and PC pixel invariance.");

        void CheckColors(float value)
        {
            Check(s_colors.Count == handles.Count && s_colors.Select(c => c.Handle).Distinct().Count() == handles.Count, "every overlay receives RGB once");
            Check(s_colors.All(c => c.B == value && c.R == value && c.G == value),
                "all overlay colors use neutral RGB multiplication");
        }

        void CheckUnchangedContent()
        {
            Check(Png().SequenceEqual(png) && page.Revision == revision && page.Id == id, "brightness leaves PNG, page and ink revision unchanged");
            using var afterDesktop = new Bitmap(beforeDesktop.Width, beforeDesktop.Height);
            desktop.Canvas.DrawToBitmap(afterDesktop, new(Point.Empty, afterDesktop.Size));
            using var beforeBytes = new MemoryStream();
            using var afterBytes = new MemoryStream();
            beforeDesktop.Save(beforeBytes, System.Drawing.Imaging.ImageFormat.Png);
            afterDesktop.Save(afterBytes, System.Drawing.Imaging.ImageFormat.Png);
            Check(beforeBytes.ToArray().SequenceEqual(afterBytes.ToArray()), "brightness leaves desktop canvas pixels unchanged");
        }
    }
}
