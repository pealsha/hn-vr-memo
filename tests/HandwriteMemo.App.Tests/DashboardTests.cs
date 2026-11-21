using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class DashboardTests
{
    private static readonly Queue<VREvent_t> s_events = [];

    [TestMethod]
    public void Run()
    {
        for (int i = 0; i < DashboardControls.Count; i++)
        {
            var rect = DashboardControls.Bounds(i);
            Vector2 Mouse(float x, float y) => new(x, DashboardControls.Height - y);
            Check(DashboardControls.HitTest(Mouse(rect.Left, rect.Top)) == i, "visible top-left is active");
            Check(DashboardControls.HitTest(Mouse(rect.Right - .1f, rect.Bottom - .1f)) == i, "visible lower-right is active");
            foreach (var p in new[] { Mouse(rect.Left - .1f, rect.Top), Mouse(rect.Right, rect.Top),
                Mouse(rect.Left, rect.Top - .1f), Mouse(rect.Left, rect.Bottom) })
                Check(DashboardControls.HitTest(p) == -1, "button boundary matches painting");
        }
        foreach (var p in new[] { new Vector2(float.NaN, 100), new(float.PositiveInfinity, 100), new(100, float.NaN), new(100, -1) })
            Check(DashboardControls.HitTest(p) == -1, "invalid position is inert");
        var root = Path.GetFullPath(Path.Combine("artifacts", "dashboard-" + Guid.NewGuid().ToString("N")));
        var options = new AppOptions { SaveFolder = Path.Combine(root, "pages") };
        var host = new AppHost(options, null, false);
        bool Stopped() => host.IsStopped;
        using var log = new RunLog(Path.Combine(root, "logs"));
        var settings = new AppSettingsService(options, log.Event);
        var calibration = new WristCalibrationController(settings, log.Event);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, options.SaveFolder, DateTimeOffset.UtcNow, log.Event);
        using var app = new VrOverlayHost(settings, calibration, engine, page, log, () => 1, host.RequestStop);
        app.InitializeEventProcessing(new FakeVrSystem(), new FakeVrOverlay(s_events, 42), 42, [42]);

        void Send(EVREventType type, float x = 0, float y = 0, uint cursor = 0, uint button = 1)
        {
            var data = type == EVREventType.VREvent_FocusLeave
                ? new VREvent_Data_t { overlay = new VREvent_Overlay_t { cursorIndex = cursor } }
                : new VREvent_Data_t { mouse = new VREvent_Mouse_t { x = x, y = y, cursorIndex = cursor, button = button } };
            s_events.Enqueue(new()
            {
                eventType = (uint)type,
                data = data
            });
            app.PollEvents();
        }
        foreach (float x in new[] { 4f, 150f, 296f, 304f, 450f, 596f, 604f, 750f, 896f })
            foreach (float y in new[] { 10f, 59.9f, 205.1f, 330f })
            {
                Send(EVREventType.VREvent_MouseMove, x, y + DashboardControls.Height - 360);
                Send(EVREventType.VREvent_MouseButtonDown);
                Check(!Stopped() && app.DashboardHoverMask == 0, "review margin/text clicks are inert");
            }
        Send(EVREventType.VREvent_MouseMove, 750, DashboardControls.Height - 227, 1);
        Send(EVREventType.VREvent_MouseMove, 150, DashboardControls.Height - 30, 0);
        Send(EVREventType.VREvent_MouseButtonDown, cursor: 0);
        Check(!Stopped() && app.DashboardHoverMask == 4, "separate cursor positions and hover");
        Send(EVREventType.VREvent_MouseButtonDown, cursor: 1, button: 2);
        Check(!Stopped(), "non-left press is inert");
        Send(EVREventType.VREvent_FocusLeave, cursor: 1);
        Send(EVREventType.VREvent_MouseButtonDown, cursor: 1);
        Check(!Stopped() && app.DashboardHoverMask == 0, "focus leave clears that cursor");
        Send(EVREventType.VREvent_MouseMove, 750, DashboardControls.Height - 227, 1);
        Send(EVREventType.VREvent_OverlayHidden);
        Send(EVREventType.VREvent_MouseButtonDown, cursor: 1);
        Check(!Stopped() && app.DashboardHoverMask == 0, "hidden overlay clears stale positions");
        var random = new Random(48);
        for (int i = 0; i < 120; i++)
        {
            bool start = random.Next(3) != 0;
            var button = DashboardControls.Bounds(start ? 10 : 11);
            Send(EVREventType.VREvent_MouseMove, button.Left + 20, DashboardControls.Height - button.Top - 20);
            Send(EVREventType.VREvent_MouseButtonDown);
            calibration.Update(true, false, null, default, i);
            Check(calibration.IsActive == start, "repeated dashboard clicks cannot turn start into cancel or cancel into start");
        }
        Send(EVREventType.VREvent_MouseMove, 750, DashboardControls.Height - 227, 1);
        Send(EVREventType.VREvent_MouseButtonDown, cursor: 1);
        Check(Stopped(), "visible exit button still requests stop (unstarted test instance only)");
        Console.WriteLine("PASS: dashboard button bounds, actual event dispatch, independent cursors, hover and stale-pointer clearing.");
    }
    [TestMethod]
    public void CancelButtonIsInertWhileHidden()
    {
        var options = new AppOptions { SaveFolder = Path.Combine("artifacts", "dashboard-cancel") };
        using var log = new RunLog(Path.Combine(options.SaveFolder, "logs"));
        var settings = new AppSettingsService(options, log.Event);
        var calibration = new WristCalibrationController(settings, log.Event);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, options.SaveFolder, DateTimeOffset.UnixEpoch, log.Event);
        var events = new Queue<VREvent_t>();
        using var host = new VrOverlayHost(settings, calibration, engine, page, log, () => 1, () => Assert.Fail("Unexpected stop."));
        host.InitializeEventProcessing(new FakeVrSystem(), new FakeVrOverlay(events, 42), 42, [42]);

        var bounds = DashboardControls.Bounds(11);
        Vector2 pointer = new(bounds.Left + 20, DashboardControls.Height - bounds.Top - 20);
        Send(EVREventType.VREvent_MouseMove);
        Assert.AreEqual(-1, DashboardControls.HitTest(pointer, calibrationActive: false));
        Assert.AreEqual(0, host.DashboardHoverMask);
        Send(EVREventType.VREvent_MouseButtonDown);
        Assert.IsFalse(calibration.IsActive);
        Assert.IsNull(calibration.Notice);

        Assert.IsTrue(calibration.Start());
        Assert.AreEqual(1 << 11, host.DashboardHoverMask);
        Send(EVREventType.VREvent_MouseButtonDown);
        Assert.IsFalse(calibration.IsActive);
        Assert.AreEqual(0, host.DashboardHoverMask);

        void Send(EVREventType type)
        {
            events.Enqueue(new()
            {
                eventType = (uint)type,
                data = new VREvent_Data_t { mouse = new VREvent_Mouse_t { x = pointer.X, y = pointer.Y, button = 1 } }
            });
            host.PollEvents();
        }
    }

}
