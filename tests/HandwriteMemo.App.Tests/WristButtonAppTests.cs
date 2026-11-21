using System.Drawing.Imaging;
using System.Numerics;
using System.Text.Json;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class WristButtonAppTests
{
    private static double s_wristSeconds = 20;
    private static readonly Queue<VREvent_t> s_events = [];
    private static InputDigitalActionData_t s_digital;
    private static EVRInputError s_error;

    private sealed class WristInput : FakeVrInput
    {
        public override EVRInputError GetDigitalActionData(ulong handle, ref InputDigitalActionData_t data, uint size, ulong source)
        {
            data = s_digital;
            return s_error;
        }

        public override EVRInputError TriggerHapticVibrationAction(ulong action, float start, float duration, float frequency, float amplitude, ulong source) =>
            Haptic(action, start, duration, frequency, amplitude, source);
    }

    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath("artifacts/task-036");
        Directory.CreateDirectory(root);
        string run = Path.Combine(root, "synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string config = Path.Combine(run, "settings.json");
        var options = new AppOptions { SaveFolder = Path.Combine(run, "pages") };
        Check(options.GestureMode == ToggleGestureMode.WristButton, "new installation defaults to wrist tap");
        foreach (string mode in new[] { "WristDoubleTap", "BackOfHand", "HeadDoubleTap", "Disabled", "WristButton" })
        {
            File.WriteAllText(config, JsonSerializer.Serialize(new
            {
                GestureMode = mode,
                SaveFolder = options.SaveFolder
            }));
            var saved = AppOptions.Load(config);
            Check(saved.GestureMode == (mode == "Disabled" ? ToggleGestureMode.Disabled : ToggleGestureMode.WristButton) &&
                saved.WristButtonActivationMode == WristButtonActivationMode.DoubleTap, "old and new named modes remain readable");
        }
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            File.WriteAllText(config, JsonSerializer.Serialize(new
            {
                GestureMode = mode,
                SaveFolder = options.SaveFolder
            }));
            Check(AppOptions.Load(config).GestureMode == (mode == 2 ? ToggleGestureMode.Disabled : ToggleGestureMode.WristButton), "legacy numeric values are normalized");
        }
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            WristButtonActivationMode = 99,
            SaveFolder = options.SaveFolder
        }));
        try
        {
            AppOptions.Load(config);
            throw new InvalidOperationException("Invalid activation accepted");
        }
        catch (ArgumentException)
        {
        }
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            SaveFolder = options.SaveFolder
        }));
        Check(AppOptions.Load(config).GestureMode == ToggleGestureMode.WristButton, "old file without mode uses wrist button");
        foreach (string activation in new[] { "DoubleTap", "Touch", "0", "1" })
        {
            string value = char.IsDigit(activation[0]) ? activation : JsonSerializer.Serialize(activation);
            File.WriteAllText(config, "{\"SaveFolder\":" + JsonSerializer.Serialize(options.SaveFolder) + ",\"WristButtonActivationMode\":" + value + "}");
            Check((int)AppOptions.Load(config).WristButtonActivationMode == (activation is "Touch" or "1" ? 1 : 0), "legacy activation stays readable");
        }
        options.Save(config);
        using var log = new RunLog(Path.Combine(run, "logs"));
        var preferences = new AppSettingsService(options, log.Event, config);
        var calibration = new WristCalibrationController(preferences, log.Event);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, options.SaveFolder, DateTimeOffset.UnixEpoch, log.Event);
        using var app = new VrOverlayHost(preferences, calibration, engine, page, log, () => s_wristSeconds, () => { });
        app.InitializeEventProcessing(new FakeVrSystem(), new FakeVrOverlay(s_events, 42), 42, [42]);
        app.SetHeadPose(new()
        {
            bDeviceIsConnected = true,
            bPoseIsValid = true,
            eTrackingResult = ETrackingResult.Running_OK,
            mDeviceToAbsoluteTracking = VrGeometry.ToOpenVr(new(new(0, 1.5f, 0), Quaternion.Identity))
        });
        using var settings = new DesktopSettingsWindow(preferences, new(preferences, log.Event), calibration, page, () => 1, _ => false, _ =>
        {
        }, () => null);
        settings.Show();
        Application.DoEvents();
        Capture(settings, Path.Combine(root, "settings-wrist.png"));
        void ClickDashboard(int index)
        {
            var bounds = DashboardControls.Bounds(index);
            s_events.Enqueue(new()
            {
                eventType = (uint)EVREventType.VREvent_MouseMove,
                data = new()
                {
                    mouse = new()
                    {
                        x = bounds.Left + bounds.Width / 2,
                        y = DashboardControls.Height - bounds.Top - bounds.Height / 2
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
                        button = 1
                    }
                }
            });
            app.PollEvents();
        }
        foreach (int index in new[] { 7, 6, 9, 8 })
        {
            ClickDashboard(index);
            var saved = AppOptions.Load(config);
            Check(saved == preferences.Current && settings.GestureChoice.SelectedIndex == (saved.GestureMode == ToggleGestureMode.WristButton ? 0 : 1) &&
                settings.ActivationChoice.SelectedIndex == (int)saved.WristButtonActivationMode, "dashboard click persists and synchronizes PC controls");
        }
        foreach (var mode in new[] { ToggleGestureMode.WristButton, ToggleGestureMode.Disabled })
            foreach (var activation in new[] { WristButtonActivationMode.DoubleTap, WristButtonActivationMode.Touch })
                foreach (var hand in new[] { HandSide.Left, HandSide.Right })
                {
                    using var dashboard = VrOverlaySprites.MakeDashboard(0, mode, false, hand, activationMode: activation);
                    dashboard.Save(Path.Combine(root, $"dashboard-{mode}-{activation}-{hand}.png"), ImageFormat.Png);
                }
        settings.ActivationChoice.SelectedIndex = 1;
        Check(AppOptions.Load(config).WristButtonActivationMode == WristButtonActivationMode.Touch, "PC selection persists");
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            settings.ActivationChoice.SelectedIndex = 0;
            Check(preferences.Current.WristButtonActivationMode == WristButtonActivationMode.Touch && settings.ErrorText.Length > 0, "failed save retains setting and reports recovery");
        }
        settings.ActivationChoice.SelectedIndex = 0;
        Check(AppOptions.Load(config).WristButtonActivationMode == WristButtonActivationMode.DoubleTap && settings.ErrorText.Length == 0, "settings retry recovers");
        Check(!preferences.Current.WristTapHapticsEnabled && settings.HapticsChoice.SelectedIndex == 0, "old config defaults to vibration off");
        Check(settings.HapticsChoice.Parent is not null && settings.HapticsChoice.Visible, "haptic option is attached and visible");
        settings.HapticsChoice.SelectedIndex = 1;
        Check(AppOptions.Load(config).WristTapHapticsEnabled, "haptic setting persists and reloads");
        Capture(settings, Path.Combine(root, "settings-haptics-on.png"));
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            settings.HapticsChoice.SelectedIndex = 0;
            Check(preferences.Current.WristTapHapticsEnabled && settings.ErrorText.Length > 0, "failed haptic save keeps old choice");
        }
        settings.HapticsChoice.SelectedIndex = 0;
        Check(!AppOptions.Load(config).WristTapHapticsEnabled && settings.ErrorText.Length == 0, "haptic save retry recovers");
        Capture(settings, Path.Combine(root, "settings-wrist.png"));
        foreach (bool confirmed in new[] { false, true })
        {
            using var cue = VrOverlaySprites.MakeWristCue(confirmed);
            cue.Save(Path.Combine(root, confirmed ? "cue-confirmed.png" : "cue-pending.png"), ImageFormat.Png);
        }
        preferences.ChangeWristButtonActivationMode(WristButtonActivationMode.Touch);
        CheckWristGates(app, preferences);
        CheckWristBoardToggles(app, preferences);
        CheckOptionalAction(app, preferences, log, options);
        CheckHaptics(log, options);
        CheckManifests();
        CheckHapticManifest();
        settings.Close();
        File.WriteAllText(Path.Combine(root, "synthetic-results.txt"), "PASS: legacy config, settings/dashboard click persistence and failure recovery, both-hand gesture gates and held objects, optional native input and real board toggles, unbound manifests.\nNo SteamVR session, registration, user settings, or user memo files accessed.\n");
        Console.WriteLine("PASS: Task 034 wrist settings/dashboard/images, synthetic gesture integration and optional SteamVR input/board toggle (no SteamVR session).");
    }

    private sealed class WristSequence(VrOverlayHost app, AppSettingsService preferences, HandSide dominant)
    {
        internal readonly BoardPose Head = new(new(0, 1.5f, 0), Quaternion.Identity);
        internal readonly HandSide Side = dominant == HandSide.Right ? HandSide.Left : HandSide.Right;
        internal WristTapState Step(bool contact = false, int gate = 0, double duration = .12)
        {
            var wrist = new BoardPose(new(0, 1.35f, -.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                Side == HandSide.Left ? -MathF.PI / 2 : MathF.PI / 2));
            var local = contact ? new Vector3(.139f, .191f, -.021f) : new Vector3(.32f, .17f, -.02f);
            if (Side == HandSide.Right)
                local.X = -local.X;
            var pointer = wrist.LocalToWorld(local);
            var pointerPalm = new BoardPose(pointer - preferences.Current.RightIndexTipOffset.Vector, Quaternion.Identity);
            var w = new HandInput(wrist, true, gate == 8, gate == 4, true);
            var p = new HandInput(pointerPalm, gate != 2, gate == 3, gate == 4, gate != 5);
            WristTapState result = default;
            for (int i = 0; i < Math.Ceiling(duration * 90); i++)
            {
                s_wristSeconds += 1d / 90;
                result = app.UpdateWristTap(Head, gate != 6, Side == HandSide.Left ? w : p,
                    Side == HandSide.Left ? p : w, gate == 7 ? null : wrist, gate == 7 ? null : wrist, gate == 1, s_wristSeconds);
                app.LogWristDiagnostics(result, s_wristSeconds);
            }
            return result;
        }
        internal WristTapState Tap(int gate = 0) { Step(true, gate); return Step(gate: gate); }
        internal void Ready() => Step(duration: 1.2);
    }

    private static void CheckWristGates(VrOverlayHost app, AppSettingsService preferences)
    {
        foreach (var dominant in Enum.GetValues<HandSide>())
        {
            preferences.ChangeHandedness(dominant);
            var s = new WristSequence(app, preferences, dominant);
            s.Ready();
            Check(s.Tap(gate: 4).Confirmed, "pressed triggers on both hands do not block a completed single tap");
            foreach (int gate in new[] { 1, 2, 3, 5, 6, 7, 8 })
            {
                s.Ready();
                Check(!s.Step(true, gate).Visible, "dashboard/tracking/either grip/buttons/head/pose blocks cue");
                Check(!s.Step(true, duration: 1).Visible, "recovery under resting finger cannot trigger");
                s.Ready();
                Check(s.Tap().Confirmed, "release and fresh tap recover after block");
            }
            var scene = app.Scene;
            foreach (var held in new[] { Grabbable.Pen, Grabbable.Board, Grabbable.Eraser })
            {
                var pose = held == Grabbable.Pen ? scene.PenPose : held == Grabbable.Eraser ? scene.EraserPose : scene.BoardPose;
                var far = new HandInput(new(new(10), Quaternion.Identity), true, false, false, true);
                scene.Update(new(new(pose, true, false, false, true), far, true, false));
                var grabbed = scene.Update(new(new(pose, true, true, false, true), far, true, false));
                Check((held == Grabbable.Pen ? grabbed.Pen.Holder : held == Grabbable.Eraser ? grabbed.Eraser.Holder : grabbed.BoardHolder) is not null,
                    "fixture holds the intended object");
                Check(!s.Step(true).Visible && !s.Tap().Triggered, "held objects block taps");
                scene.Update(new(default, default, false, false));
            }
        }
    }

    private static void CheckWristBoardToggles(VrOverlayHost app, AppSettingsService preferences)
    {
        foreach (var dominant in Enum.GetValues<HandSide>())
            foreach (var mode in Enum.GetValues<WristButtonActivationMode>())
            {
                preferences.ChangeHandedness(dominant);
                preferences.ChangeWristButtonActivationMode(mode);
                var s = new WristSequence(app, preferences, dominant);
                for (int action = 0; action < 2; action++)
                {
                    s.Ready();
                    bool before = app.BoardVisible;
                    Check(!s.Step().Visible, "look alone never reveals a target");
                    s.Step(true);
                    Check(app.BoardVisible == before, "contact alone does not change board visibility");
                    var firstRelease = s.Step();
                    if (mode == WristButtonActivationMode.DoubleTap)
                    {
                        Check(firstRelease.Pending && app.BoardVisible == before, "first tap waits with cue");
                        Check(s.Tap().Confirmed, "second release confirms");
                    }
                    else
                        Check(firstRelease.Confirmed, "one completed tap confirms in single mode");
                    Check(app.BoardVisible != before, "synthetic tap changes actual board visibility");
                    s.Step(true, duration: 1.5);
                    Check(app.BoardVisible != before, "resting finger never repeats");
                }
                var cue = VrWristInput.CuePose(s.Head,
                    new(new(0, 1.35f, -.4f), Quaternion.Identity), s.Side);
                var normal = Vector3.Transform(Vector3.UnitZ, cue.Rotation);
                Check(Vector3.Dot(normal, Vector3.Normalize(s.Head.Position - cue.Position)) > .99f, "cue faces head");
                foreach (var cuePosition in new[] { s.Head.Position, s.Head.Position - Vector3.UnitY * .3f })
                {
                    var offset = WristTapGesture.ContactCentre;
                    if (s.Side == HandSide.Right)
                        offset.X = -offset.X;
                    var edgeCue = VrWristInput.CuePose(s.Head, new(cuePosition - offset, Quaternion.Identity), s.Side);
                    Check(float.IsFinite(edgeCue.Rotation.LengthSquared()) && float.IsFinite(edgeCue.Position.LengthSquared()),
                        "cue near the head or parallel to head-up remains finite");
                }
            }
        Console.WriteLine("PASS: Task 034 synthetic wrist poses open and close the actual board, both hands and both activation modes.");
    }

    private static readonly List<(ulong Handle, float Duration, float Frequency, float Amplitude, ulong Source)> s_haptics = [];
    private static EVRInputError Haptic(ulong handle, float delay, float duration, float frequency, float amplitude, ulong source)
    {
        Check(delay == 0, "haptic is immediate");
        s_haptics.Add((handle, duration, frequency, amplitude, source));
        return s_error;
    }
    private static void CheckHaptics(RunLog log, AppOptions options)
    {
        using var input = new SteamVrInput(new WristInput(), log, options, 0, 88, 11, 12);
        s_haptics.Clear();
        foreach (var dominant in Enum.GetValues<HandSide>())
        {
            var pending = new WristTapState(true, false, false, true, WristTapEvent.CueStarted, null);
            var confirmed = new WristTapState(true, true, true, false, WristTapEvent.Confirmed, null);
            VrWristInput.SendFeedback(input, pending, dominant, true);
            VrWristInput.SendFeedback(input, pending with { Events = WristTapEvent.None }, dominant, true);
            VrWristInput.SendFeedback(input, confirmed, dominant, true);
            ulong expectedSource = dominant == HandSide.Right ? 11ul : 12ul;
            var pair = s_haptics.TakeLast(2).ToArray();
            Check(pair.All(h => h.Handle == 88 && h.Source == expectedSource), "only the wrist controller vibrates");
            Check(pair[0].Duration > 0 && pair[1].Duration > pair[0].Duration && pair[1].Amplitude > pair[0].Amplitude,
                "confirmation pulse is longer and stronger");
        }
        Check(s_haptics.Count == 4, "feedback occurs once per event, never every frame");
        var cue = new WristTapState(true, false, false, true, WristTapEvent.CueStarted, null);
        VrWristInput.SendFeedback(input, cue, HandSide.Right, new AppOptions().WristTapHapticsEnabled);
        Check(s_haptics.Count == 4, "default off suppresses native calls without hiding cue");
        s_error = EVRInputError.NoData;
        input.SendWristFeedback(HandSide.Left, false);
        s_error = EVRInputError.None;
        input.SendWristFeedback(HandSide.Left, true);
        Check(s_haptics.Count == 6 && s_haptics[^1].Source == 11, "haptics recover after a transient error");
        using var missing = new SteamVrInput(new FakeVrInput(), log, options, 0);
        missing.SendWristFeedback(HandSide.Left, false);
        File.WriteAllLines("artifacts/task-036/haptic-calls.txt", s_haptics.Select(h => h.ToString()));
    }

    private static void CheckOptionalAction(VrOverlayHost app, AppSettingsService preferences, RunLog log, AppOptions options)
    {
        using var input = new SteamVrInput(new WristInput(), log, options, 77);
        bool Visible() => app.BoardVisible;
        void Send(bool down, bool active = true, bool updated = true, bool available = true)
        {
            s_digital = new()
            {
                bActive = active,
                bState = down,
                bChanged = true
            };
            app.ProcessToggleAction(input, updated, available);
        }
        foreach (var mode in new[] { ToggleGestureMode.Disabled, ToggleGestureMode.WristButton })
        {
            preferences.ChangeGestureMode(mode);
            bool before = Visible();
            Send(false);
            Send(true);
            Check(Visible() != before, "SteamVR toggle is independent of the gesture selection");
            Send(true);
            Check(Visible() != before, "held action cannot repeat");
            Send(false);
            Send(true);
            Check(Visible() == before, "second independent press toggles back");
            Send(false, active: false);
            Send(true);
            Check(Visible() == before, "activation while held cannot toggle");
            Send(false);
            Send(true, available: false);
            Send(true);
            Check(Visible() == before, "dashboard or tracking recovery cannot toggle while held");
            Send(false);
            Send(true, updated: false);
            Send(true);
            Check(Visible() == before, "failed action update cannot toggle on recovery");
            s_error = EVRInputError.InvalidHandle;
            Send(false);
            Send(true);
            s_error = EVRInputError.None;
            Send(true);
            Check(Visible() == before, "optional input errors cannot affect board state");
        }
        using var unbound = new SteamVrInput(new FakeVrInput(), log, options, 0);
        Check(!unbound.ReadToggle(true, true), "missing optional handle is a harmless no-op");
        using var dashboard = VrOverlaySprites.MakeDashboard(0, ToggleGestureMode.WristButton, true);
        dashboard.Save("artifacts/task-036/dashboard-board-visible.png", ImageFormat.Png);
    }

    private static void CheckManifests()
    {
        string input = Path.Combine(AppContext.BaseDirectory, "input");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(input, "actions.json")));
        const string Action = "/actions/memo/in/toggleboard";
        var toggle = manifest.RootElement.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("name").GetString() == Action);
        Check(toggle.GetProperty("type").GetString() == "boolean" && toggle.GetProperty("requirement").GetString() == "optional", "toggle is an optional boolean action");
        foreach (var language in new[] { "en_US", "ja_JP" })
            Check(manifest.RootElement.GetProperty("localization").EnumerateArray().Single(l => l.GetProperty("language_tag").GetString() == language)
                .GetProperty(Action).GetString()!.Length > 0, "both localized action labels exist");
        foreach (var binding in Directory.GetFiles(input, "bindings_*.json"))
            Check(!File.ReadAllText(binding).Contains(Action, StringComparison.Ordinal), "all default bindings leave toggle unbound");
    }

    private static void CheckHapticManifest()
    {
        string input = Path.Combine(AppContext.BaseDirectory, "input");
        const string Action = "/actions/memo/out/wristfeedback";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(input, "actions.json")));
        Check(manifest.RootElement.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("name").GetString() == Action)
            .GetProperty("type").GetString() == "vibration", "haptic output action exists");
        foreach (var path in Directory.GetFiles(input, "bindings_*.json"))
        {
            using var binding = JsonDocument.Parse(File.ReadAllText(path));
            var haptics = binding.RootElement.GetProperty("bindings").GetProperty("/actions/memo").GetProperty("haptics");
            foreach (string side in new[] { "left", "right" })
                Check(haptics.EnumerateArray().Any(h => h.GetProperty("path").GetString() == $"/user/hand/{side}/output/haptic" &&
                    h.GetProperty("output").GetString() == Action), "default haptic binding exists for each hand");
        }
    }

    private static void Capture(Control control, string path)
    {
        control.PerformLayout();
        control.Refresh();
        Application.DoEvents();
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new(Point.Empty, image.Size));
        image.Save(path, ImageFormat.Png);
    }
}
