using System.Numerics;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

internal sealed partial class VrOverlayHost : IDisposable
{
    private readonly AppSettingsService _settings;
    private readonly WristCalibrationController _calibration;
    private readonly HandwriteEngine _engine;
    private readonly MemoSession _page;
    private readonly VrPageBrowser _pages;
    private readonly RunLog _log;
    private readonly Func<double> _seconds;
    private readonly Action _requestStop;
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly GrabScene _scene;
    private readonly uint _eventSize = (uint)Marshal.SizeOf<VREvent_t>();
    private readonly Dictionary<uint, Vector2> _dashboardCursors = [];
    private readonly DoubleTriggerUndo _triggerUndo = new();
    private readonly GrabHighlights _grabHighlights = new();
    private IOpenVrSystem _system = null!;
    private IOpenVrOverlay _overlay = null!;
    private VrSessionResources _resources = null!;
    private VrOverlayView _view = null!;
    private bool _recenterForHandedness;
    private bool _visible, _writingLogged;
    private bool _disconnected;
    private uint _previousLeft = OpenVR.k_unTrackedDeviceIndexInvalid, _previousRight = OpenVR.k_unTrackedDeviceIndexInvalid;

    internal VrOverlayHost(AppSettingsService settings, WristCalibrationController calibration, HandwriteEngine engine,
        MemoSession page, RunLog log, Func<double> seconds, Action requestStop)
    {
        _settings = settings;
        _calibration = calibration;
        _engine = engine;
        _page = page;
        _pages = new VrPageBrowser(page);
        _log = log;
        _seconds = seconds;
        _requestStop = requestStop;
        _scene = new(new(Vector3.Zero, Quaternion.Identity), new(new(.30f, 0, .03f), Quaternion.Identity), Options.GrabSettings)
        {
            Handedness = Options.Handedness
        };
        _wristTap = new(settings, calibration, _scene, log.Event);
        _settings.Changed += OnSettingsChanged;
        _calibration.Changed += OnCalibrationChanged;
    }

    private AppOptions Options => _settings.Current;
    internal GrabScene Scene => _scene;
    internal bool BoardVisible => _visible;
    internal bool IsConnected { get; private set; }
    internal event Action<bool>? ConnectionChanged;

