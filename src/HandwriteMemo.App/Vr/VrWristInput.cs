using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class VrWristInput(AppSettingsService settings, WristCalibrationController calibration, GrabScene _scene, Action<string> log)
{
    private readonly WristTapGesture _wristTap = new();

    internal void Reset() => _wristTap.Reset();

    private string? _wristBlockedReason, _loggedWristBlockedReason;
    private double _nextWristDiagnosticSeconds;
    private bool _wristCueShown;
    private Vector3? _controllerInWristGrip;
    private double _nextCalibrationDiagnosticSeconds;

    internal WristTapState UpdateWristTap(BoardPose head, bool headValid, HandInput left, HandInput right,
        BoardPose? leftGrip, BoardPose? rightGrip, bool dashboardVisible, double seconds)
    {
        bool leftWrist = settings.Current.Handedness == HandSide.Right;
        var wristGrip = leftWrist ? leftGrip : rightGrip;
        var dominantGrip = leftWrist ? rightGrip : leftGrip;
        var finger = HandLandmarks.FingerTip(leftWrist ? right.Pose : left.Pose, settings.Current);
        bool calibrating = calibration.IsActive;
        _controllerInWristGrip = wristGrip is { } w && dominantGrip is { } d ? w.WorldToLocal(d.Position) : null;
        calibration.Update(dashboardVisible, headValid && left.IsTracked && right.IsTracked && dominantGrip is not null,
            wristGrip, finger, seconds);
        if (calibrating && !dashboardVisible && wristGrip is { } measuredGrip && _controllerInWristGrip is { } controller &&
            left.IsTracked && right.IsTracked && seconds >= _nextCalibrationDiagnosticSeconds)
        {
            var local = measuredGrip.WorldToLocal(finger);
            _nextCalibrationDiagnosticSeconds = seconds + .2;
            log(string.Join("; ", FormattableString.Invariant($"wrist calibration sample: sampleSeconds={seconds:F4}"),
                FormattableString.Invariant($"fingerInGrip=({local.X:F4},{local.Y:F4},{local.Z:F4})"),
                FormattableString.Invariant($"controllerGripInWristGrip=({controller.X:F4},{controller.Y:F4},{controller.Z:F4})"),
                $"wrist={(leftWrist ? HandSide.Left : HandSide.Right)}; controller source=/pose/grip"));
        }
        _wristBlockedReason =
            calibrating ? "calibrating" :
            settings.Current.GestureMode != ToggleGestureMode.WristButton ? "disabled" :
            dashboardVisible ? "dashboard open" :
            !headValid || !left.IsTracked || !right.IsTracked ? "tracking lost" :
            !left.ButtonsValid || !right.ButtonsValid ? "buttons unavailable" :
            leftGrip is null || rightGrip is null ? "grip pose unavailable" :
            _scene.PenHolder is not null || _scene.BoardHolder is not null || _scene.EraserHolder is not null || _scene.ResizeHolder is not null ? "holding object" :
            left.GripDown || right.GripDown ? "grip pressed" : null;
        var state = _wristTap.Update(head, wristGrip ?? default,
            leftWrist ? HandSide.Left : HandSide.Right,
            finger, _wristBlockedReason is null, seconds, settings.Current.WristButtonActivationMode, settings.Current.CurrentWristTapCentre);
        return state;
    }

    internal static void SendFeedback(SteamVrInput input, WristTapState state, HandSide dominant, bool enabled)
    {
        if (enabled && (state.Events & (WristTapEvent.CueStarted | WristTapEvent.Confirmed)) != 0)
            input.SendWristFeedback(dominant == HandSide.Right ? HandSide.Left : HandSide.Right, state.Triggered);
    }

    // Display placement has no role in recognition. The image faces the head to stay legible.
    internal static BoardPose CuePose(BoardPose head, BoardPose grip, HandSide wrist, Vector3? centre = null)
    {
        var offset = centre ?? WristTapGesture.ContactCentre;
        offset.X *= wrist == HandSide.Left ? 1 : -1;
        var position = grip.LocalToWorld(offset);
        if (Vector3.DistanceSquared(head.Position, position) < .01f)
            position = grip.Position;
        var towardHead = Vector3.Normalize(head.Position - position);
        position += towardHead * .04f;
        var up = Vector3.Transform(Vector3.UnitY, head.Rotation);
        if (MathF.Abs(Vector3.Dot(towardHead, up)) > .98f)
            up = Vector3.Transform(-Vector3.UnitZ, head.Rotation);
        return new(position, Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -towardHead, up)));
    }

    internal void LogWristDiagnostics(WristTapState state, double seconds)
    {
        bool changed = _wristBlockedReason != _loggedWristBlockedReason;
        if (changed)
        {
            _loggedWristBlockedReason = _wristBlockedReason;
            log("wrist tap blocked: " + (_wristBlockedReason ?? "no"));
        }
        bool cueChanged = _wristCueShown != state.Visible;
        if (cueChanged)
        {
            _wristCueShown = state.Visible;
            string cueReason = state.Visible ? state.Confirmed ? "confirmed" : "first contact" : state.Reason ?? "feedback ended";
            log($"wrist tap cue: {(state.Visible ? "show" : "hide")}; reason={cueReason}");
        }
        bool eventOccurred = state.Events != WristTapEvent.None || cueChanged || changed;
        var measured = _wristTap.Measurements;
        if (!eventOccurred && (measured is not { } m || m.FingerDistance > WristTapGesture.DiagnosticNearDistanceMetres || seconds < _nextWristDiagnosticSeconds))
            return;
        _nextWristDiagnosticSeconds = seconds + .2;
        string values = measured is { } v ? string.Join("; ",
            $"look={v.Looking}",
            FormattableString.Invariant($"gaze={v.Gaze:F3} (need >={WristTapGesture.MinimumGazeDot})"),
            FormattableString.Invariant($"headDistance={v.HeadDistance:F3}m (need {WristTapGesture.MinimumHeadDistanceMetres}-{WristTapGesture.MaximumHeadDistanceMetres})"),
            FormattableString.Invariant($"fingerToGrip={v.FingerDistance:F3}m; fingerInGrip=({v.FingerInGrip.X:F3},{v.FingerInGrip.Y:F3},{v.FingerInGrip.Z:F3})"),
            FormattableString.Invariant($"controllerGripInWristGrip={_controllerInWristGrip}; controller source=/pose/grip; fingertip source=palm+IndexTipOffset"),
            FormattableString.Invariant($"headPosition={v.HeadPose.Position}; headRotation={v.HeadPose.Rotation}; gripPosition={v.GripPose.Position}; gripRotation={v.GripPose.Rotation}"))
            : "measurements unavailable";
        log(string.Join("; ",
            FormattableString.Invariant($"sampleSeconds={seconds:F4}"),
            $"wrist tap: events={state.Events}; reason={_wristBlockedReason ?? state.Reason ?? "no"}; pending={state.Pending}; confirmed={state.Confirmed}", values,
            $"wrist={(settings.Current.Handedness == HandSide.Right ? HandSide.Left : HandSide.Right)}; right wrist uses mirrored X; haptics={settings.Current.WristTapHapticsEnabled}",
            FormattableString.Invariant($"returnDot={_wristTap.ReturnDirectionDot:F3} (need >={WristTapGesture.MinimumReturnDirectionDot})"),
            FormattableString.Invariant($"centre={settings.Current.CurrentWristTapCentre}; radius={WristTapGesture.ContactRadiusMetres}; releaseMargin={WristTapGesture.ReleaseMarginMetres}"),
            FormattableString.Invariant($"contactMax={WristTapGesture.ContactMaximumSeconds}s; pairMax={WristTapGesture.PairMaximumSeconds}s"),
            FormattableString.Invariant($"outsideMin={WristTapGesture.OutsideMinimumSeconds}s; lookMin={WristTapGesture.LookMinimumSeconds}s; cooldown={WristTapGesture.CooldownSeconds}s")));
    }
}
