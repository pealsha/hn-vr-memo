using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

internal sealed class VrOverlayView(IOpenVrOverlay _overlay, VrSessionResources _resources, AppSettingsService settings,
    MemoSession _page, VrPageBrowser _pages, HandwriteEngine _engine, GrabScene _scene,
    TrackedDevicePose_t[] _poses, Func<uint, bool> isTracked, RunLog _log, Func<double> seconds)
{
    private AppOptions Options => settings.Current;
    private int _wristCueTextureIndex = -1;
    private string? _calibrationTextureText;
    private bool _penHighlighted, _eraserHighlighted;
    private BoardImageState? _boardImageState;

    private readonly record struct BoardImageState(
        Guid PageId, long InkRevision, long ActiveStrokeRevision, BoardSize Size,
        Vector2? Cursor, ContactState Contact, DrawingTool Tool, bool Highlighted,
        string PenColor, float PenWidth, EraserMode EraserMode, bool CanUndo, bool CanRedo, bool Busy,
        VrInputStatus Status, string? Notice, string? Failure, bool SaveFailed, bool Pinned,
        bool ListOpen, int ListScreen, long ListRevision, string? ListNotice, string? PageTitle,
        bool CanGoBack, bool CanGoForward);

    private (int HoverMask, bool Visible, HandSide Hand, float Brightness, ToggleGestureMode Gesture,
        WristButtonActivationMode Activation, string? Notice, string Guidance, bool Calibrating)? _dashboardState;

    internal void Initialize(D3DDevice device)
    {
        CreateOverlays();
        _resources.CreateToolTextures(device);
        _resources.DashboardTexture.Upload(_resources.DashboardImage);
        Submit(_resources.Dashboard, _resources.DashboardTexture);
        _resources.TabTexture.Upload(_resources.TabImage);
        Submit(_resources.Thumbnail, _resources.TabTexture);
        for (int i = 0; i < _resources.PenFaces.Length; i++)
            Submit(_resources.PenFaces[i], _resources.PenTextures[i]);
        for (int i = 0; i < _resources.EraserFaces.Length; i++)
            Submit(_resources.EraserFaces[i], _resources.EraserTextures[i]);
        _resources.TipTexture.Upload(_resources.TipImage);
        Submit(_resources.PenMarker, _resources.TipTexture);
        Submit(_resources.EraserMarker, _resources.TipTexture);
    }

    internal void ApplyBrightness()
    {
        float value = Options.BoardBrightness;
        foreach (ulong handle in _resources.Handles)
            Check(_overlay.SetOverlayColor(handle, value, value, value), "overlay brightness");
    }

    internal void Show()
    {
        _boardImageState = null;
        Check(_overlay.ShowOverlay(_resources.Board), "show board");
    }

    internal void Hide()
    {
        Check(_overlay.HideOverlay(_resources.Board), "hide board");
        Check(_overlay.HideOverlay(_resources.ToolsDock), "hide tools");
        Check(_overlay.HideOverlay(_resources.ResizeHandle), "hide resize handle");
        Check(_overlay.HideOverlay(_resources.PenMarker), "hide tip");
        Check(_overlay.HideOverlay(_resources.EraserMarker), "hide eraser tip");
        foreach (var face in _resources.PenFaces)
            Check(_overlay.HideOverlay(face), "hide pen");
        foreach (var face in _resources.EraserFaces)
            Check(_overlay.HideOverlay(face), "hide eraser");
    }

    private void CreateOverlays()
    {
        const string Key = AutoStart.AppKey;
        _resources.Board = Create(Key + ".board", "HnMemo Board");
        _resources.ToolsDock = Create(Key + ".tools", "HnMemo Tools and Guidance");
        Check(_overlay.SetOverlayWidthInMeters(_resources.ToolsDock, BoardSize.Default.WidthMetres), "tools width");
        _resources.ResizeHandle = Create(Key + ".resize", "HnMemo Resize Handle");
        Check(_overlay.SetOverlayWidthInMeters(_resources.ResizeHandle, VrOverlaySprites.ResizeVisualWidthMetres), "resize visual width");
        Check(_overlay.SetOverlaySortOrder(_resources.ResizeHandle, 1), "resize handle sort order");
        _resources.PenMarker = Create(Key + ".pen", "Handwrite Pen Tip");
        _resources.EraserMarker = Create(Key + ".eraser.tip", "Handwrite Eraser Tip");
        _resources.WristCue = Create(Key + ".wristcue", "HnMemo Wrist Cue");
        _resources.WristCalibration = Create(Key + ".wristcalibration", "HnMemo Wrist Calibration");
        Check(_overlay.SetOverlayWidthInMeters(_resources.WristCalibration, .55f), "wrist calibration guidance width");
        Check(_overlay.SetOverlaySortOrder(_resources.WristCalibration, 3), "wrist calibration guidance sort order");
        Check(_overlay.SetOverlayWidthInMeters(_resources.WristCue, .05f), "wrist cue width");
        Check(_overlay.SetOverlayFlag(_resources.WristCue, VROverlayFlags.NoBackside, true), "wrist cue outward only");
        for (int i = 0; i < _resources.PenFaces.Length; i++)
        {
            _resources.PenFaces[i] = Create(Key + ".penface" + i, "Handwrite Pen");
            Check(_overlay.SetOverlayWidthInMeters(_resources.PenFaces[i], PenPainter.FaceWidthMetres(i)), "pen width");
            Check(_overlay.SetOverlayFlag(_resources.PenFaces[i], VROverlayFlags.NoBackside, true), "pen outward faces only");
            // Paper is order 0, tool bodies 1, tip markers 2. Avoid HMD-distance order flips against the paper.
            Check(_overlay.SetOverlaySortOrder(_resources.PenFaces[i], 1), "pen body sort order");
        }
        for (int i = 0; i < _resources.EraserFaces.Length; i++)
        {
            _resources.EraserFaces[i] = Create(Key + ".eraserface" + i, "Handwrite Eraser");
            Check(_overlay.SetOverlayWidthInMeters(_resources.EraserFaces[i], EraserPainter.FaceWidthMetres(i)), "eraser width");
            Check(_overlay.SetOverlaySortOrder(_resources.EraserFaces[i], 1), "eraser body sort order");
        }
        ulong dashboard = _resources.Dashboard, thumbnail = _resources.Thumbnail;
        var dashboardError = _overlay.CreateDashboardOverlay(Key + ".dashboard", "HnMemo", ref dashboard, ref thumbnail);
        _resources.Dashboard = dashboard;
        _resources.Thumbnail = thumbnail;
        Check(dashboardError, "CreateDashboardOverlay");
        _resources.Handles.Add(_resources.Dashboard);
        _resources.Handles.Add(_resources.Thumbnail);
        Check(_overlay.SetOverlayWidthInMeters(_resources.Board, BoardSize.Default.WidthMetres), "board width");
        Check(_overlay.SetOverlaySortOrder(_resources.Board, 0), "board sort order");
        Check(_overlay.SetOverlayWidthInMeters(_resources.Dashboard, 1.15f), "dashboard width");
        Check(_overlay.SetOverlayWidthInMeters(_resources.PenMarker, .010f), "tip width");
        Check(_overlay.SetOverlaySortOrder(_resources.PenMarker, 2), "tip sort order");
        Check(_overlay.SetOverlayWidthInMeters(_resources.EraserMarker, .010f), "eraser tip width");
        Check(_overlay.SetOverlaySortOrder(_resources.EraserMarker, 2), "eraser tip sort order");
        foreach (var handle in _resources.Handles.Where(h => h != _resources.Dashboard && h != _resources.Thumbnail))
            Check(_overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.None), "overlay input");
        Check(_overlay.SetOverlayInputMethod(_resources.Dashboard, VROverlayInputMethod.Mouse), "dashboard input");
        var scale = new HmdVector2_t { v0 = DashboardControls.Width, v1 = DashboardControls.Height };
        Check(_overlay.SetOverlayMouseScale(_resources.Dashboard, ref scale), "dashboard scale");
        ApplyBrightness();
    }

    private ulong Create(string key, string name)
    {
        ulong handle = 0;
        Check(_overlay.CreateOverlay(key, name, ref handle), "CreateOverlay (another instance may be running)");
        _resources.Handles.Add(handle);
        return handle;
    }

    private void Submit(ulong handle, D3DTexture texture)
    {
        var vrTexture = new Texture_t { handle = texture.Texture.NativePointer, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Gamma };
        Check(_overlay.SetOverlayTexture(handle, ref vrTexture), "SetOverlayTexture");
    }

    internal void SetPose(ulong handle, BoardPose pose)
    {
        var transform = VrGeometry.ToOpenVr(pose);
        Check(_overlay.SetOverlayTransformAbsolute(handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref transform), "overlay transform");
    }

    private void SetAttachedPose(ulong handle, BoardPose world, HandSide? holder, HandInput leftHand, HandInput rightHand, uint left, uint right)
    {
        if (!_resources.Attachments.TryGetValue(handle, out var motion))
            _resources.Attachments[handle] = motion = new();
        uint index = holder == HandSide.Left ? left : right;
        if (holder is not { } hand || !isTracked(index))
        {
            motion.Release();
            SetPose(handle, world);
            return;
        }
        var raw = VrGeometry.FromOpenVr(_poses[index].mDeviceToAbsoluteTracking);
        var relative = motion.RelativePose(world, hand, index, raw, hand == HandSide.Left ? leftHand.Pose : rightHand.Pose);
        var transform = VrGeometry.ToOpenVr(relative);
        Check(_overlay.SetOverlayTransformTrackedDeviceRelative(handle, index, ref transform), "device-relative overlay transform");
    }

    private void UpdateTools(HandInput leftHand, HandInput rightHand, uint left, uint right)
    {
        for (int i = 0; i < _resources.PenFaces.Length; i++)
        {
            SetAttachedPose(_resources.PenFaces[i], PenPainter.FacePose(_scene.PenPose, i), _scene.PenHolder, leftHand, rightHand, left, right);
            Check(_overlay.ShowOverlay(_resources.PenFaces[i]), "show pen");
        }
        for (int i = 0; i < _resources.EraserFaces.Length; i++)
        {
            SetAttachedPose(_resources.EraserFaces[i], EraserPainter.FacePose(_scene.EraserPose, i), _scene.EraserHolder, leftHand, rightHand, left, right);
            Check(_overlay.ShowOverlay(_resources.EraserFaces[i]), "show eraser");
        }
        if (isTracked(OpenVR.k_unTrackedDeviceIndex_Hmd))
        {
            var head = VrGeometry.FromOpenVr(_poses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
            SetAttachedPose(_resources.PenMarker, new BoardPose(_scene.PenTip, head.Rotation), _scene.PenHolder, leftHand, rightHand, left, right);
            Check(_overlay.ShowOverlay(_resources.PenMarker), "show tip");
            SetAttachedPose(_resources.EraserMarker, new BoardPose(_scene.EraserTip, head.Rotation), _scene.EraserHolder, leftHand, rightHand, left, right);
            Check(_overlay.ShowOverlay(_resources.EraserMarker), "show eraser tip");
        }
        else
        {
            Check(_overlay.HideOverlay(_resources.PenMarker), "hide tip");
            Check(_overlay.HideOverlay(_resources.EraserMarker), "hide eraser tip");
        }
    }

    private int _resizeTextureIndex = -1;

    internal void RenderResizeHandle(HandInput leftHand, HandInput rightHand, uint left, uint right, bool highlighted)
    {
        SetAttachedPose(_resources.ResizeHandle, VrOverlaySprites.ResizeVisualPose(_scene.ResizeHandlePose),
            _scene.ResizeHolder ?? _scene.BoardHolder, leftHand, rightHand, left, right);
        int state = _scene.ResizeHolder is not null ? 3 : highlighted ? 1 : _scene.ResizeEnabled && _scene.BoardHolder is null ? 0 : 2;
        int index = (Options.Handedness == HandSide.Left ? 4 : 0) + state;
        // Fixed textures stay submitted until their visible state actually changes.
        if (_resizeTextureIndex != index)
        {
            Submit(_resources.ResizeHandle, _resources.ResizeTextures[index]);
            _resizeTextureIndex = index;
            _log.Event($"resize texture state={state}; holder={_scene.ResizeHolder ?? _scene.BoardHolder}");
        }
        if (_pages.IsOpen)
            Check(_overlay.HideOverlay(_resources.ResizeHandle), "hide resize handle in list");
        else
            Check(_overlay.ShowOverlay(_resources.ResizeHandle), "show resize handle");
    }

    internal void RefreshDashboard(int hoverMask, bool visible, string? notice, string guidance, bool calibrating)
    {
        var state = (hoverMask, visible, Options.Handedness, Options.BoardBrightness, Options.GestureMode,
            Options.WristButtonActivationMode, notice, guidance, calibrating);
        if (_dashboardState == state)
            return;
        using var image = VrOverlaySprites.MakeDashboard(hoverMask, Options.GestureMode, visible, Options.Handedness,
            Options.BoardBrightness, notice, Options.WristButtonActivationMode, guidance, calibrating);
        _resources.DashboardTexture.Upload(image);
        Submit(_resources.Dashboard, _resources.DashboardTexture);
        _dashboardState = state;
    }

    internal (double RenderMs, double UploadMs) RenderFrame(D3DDevice device, BoardPainter painter, VrHands hands, GrabFrameResult result, FrameResult frame, GrabHighlights highlights)
    {
        var leftHand = hands.LeftHand;
        var rightHand = hands.RightHand;
        uint left = hands.Left;
        uint right = hands.Right;
        SetAttachedPose(_resources.Board, _scene.BoardPose, _scene.BoardHolder, leftHand, rightHand, left, right);
        var state = VrInputGuide.Status(_engine, _page, _scene, result, leftHand, rightHand,
            _overlay.IsDashboardVisible(), isTracked(OpenVR.k_unTrackedDeviceIndex_Hmd));
        if (_page.TriggerUndoNotice(seconds()) is { } undoNotice)
            state = new(undoNotice, VrInputState.Undo);
        bool highlightPen = highlights.Contains(Grabbable.Pen);
        bool highlightBoard = highlights.Contains(Grabbable.Board);
        bool highlightEraser = highlights.Contains(Grabbable.Eraser);
        bool highlightResize = result.ResizeHolder is not null || highlights.Contains(Grabbable.ResizeHandle);
        var timings = UpdateBoardImages(device, painter, frame, state, highlightBoard);
        SetAttachedPose(_resources.ToolsDock, BoardPainter.ToolsPose(_scene.BoardPose, _engine.BoardSize),
            _scene.BoardHolder, leftHand, rightHand, left, right);
        Check(_overlay.ShowOverlay(_resources.ToolsDock), "show tools and recovery guidance");
        RenderResizeHandle(leftHand, rightHand, left, right, highlightResize);
        if (highlightPen != _penHighlighted)
        {
            for (int i = 0; i < _resources.PenFaces.Length; i++)
                Submit(_resources.PenFaces[i], _resources.PenTextures[i + (highlightPen ? PenPainter.FaceCount : 0)]);
            _penHighlighted = highlightPen;
        }
        if (highlightEraser != _eraserHighlighted)
        {
            for (int i = 0; i < _resources.EraserFaces.Length; i++)
                Submit(_resources.EraserFaces[i], _resources.EraserTextures[i + (highlightEraser ? EraserPainter.FaceCount : 0)]);
            _eraserHighlighted = highlightEraser;
        }
        UpdateTools(leftHand, rightHand, left, right);
        return timings;
    }

    private (double RenderMs, double UploadMs) UpdateBoardImages(D3DDevice device, BoardPainter painter, FrameResult frame,
        VrInputStatus status, bool highlighted)
    {
        bool list = _pages.IsOpen;
        var state = new BoardImageState(
            PageId: _page.Id,
            InkRevision: _engine.InkRevision,
            ActiveStrokeRevision: _engine.ActiveStrokeRevision,
            Size: _engine.BoardSize,
            Cursor: frame.CursorUv,
            Contact: frame.Contact,
            Tool: frame.Tool,
            Highlighted: highlighted,
            PenColor: _engine.PenColor,
            PenWidth: _engine.PenWidthMetres,
            EraserMode: _engine.EraserMode,
            CanUndo: _engine.CanUndo,
            CanRedo: _engine.CanRedo,
            Busy: _page.IsBusy,
            Status: status,
            Notice: _page.Notice,
            Failure: _page.VrFailureMessage,
            SaveFailed: _page.Error is not null,
            Pinned: _page.IsPinned,
            ListOpen: list,
            ListScreen: list ? _pages.Screen : 0,
            ListRevision: list ? _pages.Revision : 0,
            ListNotice: list ? _pages.Notice : null,
            PageTitle: list ? _page.Title : null,
            CanGoBack: _pages.CanNavigate(-1),
            CanGoForward: _pages.CanNavigate(1));
        if (state == _boardImageState)
            return default;

        double renderStart = seconds();
        painter.Draw(_engine, frame, status, highlighted, _page, _pages);
        double renderMs = (seconds() - renderStart) * 1000;

        double uploadStart = seconds();
        _resources.EnsureBoardTexture(_overlay, device, painter.Image);
        if (_boardImageState?.Size != state.Size)
            Check(_overlay.SetOverlayWidthInMeters(_resources.Board, state.Size.WidthMetres), "board width");
        _resources.BoardTexture!.Upload(painter.Image);
        Submit(_resources.Board, _resources.BoardTexture);
        _resources.ToolsTexture.Upload(painter.ToolsImage);
        Submit(_resources.ToolsDock, _resources.ToolsTexture);
        _boardImageState = state;
        return (renderMs, (seconds() - uploadStart) * 1000);
    }

    internal void RenderWristCalibration(BoardPose head, bool headValid, string? notice)
    {
        if (notice is null || !headValid)
        {
            Check(_overlay.HideOverlay(_resources.WristCalibration), "hide wrist calibration guidance");
            return;
        }
        if (_calibrationTextureText != notice)
        {
            using var image = VrOverlaySprites.MakeWristCalibration(notice);
            _resources.WristCalibrationTexture.Upload(image);
            Submit(_resources.WristCalibration, _resources.WristCalibrationTexture);
            _calibrationTextureText = notice;
        }
        SetPose(_resources.WristCalibration, new(head.LocalToWorld(new(0, -.16f, -.55f)), head.Rotation));
        Check(_overlay.ShowOverlay(_resources.WristCalibration), "show wrist calibration guidance");
    }

    internal void RenderWristCue(WristTapState state, BoardPose head, HandInput left, HandInput right,
        BoardPose? leftGrip, BoardPose? rightGrip, uint leftIndex, uint rightIndex)
    {
        if (!state.Visible)
        {
            Check(_overlay.HideOverlay(_resources.WristCue), "hide wrist cue");
            return;
        }
        int textureIndex = state.Confirmed ? 1 : 0;
        if (_wristCueTextureIndex != textureIndex)
        {
            Submit(_resources.WristCue, _resources.WristCueTextures[textureIndex]);
            _wristCueTextureIndex = textureIndex;
        }
        var holder = Options.Handedness == HandSide.Right ? HandSide.Left : HandSide.Right;
        var grip = (holder == HandSide.Left ? leftGrip : rightGrip)!.Value;
        SetAttachedPose(_resources.WristCue, VrWristInput.CuePose(head, grip, holder, Options.CurrentWristTapCentre), holder, left, right,
            leftIndex, rightIndex);
        Check(_overlay.ShowOverlay(_resources.WristCue), "show wrist cue");
    }

    private static void Check(EVROverlayError error, string operation)
    {
        if (error != EVROverlayError.None)
            throw new InvalidOperationException($"{operation}: {error}");
    }
}