    internal int DashboardHoverMask => _dashboardCursors.Values.Select(cursor => DashboardControls.HitTest(cursor, _calibration.IsActive))
        .Where(i => i >= 0).Aggregate(0, (mask, i) => mask | (1 << i));

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected)
            return;
        IsConnected = connected;
        _calibration.SetConnected(connected, _seconds());
        ConnectionChanged?.Invoke(connected);
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        if ((change & SettingsChange.Handedness) != 0)
        {
            _scene.Handedness = Options.Handedness;
            _calibration.Cancel(_seconds());
            _triggerUndo.Reset();
            _recenterForHandedness = IsConnected;
        }
        if ((change & (SettingsChange.Handedness | SettingsChange.Gesture | SettingsChange.WristCalibration)) != 0)
            _wristTap.Reset();
        if (IsConnected && (change & SettingsChange.Brightness) != 0)
            _view.ApplyBrightness();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _calibration.Changed -= OnCalibrationChanged;
        _pages.Dispose();
        SetConnected(false);
    }

    internal void SetHeadPose(TrackedDevicePose_t pose) => _poses[OpenVR.k_unTrackedDeviceIndex_Hmd] = pose;

    internal void InitializeEventProcessing(IOpenVrSystem system, IOpenVrOverlay overlay, ulong dashboardHandle, IEnumerable<ulong> overlayHandles)
    {
        InitializeBoundaries(system, overlay);
        _resources.Dashboard = dashboardHandle;
        _resources.Handles.AddRange(overlayHandles);
        SetConnected(true);
    }

    private void InitializeBoundaries(IOpenVrSystem system, IOpenVrOverlay overlay)
    {
        _system = system;
        _overlay = overlay;
        _resources = new VrSessionResources();
        _view = new VrOverlayView(overlay, _resources, _settings, _page, _pages, _engine, _scene, _poses, Valid, _log, _seconds);
    }

    internal void RunSession(Action pumpUi, Func<bool> stopping, bool showAtStart, AutoStartCoordinator autoStart)
    {
        _disconnected = false;
        _recenterForHandedness = false;
        _dashboardCursors.Clear();
        _grabHighlights.Reset();
        _wristTap.Reset();
        _scene.Handedness = Options.Handedness;
        var error = EVRInitError.None;
        var system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        try
        {
            if (error != EVRInitError.None || system is null)
            {
                _log.Event($"OpenVR initialization failed: {error}. {OpenVR.GetStringForHmdError(error)}");
                return;
            }
            var applications = new OpenVrApplicationsAdapter(OpenVR.Applications ?? throw new InvalidOperationException("OpenVR Applications unavailable."));
            autoStart.HandleVrConnected(applications);
            var overlay = new OpenVrOverlayAdapter(OpenVR.Overlay ?? throw new InvalidOperationException("OpenVR overlay interface unavailable."));
            InitializeBoundaries(new OpenVrSystemAdapter(system), overlay);
            var inputApi = new OpenVrInputAdapter(OpenVR.Input ?? throw new InvalidOperationException("SteamVR Input unavailable."));
            using var input = new SteamVrInput(inputApi, applications, _log, Options);
            int adapter = -1;
            _system.GetDXGIOutputInfo(ref adapter);
            if (adapter < 0)
                throw new InvalidOperationException("SteamVR did not report a D3D11 adapter. Check the HMD connection.");
            using var device = new D3DDevice(adapter);
            using var painter = new BoardPainter();
            _resources.CreateFixedTextures(device, painter, Options, _visible, _settings.Notice);
            try
            {
                _view.Initialize(device);
                SetConnected(true);
                RunFrames(input, device, painter, pumpUi, stopping, showAtStart);
            }
            finally
            {
                SetConnected(false);
                try
                {
                    _resources.DestroyOverlaysAndDynamicTextures(overlay);
                }
                finally
                {
                    _resources.DisposeFixedTextures();
                }
            }
        }
        finally
        {
            _visible = false;
            autoStart.HandleVrDisconnected();
            OpenVR.Shutdown();
            _page.InterruptVr(_seconds());
        }
    }

    private bool Valid(uint index) => index < _poses.Length && _poses[index].bDeviceIsConnected && _poses[index].bPoseIsValid &&
        _poses[index].eTrackingResult == ETrackingResult.Running_OK;

    private void Toggle(string source)
    {
        if (_visible)
        {
            Hide(source);
            return;
        }
        if (!Recenter())
            return;
        _visible = true;
        // The next frame refreshes the board image and shows the pen.
        _log.Event("board shown: " + source);
        _view.Show();
    }

    private void Hide(string source)
    {
        InterruptInk("hide");
        if (!_page.Save(_seconds()))
        {
            _log.Event("Hide cancelled: save failed.");
            return;
        }
        LogWriting(source);
        LogGrabs(_scene.Update(new GrabFrame(default, default, false, false)).Events);
        _grabHighlights.Reset();
        _visible = false;
        _view.Hide();
        _log.Event("board hidden: " + source);
    }

    private bool Recenter()
    {
        if (!Valid(OpenVR.k_unTrackedDeviceIndex_Hmd))
        {
            _log.Event("Recenter skipped: valid HMD tracking required.");
            return false;
        }
        var head = VrGeometry.FromOpenVr(_poses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
        BoardPose pose;
        try
        {
            pose = VrGeometry.PlaceBoard(head, Options, _engine.BoardSize);
        }
        catch (InvalidOperationException e)
        {
            _log.Event(e.Message);
            return false;
        }
        InterruptInk("recenter");
        _scene.SetPageSize(_engine.BoardSize, _scene.Handedness == HandSide.Left ? _engine.MinimumBoardSizeFromRight : _engine.MinimumBoardSize);
        LogGrabs(_scene.Reset(pose, BoardResize.PenDock(pose, _engine.BoardSize, Options.Handedness)));
        _resources.Attachments.Clear();
        _triggerUndo.Reset();
        _grabHighlights.Reset();
        _engine.SetBoardPose(pose);
        _view.SetPose(_resources.Board, pose);
        _log.Event("board recentered; pen and eraser returned to dock; release grips before grabbing");
        return true;
    }

    internal static void SyncPageGeometry(GrabScene scene, HandwriteEngine engine)
    {
        scene.SetPageSize(engine.BoardSize, scene.Handedness == HandSide.Left ? engine.MinimumBoardSizeFromRight : engine.MinimumBoardSize);
        engine.SetBoardPose(scene.BoardPose);
    }

    private void InterruptInk(string reason)
    {
        _page.InterruptVr(_seconds());
        LogWriting(reason);
    }

    private void LogGrabs(IReadOnlyList<GrabEvent> events)
    {
        foreach (var item in events)
            _log.Event($"grab {item.Kind}: {item.Object}, hand={item.Hand}");
    }

    private void LogWriting(string reason)
    {
        if (_writingLogged == _engine.IsWriting)
            return;
        _writingLogged = _engine.IsWriting;
        _log.Event($"writing {(_writingLogged ? "start" : "end")}: reason={reason}");
    }

    internal void PollEvents()
    {
        var evt = new VREvent_t();
        while (_system.PollNextEvent(ref evt, _eventSize))
        {
            var type = (EVREventType)evt.eventType;
            if (type is EVREventType.VREvent_Quit or EVREventType.VREvent_DriverRequestedQuit)
                _disconnected = true;
            if (type is EVREventType.VREvent_ChaperoneUniverseHasChanged or EVREventType.VREvent_StandingZeroPoseReset)
                Hide("tracking origin changed; reopen to recenter");
        }
        foreach (var handle in _resources.Handles)
        {
            while (_overlay.PollNextOverlayEvent(handle, ref evt, _eventSize))
            {
                var type = (EVREventType)evt.eventType;
                if (type is EVREventType.VREvent_Quit or EVREventType.VREvent_DriverRequestedQuit)
                {
                    _disconnected = true;
                    continue;
                }
                if (handle != _resources.Dashboard)
                    continue;
                if (type == EVREventType.VREvent_OverlayHidden)
                    _dashboardCursors.Clear();
                if (type == EVREventType.VREvent_FocusLeave)
                    _dashboardCursors.Remove(evt.data.overlay.cursorIndex);
                if (type == EVREventType.VREvent_MouseMove)
                    _dashboardCursors[evt.data.mouse.cursorIndex] = new(evt.data.mouse.x, evt.data.mouse.y);
                if (type != EVREventType.VREvent_MouseButtonDown || evt.data.mouse.button != 1 ||
                    !_dashboardCursors.TryGetValue(evt.data.mouse.cursorIndex, out var cursor))
                    continue;
                int button = DashboardControls.HitTest(cursor, _calibration.IsActive);
                switch (button)
                {
                    case 0:
                        Toggle("dashboard");
                        break;
                    case 1:
                        Recenter();
                        break;
                    case 2:
                        _requestStop();
                        break;
                    case 3:
                    case 4:
                    case 5:
                        _settings.ChangeBrightness(DashboardControls.Brightness(button));
                        break;
                    case 6:
                    case 7:
                        _settings.ChangeGestureMode(DashboardControls.Gesture(button));
                        break;
                    case 8:
                    case 9:
                        _settings.ChangeWristButtonActivationMode(DashboardControls.Activation(button));
                        break;
                    case 10:
                        _calibration.Start();
                        break;
                    case 11:
                        _calibration.Cancel(_seconds());
                        break;
                }
            }
        }
    }
}
