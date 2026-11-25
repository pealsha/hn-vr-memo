using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

internal readonly record struct VrHands(uint Left, uint Right, bool Paused, bool DevicesChanged,
    HandInput LeftHand, HandInput RightHand, BoardPose? LeftGesturePose, BoardPose? RightGesturePose);

internal sealed partial class VrOverlayHost
{
    private readonly VrWristInput _wristTap;
    private bool _calibrationWasActive;
    private string? _lastGestureStatus, _lastCandidates;
    internal event Action<string>? GestureStatusChanged;

    private void RunFrames(SteamVrInput input, D3DDevice device, BoardPainter painter, Action pumpUi, Func<bool> stopping, bool showAtStart)
    {
        var propertyError = ETrackedPropertyError.TrackedProp_Success;
        float hz = _system.GetFloatTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref propertyError);
        if (propertyError != ETrackedPropertyError.TrackedProp_Success || !float.IsFinite(hz) || hz is < 30 or > 240)
            hz = 90;
        propertyError = ETrackedPropertyError.TrackedProp_Success;
        float toPhotons = _system.GetFloatTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float, ref propertyError);
        if (propertyError != ETrackedPropertyError.TrackedProp_Success || !float.IsFinite(toPhotons) || toPhotons is < 0 or > .05f)
            toPhotons = 0;
        uint waitMs = (uint)Math.Max(1, Math.Ceiling(1000 / hz));
        _log.Event(FormattableString.Invariant($"motion=device-relative; pen=6 separated padded low-detail faces; displayHz={hz:F1}; syncTimeoutMs={waitMs}; vsyncToPhotonsMs={toPhotons * 1000:F2}; handedness={Options.Handedness}"));
        _log.Event($"Started hidden. VRChatRunning={SteamVrRuntime.IsProcessRunning("VRChat")}; grabRadius={Options.GrabRadiusMeters}; drawingDistance={Options.DrawingDistanceMeters}; gesture={Options.GestureMode}");
        _log.Event("Hold GRIP to grab freely; TRIGGER writes. Both buttons still reach VRChat. Automatic JSON+PNG saving enabled.");
        _system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, OverlayMotion.PredictionSeconds(hz, 0, toPhotons), _poses);
        if (showAtStart)
            Toggle("startup --show");

        double nextProcessCheck = _seconds() + 1;
        while (!stopping() && !_disconnected)
        {
            var wait = WaitForFrame(waitMs);
            double start = _seconds();
            pumpUi();
            if (stopping())
                break;
            if (start >= nextProcessCheck)
            {
                nextProcessCheck = start + 1;
                if (!SteamVrRuntime.IsRunning())
                    break;
            }
            var (prediction, updated) = UpdateTracking(input, hz, toPhotons);
            if (_recenterForHandedness && Valid(OpenVR.k_unTrackedDeviceIndex_Hmd))
                _recenterForHandedness = !Recenter();
            PollEvents();
            if (stopping() || _disconnected)
                break;
            var hands = ReadHands(input, updated, prediction);
            ProcessToggleAction(input, updated, !hands.Paused && !hands.DevicesChanged);
            var result = UpdateGrabInput(hands, input);
            _view.RefreshDashboard(DashboardHoverMask, _visible, _settings.Notice, _calibration.Guidance, _calibration.IsActive);
            var (frame, tracked) = ProcessDrawingInput(result, hands.Paused);
            var (renderMs, uploadMs) = _visible ? _view.RenderFrame(device, painter, hands, result, frame, _grabHighlights) : (0, 0);
            _log.Frame((_seconds() - start) * 1000, _seconds(), _visible, tracked, _engine.Strokes.Count,
                prediction * 1000, wait.ToString(), renderMs, uploadMs);
        }
    }

    private EVROverlayError WaitForFrame(uint waitMs)
    {
        var waitStart = _seconds();
        var wait = _overlay.WaitFrameSync(waitMs);
        // A timeout has already waited one display interval; do not add another fixed sleep.
        if (wait != EVROverlayError.None && _seconds() - waitStart < .001)
            Thread.Sleep((int)waitMs);
        return wait;
    }

    private (float Prediction, bool Updated) UpdateTracking(SteamVrInput input, float hz, float toPhotons)
    {
        float sinceVsync = 0;
        ulong frameCounter = 0;
        _system.GetTimeSinceLastVsync(ref sinceVsync, ref frameCounter);
        float prediction = OverlayMotion.PredictionSeconds(hz, sinceVsync, toPhotons);
        _system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, prediction, _poses);
        bool updated = input.Update();
        return (prediction, updated);
    }

    private VrHands ReadHands(SteamVrInput input, bool updated, float prediction)
    {
        uint left = _system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
        uint right = _system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
        bool paused = _overlay.IsDashboardVisible() || !Valid(OpenVR.k_unTrackedDeviceIndex_Hmd);
        bool devicesChanged = left != _previousLeft || right != _previousRight;
        _previousLeft = left;
        _previousRight = right;
        if (devicesChanged)
            InterruptInk("controller identity changed");
        HandInput Hand(HandSide side, uint index) => input.Read(side, index,
            Valid(index) ? VrGeometry.FromOpenVr(_poses[index].mDeviceToAbsoluteTracking) : new BoardPose(Vector3.Zero, Quaternion.Identity),
            Valid(index) && !paused && !devicesChanged, updated, prediction);
        var leftHand = Hand(HandSide.Left, left);
        var rightHand = Hand(HandSide.Right, right);
        return new(left, right, paused, devicesChanged, leftHand, rightHand, input.LeftGesturePose, input.RightGesturePose);
    }

    private GrabFrameResult UpdateGrabInput(VrHands hands, SteamVrInput input)
    {
        var leftHand = hands.LeftHand;
        var rightHand = hands.RightHand;
        bool devicesChanged = hands.DevicesChanged;
        PollGesture(input, hands, _overlay.IsDashboardVisible());
        var grabFrame = new GrabFrame(leftHand, rightHand, _visible, devicesChanged);
        var result = UpdatePageGrab(_scene, _engine, _page, grabFrame, _seconds(), Options.Handedness, _pages.IsOpen);
        _grabHighlights.Update(grabFrame, result, _seconds());
        LogGrabs(result.Events);
        string candidates = $"Left={result.LeftCandidate?.ToString() ?? "none"}; Right={result.RightCandidate?.ToString() ?? "none"}";
        if (candidates != _lastCandidates)
        {
            _log.Event("grab candidates: " + candidates);
            _lastCandidates = candidates;
        }
        // A release or handover must never connect strokes across owners.
        if (result.Events.Any(e => e.Object is Grabbable.Pen or Grabbable.Eraser))
        {
            InterruptInk("tool ownership changed");
            if (!_page.DesktopOwnsInput)
                _engine.RequireTriggerRelease();
        }
        return result;
    }

    internal static GrabFrameResult UpdatePageGrab(GrabScene scene, HandwriteEngine engine, MemoSession page,
        GrabFrame input, double seconds, HandSide handedness, bool listOpen = false)
    {
        SyncPageGeometry(scene, engine);
        scene.ResizeEnabled = !listOpen && !page.DesktopOwnsInput && !engine.IsWriting && !engine.IsErasing;
        var result = scene.Update(input);
        page.SetVrResizing(result.ResizeHolder is not null, seconds);
        if (result.ResizeHolder is not null)
            page.Resize(result.BoardSize, handedness == HandSide.Left);
        return result;
    }

    private (FrameResult Frame, bool Tracked) ProcessDrawingInput(GrabFrameResult result, bool paused)
    {
        _engine.SetBoardPose(result.BoardPose);
        // With both tools held, a pressed pen wins; otherwise the eraser can act.
        bool useEraser = result.Eraser.InputAvailable && (!result.Pen.InputAvailable || result.Eraser.TriggerDown && !result.Pen.TriggerDown);
        var tool = useEraser ? result.Eraser : result.Pen;
        bool tracked = tool.InputAvailable && _visible;
        var sample = new TipSample(tool.Tip, tracked);
        _pages.RefreshIfNeeded();
        double seconds = _seconds();
        var frame = _page.VrPoint(sample, tool.TriggerDown,
            Options.DrawingDistanceMeters, seconds, useEraser ? DrawingTool.Eraser : DrawingTool.Pen, _pages.IsOpen);
        _pages.Handle(frame, seconds);
        UpdateTriggerUndo(_triggerUndo, _page, result, frame, useEraser, paused, _visible, seconds, _pages.IsOpen);
        LogWriting("input");
        foreach (var command in frame.Commands)
        {
            _log.Event("board command=" + command);
            if (command == BoardCommand.Close)
                Hide("board close");
        }
        // A board command can replace the page. Sync its size before drawing this frame.
        SyncPageGeometry(_scene, _engine);
        return (frame, tracked);
    }

    internal static bool UpdateTriggerUndo(DoubleTriggerUndo triggerUndo, MemoSession page, GrabFrameResult result, FrameResult frame,
        bool useEraser, bool paused, bool visible, double seconds, bool listOpen = false)
    {
        bool eligible = visible && !paused && result.Pen.InputAvailable && !useEraser && !page.IsBusy &&
            !listOpen && frame.CursorUv is null;
        return triggerUndo.Update(result.Pen.Holder, page.Id, result.Pen.TriggerDown, eligible, seconds, page.Revision) && page.UndoFromTrigger(seconds);
    }

    private void OnCalibrationChanged()
    {
        if (_calibrationWasActive == _calibration.IsActive)
            return;
        _calibrationWasActive = _calibration.IsActive;
        _wristTap.Reset();
    }

    internal void ProcessToggleAction(SteamVrInput input, bool updated, bool available)
    {
        if (input.ReadToggle(updated, available))
            Toggle("SteamVR toggle action");
    }

    internal WristTapState UpdateWristTap(BoardPose head, bool headValid, HandInput left, HandInput right,
        BoardPose? leftGrip, BoardPose? rightGrip, bool dashboardVisible, double seconds)
    {
        var state = _wristTap.UpdateWristTap(head, headValid, left, right, leftGrip, rightGrip, dashboardVisible, seconds);
        if (state.Triggered)
            Toggle("wrist tap " + Options.WristButtonActivationMode);
        return state;
    }

    private void PollGesture(SteamVrInput input, VrHands hands, bool dashboardVisible)
    {
        bool headValid = Valid(OpenVR.k_unTrackedDeviceIndex_Hmd);
        var head = headValid ? VrGeometry.FromOpenVr(_poses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking) : new BoardPose(Vector3.Zero, Quaternion.Identity);
        double seconds = _seconds();
        var state = UpdateWristTap(head, headValid, hands.LeftHand, hands.RightHand,
            hands.LeftGesturePose, hands.RightGesturePose, dashboardVisible, seconds);
        _view.RenderWristCalibration(head, headValid && !dashboardVisible, _calibration.Notice);
        _wristTap.LogWristDiagnostics(state, seconds);
        VrWristInput.SendFeedback(input, state, Options.Handedness, Options.WristTapHapticsEnabled);
        string status = state.Triggered ? "手首タップ：切り替え" : state.Pending ? "手首タップ：もう1回" : "手首タップ：見てタップ";
        if (_lastGestureStatus != status)
        {
            _lastGestureStatus = status;
            _log.Event(status);
            GestureStatusChanged?.Invoke(status);
        }
        _view.RenderWristCue(state, head, hands.LeftHand, hands.RightHand, hands.LeftGesturePose, hands.RightGesturePose, hands.Left, hands.Right);
    }

    internal void LogWristDiagnostics(WristTapState state, double seconds) => _wristTap.LogWristDiagnostics(state, seconds);
}
