using System.Runtime.InteropServices;
using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

// Actions read alongside the scene application; no capture or input suppression.
internal sealed class SteamVrInput : IDisposable
{
    private const string AppKey = AutoStart.AppKey;
    private readonly string _manifest = Path.Combine(AppContext.BaseDirectory, "input", "app.vrmanifest");
    private readonly IOpenVrInput _input;
    private readonly RunLog _log;
    private readonly IOpenVrApplications? _applications;
    private readonly VRActiveActionSet_t[] _sets;
    private readonly ulong _grab, _draw, _handPose, _left, _right;
    private readonly ulong _toggle;
    private readonly ulong _wristFeedback;
    private EVRInputError? _lastHapticError;
    private bool _toggleArmed;
    private readonly AppOptions _options;
    private readonly HashSet<HandSide> _reportedOffsets = [];
    private readonly Dictionary<HandSide, string> _previousStatus = [];
    private bool _registered;
    // Unadjusted grip origins for gesture travel; tool calibration offsets stay tool-only.
    internal BoardPose? LeftGesturePose { get; private set; }
    internal BoardPose? RightGesturePose { get; private set; }

    public SteamVrInput(IOpenVrInput input, IOpenVrApplications apps, RunLog log, AppOptions options)
    {
        this._log = log;
        this._options = options;
        _input = input;
        _applications = apps;
        try
        {
            if (!apps.IsApplicationInstalled(AppKey))
            {
                CheckApp(apps.AddApplicationManifest(_manifest, true), "Register temporary application");
                _registered = true;
            }
            CheckApp(apps.IdentifyApplication((uint)Environment.ProcessId, AppKey), "Identify overlay application");
            Check(_input.SetActionManifestPath(Path.Combine(AppContext.BaseDirectory, "input", "actions.json")), "SetActionManifestPath");
            ulong actionSet = 0, grabHandle = 0, drawHandle = 0, poseHandle = 0, leftHandle = 0, rightHandle = 0;
            Check(_input.GetActionSetHandle("/actions/memo", ref actionSet), "GetActionSetHandle");
            Check(_input.GetActionHandle("/actions/memo/in/grab", ref grabHandle), "Get grab action");
            Check(_input.GetActionHandle("/actions/memo/in/draw", ref drawHandle), "Get draw action");
            Check(_input.GetActionHandle("/actions/memo/in/handpose", ref poseHandle), "Get grip pose action");
            ulong toggleHandle = 0;
            var toggleError = _input.GetActionHandle("/actions/memo/in/toggleboard", ref toggleHandle);
            if (toggleError == EVRInputError.None)
                _toggle = toggleHandle;
            else
                _log.Event("Optional toggle action unavailable: " + toggleError);
            ulong feedbackHandle = 0;
            var feedbackError = _input.GetActionHandle("/actions/memo/out/wristfeedback", ref feedbackHandle);
            if (feedbackError == EVRInputError.None)
                _wristFeedback = feedbackHandle;
            else
                _log.Event("Optional wrist haptic unavailable: " + feedbackError);
            Check(_input.GetInputSourceHandle("/user/hand/left", ref leftHandle), "Get left source");
            Check(_input.GetInputSourceHandle("/user/hand/right", ref rightHandle), "Get right source");
            _grab = grabHandle;
            _draw = drawHandle;
            _handPose = poseHandle;
            _left = leftHandle;
            _right = rightHandle;
            _sets = [new VRActiveActionSet_t { ulActionSet = actionSet, nPriority = 0 }];
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Update()
    {
        var error = _input.UpdateActionState(_sets, (uint)Marshal.SizeOf<VRActiveActionSet_t>());
        return error == EVRInputError.None;
    }

    // Native boundary for tests; does not register or identify an application.
    internal SteamVrInput(IOpenVrInput input, RunLog log, AppOptions options, ulong toggleHandle,
        ulong feedbackHandle = 0, ulong leftSource = 0, ulong rightSource = 0)
    {
        _input = input;
        _log = log;
        _options = options;
        _toggle = toggleHandle;
        _wristFeedback = feedbackHandle;
        _left = leftSource;
        _right = rightSource;
        _sets = [];
    }

    internal void SendWristFeedback(HandSide wrist, bool confirmed)
    {
        ulong source = wrist == HandSide.Left ? _left : _right;
        var error = _wristFeedback == 0 || source == 0 ? EVRInputError.InvalidHandle :
            _input.TriggerHapticVibrationAction(_wristFeedback, 0, confirmed ? .08f : .04f, 100, confirmed ? .55f : .35f, source);
        if (error != _lastHapticError)
        {
            _lastHapticError = error;
            _log.Event($"wrist tap haptic: {error}; wrist={wrist}; confirmed={confirmed}");
        }
    }

    internal bool ReadToggle(bool updated, bool available)
    {
        var data = new InputDigitalActionData_t();
        var error = _toggle == 0 ? EVRInputError.InvalidHandle :
            _input.GetDigitalActionData(_toggle, ref data, (uint)Marshal.SizeOf<InputDigitalActionData_t>(), OpenVR.k_ulInvalidInputValueHandle);
        if (!updated || !available || error != EVRInputError.None || !data.bActive)
        {
            _toggleArmed = false;
            return false;
        }
        if (!data.bState)
        {
            _toggleArmed = true;
            return false;
        }
        bool pressed = _toggleArmed && data.bChanged;
        _toggleArmed = false;
        return pressed;
    }

    public HandInput Read(HandSide hand, uint device, BoardPose pose, bool tracked, bool updated, float predictionSeconds = 0)
    {
        var source = hand == HandSide.Left ? _left : _right;
        var grip = new InputDigitalActionData_t();
        var trigger = new InputDigitalActionData_t();
        var gripPose = new InputPoseActionData_t();
        var poseError = _input.GetPoseActionDataRelativeToNow(_handPose, ETrackingUniverseOrigin.TrackingUniverseStanding,
            predictionSeconds, ref gripPose, (uint)Marshal.SizeOf<InputPoseActionData_t>(), source);
        bool poseValid = updated && poseError == EVRInputError.None && gripPose.bActive &&
            gripPose.pose.bPoseIsValid && gripPose.pose.bDeviceIsConnected &&
            gripPose.pose.eTrackingResult == ETrackingResult.Running_OK && OriginMatches(gripPose.activeOrigin, device);
        var palm = new BoardPose(Vector3.Zero, Quaternion.Identity);
        BoardPose? gesturePose = null;
        if (poseValid)
        {
            var gripFrame = VrGeometry.FromOpenVr(gripPose.pose.mDeviceToAbsoluteTracking);
            if (tracked)
                gesturePose = gripFrame;
            palm = HandLandmarks.Palm(gripFrame, hand == HandSide.Left ? _options.LeftPalmOffset : _options.RightPalmOffset);
            if (tracked && _reportedOffsets.Add(hand))
            {
                var delta = pose.WorldToLocal(gripFrame.Position);
                _log.Event(FormattableString.Invariant($"{hand} gripPose minus rawOrigin (raw-local metres)=({delta.X:F4},{delta.Y:F4},{delta.Z:F4}); distance={delta.Length():F4}m"));
            }
        }
        if (hand == HandSide.Left)
            LeftGesturePose = gesturePose;
        else
            RightGesturePose = gesturePose;
        var size = (uint)Marshal.SizeOf<InputDigitalActionData_t>();
        var gripError = _input.GetDigitalActionData(_grab, ref grip, size, source);
        var triggerError = _input.GetDigitalActionData(_draw, ref trigger, size, source);
        bool buttons = updated && gripError == EVRInputError.None && triggerError == EVRInputError.None &&
            grip.bActive && trigger.bActive && OriginMatches(grip.activeOrigin, device) && OriginMatches(trigger.activeOrigin, device);
        string status = $"tracked={tracked && poseValid}; gripPoseActive={poseValid}; pose={poseError}; buttonsActive={buttons}; grab={gripError}; draw={triggerError}";
        if (!_previousStatus.TryGetValue(hand, out var old) || old != status)
        {
            _log.Event($"{hand} input: {status}");
            if (!buttons)
                _log.Event("Input paused. Check controllers and SteamVR bindings for HnMemo (Grab / Draw).");
            if (!poseValid)
                _log.Event("Hand pose unavailable. Bind Hand grip pose to /pose/grip for both hands; raw controller pose is not used as a fallback.");
            _previousStatus[hand] = status;
        }
        return new(palm, tracked && poseValid, grip.bState && buttons, trigger.bState && buttons, buttons);
    }

    private bool OriginMatches(ulong origin, uint device)
    {
        var info = new InputOriginInfo_t();
        return origin != 0 && _input.GetOriginTrackedDeviceInfo(origin, ref info, (uint)Marshal.SizeOf<InputOriginInfo_t>()) == EVRInputError.None &&
            info.trackedDeviceIndex == device;
    }

    public void Dispose()
    {
        if (_registered)
        {
            _applications?.RemoveApplicationManifest(_manifest);
            _registered = false;
        }
    }
    private static void Check(EVRInputError error, string operation)
    {
        if (error != EVRInputError.None)
            throw new InvalidOperationException($"{operation}: {error}");
    }
    private static void CheckApp(EVRApplicationError error, string operation)
    {
        if (error != EVRApplicationError.None)
            throw new InvalidOperationException($"{operation}: {error}");
    }
}
