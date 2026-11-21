using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class VrFeedbackTests
{
    [TestMethod]
    public void Calibration()
    {
        string root = Path.GetFullPath("artifacts/task-037");
        string run = Path.Combine(root, "integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string config = Path.Combine(run, "settings.json");
        var options = new AppOptions { SaveFolder = Path.Combine(run, "pages") };
        File.WriteAllText(config, "{}");
        Check(AppOptions.Load(config).CurrentWristTapCentre == WristTapGesture.ContactCentre, "old config uses measured default");
        options.Save(config);
        using var log = new RunLog(Path.Combine(run, "logs"));
        var app = new AppSettingsService(options, log.Event, config);
        var calibration = new WristCalibrationController(app, log.Event);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, options.SaveFolder, DateTimeOffset.UnixEpoch, log.Event);
        using var vr = new VrOverlayHost(app, calibration, engine, page, log, () => 1, () => { });
        using var settings = new DesktopSettingsWindow(app, new(app, log.Event), calibration, page, () => 1, _ => false, _ => { }, () => null);
        settings.Show();
        Application.DoEvents();
        Check(!calibration.CanCalibrate && !settings.CalibrateButton.Enabled, "calibration is initially unavailable");
        var connections = new List<bool>();
        vr.ConnectionChanged += connections.Add;
        vr.InitializeEventProcessing(new FakeVrSystem(), new FakeVrOverlay(), 42, [42]);
        Check(vr.IsConnected && calibration.CanCalibrate && settings.CalibrateButton.Enabled, "connection enables calibration on the open settings window");
        Capture(settings, Path.Combine(root, "settings.png"));
        settings.CalibrateButton.PerformClick();
        Check(calibration.IsActive && settings.CalibrateButton.Text.Contains("中止"), "actual PC button starts and offers cancel");
        settings.CalibrateButton.PerformClick();
        Check(!calibration.IsActive, "actual PC button cancels");
        double time = 0;
        var grip = new BoardPose(new(.2f, 1.2f, -.5f), Quaternion.CreateFromYawPitchRoll(.4f, -.3f, .2f));
        void Sample(Vector3? local, bool dashboard = false)
        {
            time += 1d / 90;
            calibration.Update(dashboard, local is not null, grip, grip.LocalToWorld(local ?? Vector3.Zero), time);
            Check(settings.CalibrationStatusText == calibration.Guidance, "PC and VR show the same instruction on every transition");
        }
        void Hold(Vector3 point, double duration)
        {
            for (int i = 0; i < Math.Ceiling(duration * 90); i++)
                Sample(point);
        }
        foreach (var dominant in Enum.GetValues<HandSide>())
        {
            Check(app.ChangeHandedness(dominant), "change handedness");
            Check(calibration.Start(), "start calibration");
            var target = new Vector3(.11f, .08f, -.025f);
            var raw = dominant == HandSide.Left ? target with { X = -target.X } : target;
            for (int i = 0; i < 450; i++) Sample(raw, dashboard: true);
            Check(calibration.IsActive && app.Current.CurrentWristTapCentre == WristTapGesture.ContactCentre,
                "dashboard wait does not save a premature position");
            Hold(raw, WristCalibration.PreparationSeconds + 1);
            for (int i = 0; i < (WristCalibration.TimeoutSeconds + 1) * 90; i++) Sample(null, dashboard: true);
            Check(calibration.IsActive && settings.CalibrationStatusText.Contains("閉じる"),
                "reopening the menu suspends sampling and tells both screens how to continue");
            Hold(raw, WristCalibration.PreparationSeconds + 1);
            Check(calibration.IsActive, "samples from before opening the menu cannot complete the new measurement");
            Hold(raw, WristCalibration.PreparationSeconds + WristCalibration.CollectionSeconds + .5);
            Check(!calibration.IsActive && calibration.Notice!.Contains("保存しました"), "stationary target confirms");
            Check(Vector3.Distance(AppOptions.Load(config).CurrentWristTapCentre, target) < .0001f, "position persists in grip-local coordinates");
            var before = app.Current.CurrentWristTapCentre;
            Check(calibration.Start(), "restart for save failure");
            using (var locked = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.None))
                Hold(raw + Vector3.UnitY * .04f, WristCalibration.PreparationSeconds + WristCalibration.CollectionSeconds + .5);
            Check(calibration.Notice!.Contains("保存できません") && app.Current.CurrentWristTapCentre == before,
                "failed persistence keeps previous runtime centre");
            Check(AppOptions.Load(config).CurrentWristTapCentre == before, "failed persistence keeps previous file");
            Check(app.Notice is null, "a calibration failure cannot leave a second retry message on the dashboard");
            Check(calibration.Start(), "restart after failure");
            Hold(raw, WristCalibration.PreparationSeconds + .3);
            Sample(raw + Vector3.UnitX * (WristCalibration.MaximumMovementMetres * 3));
            Check(calibration.Notice!.Contains("もう一度"), "movement explicitly requests retry");
            Hold(raw, .25);
            Check(calibration.IsActive && calibration.Notice!.Contains("やり直しています"),
                "retry feedback remains readable during the next collection, beyond one frame");
            Hold(raw, WristCalibration.CollectionSeconds + .4);
            Check(calibration.Notice!.Contains("保存しました"), "stationary retry recovers");
            Check(calibration.Start(), "start tracking timeout");
            for (int i = 0; i < Math.Ceiling((WristCalibration.TimeoutSeconds + 1) * 90); i++) Sample(null);
            Check(!calibration.IsActive && calibration.Notice!.Contains("保存していません"), "tracking loss terminates with recovery advice");
        }
        Check(app.Current.WristTapCentreLeftHanded is not null && app.Current.WristTapCentreRightHanded is not null, "both hands retain separate positions");
        vr.Dispose();
        Check(!vr.IsConnected && !settings.CalibrateButton.Enabled && connections.SequenceEqual([true, false]),
            "disconnect disables calibration on the open settings window");
        settings.Close();
    }

    [TestMethod]
    public void QuietGuidance()
    {
        string root = Path.GetFullPath("artifacts/task-037");
        Directory.CreateDirectory(root);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, Path.Combine(root, "quiet-" + Guid.NewGuid().ToString("N")), DateTimeOffset.UnixEpoch, _ => { });
        var board = new BoardPose(Vector3.Zero, Quaternion.Identity);
        var scene = new GrabScene(board, BoardResize.PenDock(board, engine.BoardSize));
        var held = new HandInput(scene.PenPose, true, false, false, true);
        var far = new HandInput(new(new(5), Quaternion.Identity), true, false, false, true);
        scene.Update(new(held, far, true, false));
        held = held with { GripDown = true };
        var result = scene.Update(new(held, far, true, false));
        engine.RequireTriggerRelease();
        var status = VrInputGuide.Status(engine, page, scene, result, held, far, false, true);
        Check(page.VrNeedsTriggerRelease && status.Text.Length == 0 && !status.ShowInDock,
            "trigger release guard remains active with no duplicated board/dock guidance");
        using var painter = new BoardPainter();
        painter.Draw(engine, default, status, page: page);
        painter.Image.Save(Path.Combine(root, "board-release-guard.png"), ImageFormat.Png);
        painter.ToolsImage.Save(Path.Combine(root, "dock-release-guard.png"), ImageFormat.Png);
        foreach (int index in new[] { 0, 1 })
        {
            var bounds = BoardControls.ButtonBounds(index, false);
            var frame = default(FrameResult) with { CursorUv = new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2) };
            painter.Draw(engine, frame, status, page: page);
            painter.Image.Save(Path.Combine(root, index == 0 ? "board-undo-hover.png" : "board-redo-hover.png"), ImageFormat.Png);
        }
        using var dashboard = VrOverlaySprites.MakeDashboard();
        dashboard.Save(Path.Combine(root, "dashboard.png"), ImageFormat.Png);
    }

    private static void Capture(Control control, string path)
    {
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new(Point.Empty, image.Size));
        image.Save(path, ImageFormat.Png);
    }
}
