using System.Numerics;

namespace HandwriteMemo.Core;

public enum HandSide
{
    Left,
    Right
}
public enum Grabbable
{
    Pen,
    Board,
    Eraser,
    ResizeHandle
}
public enum GrabEventKind
{
    Grabbed,
    Released
}

public readonly record struct HandInput(BoardPose Pose, bool IsTracked, bool GripDown, bool TriggerDown, bool ButtonsValid);
public readonly record struct GrabFrame(HandInput Left, HandInput Right, bool IsVisible, bool Recenter);
public readonly record struct GrabEvent(GrabEventKind Kind, HandSide Hand, Grabbable Object);
public readonly record struct ToolGrabState(BoardPose Pose, Vector3 Tip, HandSide? Holder, bool TriggerDown, bool InputAvailable);
public readonly record struct GrabFrameResult(
    BoardPose BoardPose,
    BoardSize BoardSize,
    HandSide? BoardHolder,
    HandSide? ResizeHolder,
    ToolGrabState Pen,
    ToolGrabState Eraser,
    HandSide? ToolResizeSuppressedHand,
    Grabbable? LeftCandidate,
    Grabbable? RightCandidate,
    IReadOnlyList<GrabEvent> Events);

/// <summary>All distances are metres. The pen local +Y axis points from its grip toward the tip.</summary>
public readonly record struct GrabSettings(float GrabRadiusMetres = GrabSettings.DefaultGrabRadiusMetres)
{
    public const float DefaultGrabRadiusMetres = 0.10f;
    public static GrabSettings Default => new(DefaultGrabRadiusMetres);

    public void Validate()
    {
        if (!float.IsFinite(GrabRadiusMetres) || GrabRadiusMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(GrabRadiusMetres));
    }
}

/// <summary>
/// Pure interaction state for two tracked hands. It owns no OpenVR or renderer objects.
/// A held grip that is interrupted must be released before that hand may pick up again.
/// </summary>
public sealed class GrabScene
{
    public static readonly Vector3 PenTipLocal = new(0, 0.09f, 0);
    public static readonly Vector3 PenTailLocal = new(0, -0.07f, 0);
    public static readonly Vector3 EraserTipLocal = new(0, .035f, 0);
    public static readonly Vector3 EraserHalfSize = new(.025f, .035f, .015f);
    public static BoardPose EraserDock(BoardPose penDock) => new(penDock.LocalToWorld(new Vector3(0, -.20f, 0)), penDock.Rotation);

    private readonly GrabSettings _settings;
    private BoardPose _boardPose;
    private BoardPose _penPose;
    private BoardPose _eraserPose;
    private HandSide? _penHolder;
    private HandSide? _boardHolder;
    private HandSide? _eraserHolder;
    private BoardPose _penInHand;
    private BoardPose _boardInHand;
    private BoardPose _eraserInHand;
    private readonly bool[] _lastGrip = new bool[2];
    private readonly bool[] _requireGripRelease = new bool[2];
    private bool _wasVisible = true;
    private BoardSize _boardSize = BoardSize.Default, _minimumSize = BoardSize.Default, _resizeStartSize;
    private BoardPose _resizeStartPose;
    private Vector3 _resizeStartHand;
    private HandSide? _resizeHolder;
    private bool _resizeMoved;
    private bool _resizeByTrigger;
    private readonly bool[] _lastTrigger = new bool[2];
    private readonly bool[] _requireTriggerRelease = new bool[2];
    private HandSide? _resizeSuppressedHand;
    private HandSide? _pendingResizeSuppressedHand;
    public bool ResizeEnabled { get; set; } = true;
    public HandSide Handedness { get; set; } = HandSide.Right;

    public GrabScene(BoardPose boardPose, BoardPose penDockPose, GrabSettings? settings = null)
    {
        boardPose.Validate();
        penDockPose.Validate();
        _settings = settings ?? GrabSettings.Default;
        _settings.Validate();
        _boardPose = boardPose;
        _penPose = penDockPose;
        _eraserPose = EraserDock(penDockPose);
    }

    public BoardPose BoardPose => _boardPose;
    public BoardPose PenPose => _penPose;
    public Vector3 PenTip => _penPose.LocalToWorld(PenTipLocal);
    public HandSide? PenHolder => _penHolder;
    public HandSide? BoardHolder => _boardHolder;
    public BoardPose EraserPose => _eraserPose;
    public Vector3 EraserTip => _eraserPose.LocalToWorld(EraserTipLocal);
    public HandSide? EraserHolder => _eraserHolder;
    public HandSide? ResizeHolder => _resizeHolder;
    public bool NeedsGripRelease(HandSide hand) => _requireGripRelease[(int)hand];
    public BoardSize BoardSize => _boardSize;
    public BoardPose ResizeHandlePose => new(_boardPose.LocalToWorld(BoardResize.HandleLocal(_boardSize, Handedness)), _boardPose.Rotation);

    public void SetPageSize(BoardSize size, BoardSize minimum)
    {
        size.Validate();
        minimum.Validate();
        if (size != _boardSize)
        {
            // Page changes resize around the paper's top-left, without docking tools or releasing grips.
            var shift = new Vector3((size.WidthMetres - _boardSize.WidthMetres) / 2,
                -(size.HeightMetres - _boardSize.HeightMetres) / 2, 0);
            _boardPose = _boardPose with
            {
                Position = _boardPose.LocalToWorld(shift)
            };
            if (_boardHolder is not null)
                _boardInHand = _boardInHand with
                {
                    Position = _boardInHand.LocalToWorld(shift)
                };
        }
        _boardSize = size;
        _minimumSize = minimum;
    }

    /// <summary>Replaces the board and pen dock pose, releasing held objects. A held grip remains blocked until released.</summary>
    public IReadOnlyList<GrabEvent> Reset(BoardPose boardPose, BoardPose penDockPose)
    {
        boardPose.Validate();
        penDockPose.Validate();
        var events = new List<GrabEvent>();
        ReleaseAll(events, requireGripRelease: true);
        RequireGripReleaseForBothHands();
        RequireTriggerReleaseForBothHands();
        _pendingResizeSuppressedHand = _resizeSuppressedHand;
        _boardPose = boardPose;
        _penPose = penDockPose;
        _eraserPose = EraserDock(penDockPose);
        return events;
    }

    public GrabFrameResult Update(GrabFrame frame)
    {
        var events = new List<GrabEvent>();
        _resizeSuppressedHand = _pendingResizeSuppressedHand;
        _pendingResizeSuppressedHand = null;
        if (frame.Recenter)
        {
            ReleaseAll(events, requireGripRelease: true);
            RequireGripReleaseForBothHands();
            RequireTriggerReleaseForBothHands();
        }

        if (!frame.IsVisible)
        {
            ReleaseAll(events, requireGripRelease: true);
            RequireGripReleaseForBothHands();
            RequireTriggerReleaseForBothHands();
            UpdateGripBookkeeping(frame.Left, HandSide.Left, valid: false);
            UpdateGripBookkeeping(frame.Right, HandSide.Right, valid: false);
            _wasVisible = false;
            return Result(frame, events);
        }

        if (!_wasVisible)
        {
            // Showing does not automatically pick up an already-held controller button.
            if (frame.Left.GripDown)
                _requireGripRelease[(int)HandSide.Left] = true;
            if (frame.Right.GripDown)
                _requireGripRelease[(int)HandSide.Right] = true;
        }
        _wasVisible = true;

        ProcessInterruptions(frame.Left, HandSide.Left, events);
        ProcessInterruptions(frame.Right, HandSide.Right, events);
        FollowHeld(frame.Left, HandSide.Left);
        FollowHeld(frame.Right, HandSide.Right);
        // Only objects held at the beginning of this frame can be transferred.
        // This prevents two fresh grips from passing an object across twice in one update.
        var penHolderAtFrameStart = _penHolder;
        var boardHolderAtFrameStart = _boardHolder;
        var eraserHolderAtFrameStart = _eraserHolder;
        TryGrab(frame.Left, HandSide.Left, penHolderAtFrameStart, boardHolderAtFrameStart, eraserHolderAtFrameStart, events);
        TryGrab(frame.Right, HandSide.Right, penHolderAtFrameStart, boardHolderAtFrameStart, eraserHolderAtFrameStart, events);
        TryToolResize(frame.Left, HandSide.Left, events);
        TryToolResize(frame.Right, HandSide.Right, events);
        UpdateTriggerBookkeeping(frame.Left, HandSide.Left);
        UpdateTriggerBookkeeping(frame.Right, HandSide.Right);
        UpdateGripBookkeeping(frame.Left, HandSide.Left, frame.Left.IsTracked && frame.Left.ButtonsValid);
        UpdateGripBookkeeping(frame.Right, HandSide.Right, frame.Right.IsTracked && frame.Right.ButtonsValid);
        return Result(frame, events);
    }

    private GrabFrameResult Result(GrabFrame frame, List<GrabEvent> events)
    {
        var suppressed = _resizeByTrigger ? _resizeHolder : _resizeSuppressedHand;
        return new(_boardPose, _boardSize, _boardHolder, _resizeHolder,
            ToolState(_penPose, PenTip, _penHolder), ToolState(_eraserPose, EraserTip, _eraserHolder), suppressed,
            SelectCandidate(frame.Left, HandSide.Left, canTransfer: true),
            SelectCandidate(frame.Right, HandSide.Right, canTransfer: true), events);

        ToolGrabState ToolState(BoardPose pose, Vector3 tip, HandSide? holder)
        {
            var hand = holder switch
            {
                HandSide.Left => frame.Left,
                HandSide.Right => frame.Right,
                _ => default
            };
            bool available = holder is not null && holder != suppressed && holder != _resizeSuppressedHand &&
                hand.IsTracked && hand.ButtonsValid;
            return new(pose, tip, holder, available && hand.TriggerDown, available);
        }
    }

    private void ProcessInterruptions(HandInput input, HandSide hand, List<GrabEvent> events)
    {
        var valid = input.IsTracked && input.ButtonsValid;
        if (!valid)
        {
            ReleaseHeldBy(hand, events);
            // A failed read may report GripDown=false regardless of the physical controller.
            _requireGripRelease[(int)hand] = true;
            _requireTriggerRelease[(int)hand] = true;
            return;
        }
        if (!input.GripDown)
            ReleaseHeldBy(hand, events);
        if (_resizeHolder == hand && _resizeByTrigger && (!input.TriggerDown || !ResizeEnabled))
        {
            if (!ResizeEnabled && input.TriggerDown)
                _requireTriggerRelease[(int)hand] = true;
            ReleaseResize(events);
        }
        else if (_resizeHolder == hand && !ResizeEnabled)
            ReleaseHeldBy(hand, events, requireGripRelease: true);
        if (!ResizeEnabled && input.TriggerDown)
            _requireTriggerRelease[(int)hand] = true;
    }

    private void FollowHeld(HandInput input, HandSide hand)
    {
        if (!input.IsTracked || !input.ButtonsValid)
            return;
        if (_penHolder == hand)
            _penPose = Compose(input.Pose, _penInHand);
        if (_boardHolder == hand)
            _boardPose = Compose(input.Pose, _boardInHand);
        if (_eraserHolder == hand)
            _eraserPose = Compose(input.Pose, _eraserInHand);
        if (_resizeHolder == hand)
        {
            var delta = _resizeStartPose.WorldToLocal(input.Pose.Position) - _resizeStartHand;
            if (!_resizeMoved && new Vector2(delta.X, delta.Y).Length() < BoardResize.DeadZoneMetres)
                return;
            _resizeMoved = true;
            // Five millimetre steps reduce texture reallocations and hand jitter.
            float Step(float value) => MathF.Round(value / .005f) * .005f;
            _boardSize = BoardResize.Clamp(new(Math.Max(.001f, _resizeStartSize.WidthMetres + BoardResize.Direction(Handedness) * Step(delta.X)),
                Math.Max(.001f, _resizeStartSize.HeightMetres - Step(delta.Y))), _minimumSize, _resizeStartSize);
            _boardPose = _resizeStartPose with
            {
                Position = _resizeStartPose.LocalToWorld(new(
                BoardResize.Direction(Handedness) * (_boardSize.WidthMetres - _resizeStartSize.WidthMetres) / 2,
                -(_boardSize.HeightMetres - _resizeStartSize.HeightMetres) / 2, 0))
            };
            DockFreeTools();
        }
    }

    private void TryGrab(HandInput input, HandSide hand, HandSide? penHolderAtFrameStart, HandSide? boardHolderAtFrameStart,
        HandSide? eraserHolderAtFrameStart, List<GrabEvent> events)
    {
        var index = (int)hand;
        var valid = input.IsTracked && input.ButtonsValid;
        if (!valid || !input.GripDown || _lastGrip[index] || _requireGripRelease[index] || _penHolder == hand || _boardHolder == hand || _eraserHolder == hand || _resizeHolder == hand)
            return;

        // The pen wins when both candidates are in reach, including a pen held by the other hand.
        // An object acquired earlier in this update is deliberately not transferable until the next press.
        var candidate = SelectCandidate(input, hand, canTransfer: true, penHolderAtFrameStart, boardHolderAtFrameStart,
            eraserHolderAtFrameStart, limitTransfersToFrameStart: true);
        if (input.TriggerDown && candidate is Grabbable.Pen or Grabbable.Eraser)
            _requireTriggerRelease[index] = true;
        if (candidate == Grabbable.Pen)
        {
            if (_penHolder is null)
                GrabPen(input.Pose, hand, events);
            else if (_penHolder != hand && _penHolder == penHolderAtFrameStart)
            {
                TransferPen(input.Pose, _penHolder.Value, hand, events);
            }
        }
        else if (candidate == Grabbable.Board)
        {
            if (_boardHolder is null)
                GrabBoard(input.Pose, hand, events);
            else if (_boardHolder != hand && _boardHolder == boardHolderAtFrameStart)
                TransferBoard(input.Pose, _boardHolder.Value, hand, events);
        }
        else if (candidate == Grabbable.Eraser)
        {
            if (_eraserHolder is { } oldHand)
            {
                ReleaseHeldBy(oldHand, events, requireGripRelease: true);
            }
            _eraserHolder = hand;
            _eraserInHand = RelativeTo(input.Pose, _eraserPose);
            events.Add(new(GrabEventKind.Grabbed, hand, Grabbable.Eraser));
        }
        else if (candidate == Grabbable.ResizeHandle)
        {
            StartResize(input, hand, events, byTrigger: false);
        }
    }

    private Grabbable? SelectCandidate(HandInput input, HandSide hand, bool canTransfer,
        HandSide? penHolderAtFrameStart = null, HandSide? boardHolderAtFrameStart = null,
        HandSide? eraserHolderAtFrameStart = null, bool limitTransfersToFrameStart = false)
    {
        if (!input.IsTracked || !input.ButtonsValid || _requireGripRelease[(int)hand] || _boardHolder == hand || _resizeHolder == hand)
            return null;
        if (_penHolder == hand || _eraserHolder == hand)
            return ToolAtResizeHandle(hand) ? Grabbable.ResizeHandle : null;

        var penAvailable = _penHolder is null || canTransfer && _penHolder != hand &&
            (!limitTransfersToFrameStart || penHolderAtFrameStart is not null && _penHolder == penHolderAtFrameStart);
        if (penAvailable && IsInGrabRange(DistanceToPen(input.Pose.Position)))
            return Grabbable.Pen;

        var eraserAvailable = _eraserHolder is null || canTransfer && _eraserHolder != hand &&
            (!limitTransfersToFrameStart || eraserHolderAtFrameStart is not null && _eraserHolder == eraserHolderAtFrameStart);
        if (eraserAvailable && IsInGrabRange(DistanceToEraser(input.Pose.Position)))
            return Grabbable.Eraser;

        // A resize never transfers the board to the other hand. Moving stays available on the paper.
        if (_resizeHolder is not null)
            return null;
        if (ResizeEnabled && _boardHolder is null &&
            Vector3.Distance(input.Pose.Position, ResizeHandlePose.Position) <= BoardResize.HandleRadiusMetres)
            return Grabbable.ResizeHandle;

        var boardAvailable = _boardHolder is null || canTransfer && _boardHolder != hand &&
            (!limitTransfersToFrameStart || boardHolderAtFrameStart is not null && _boardHolder == boardHolderAtFrameStart);
        return boardAvailable && IsInGrabRange(DistanceToBoard(input.Pose.Position))
            ? Grabbable.Board : null;
    }

    private bool ToolAtResizeHandle(HandSide hand)
    {
        if (!ResizeEnabled || _boardHolder is not null || _resizeHolder is not null)
            return false;
        var tip = _penHolder == hand ? PenTip : EraserTip;
        return Vector3.Distance(tip, ResizeHandlePose.Position) <= BoardResize.HandleRadiusMetres;
    }

    private void TryToolResize(HandInput input, HandSide hand, List<GrabEvent> events)
    {
        var i = (int)hand;
        if (input.IsTracked && input.ButtonsValid && input.GripDown && input.TriggerDown &&
            !_lastTrigger[i] && !_requireTriggerRelease[i] &&
            (_penHolder == hand || _eraserHolder == hand) && ToolAtResizeHandle(hand))
            StartResize(input, hand, events, byTrigger: true);
    }

    private void StartResize(HandInput input, HandSide hand, List<GrabEvent> events, bool byTrigger)
    {
        _resizeHolder = hand;
        _resizeByTrigger = byTrigger;
        _resizeMoved = false;
        _resizeStartSize = _boardSize;
        _resizeStartPose = _boardPose;
        _resizeStartHand = _boardPose.WorldToLocal(input.Pose.Position);
        events.Add(new(GrabEventKind.Grabbed, hand, Grabbable.ResizeHandle));
    }

    private void UpdateTriggerBookkeeping(HandInput input, HandSide hand)
    {
        var i = (int)hand;
        var valid = input.IsTracked && input.ButtonsValid;
        if (valid && !input.TriggerDown)
            _requireTriggerRelease[i] = false;
        _lastTrigger[i] = valid && input.TriggerDown;
    }

    private void RequireTriggerReleaseForBothHands()
    {
        _requireTriggerRelease[0] = true;
        _requireTriggerRelease[1] = true;
    }

    private bool IsInGrabRange(float distance) => distance <= _settings.GrabRadiusMetres + 1e-6f;

    private void GrabPen(BoardPose handPose, HandSide hand, List<GrabEvent> events)
    {
        _penHolder = hand;
        _penInHand = RelativeTo(handPose, _penPose);
        events.Add(new(GrabEventKind.Grabbed, hand, Grabbable.Pen));
    }

    private void GrabBoard(BoardPose handPose, HandSide hand, List<GrabEvent> events)
    {
        _boardHolder = hand;
        _boardInHand = RelativeTo(handPose, _boardPose);
        events.Add(new(GrabEventKind.Grabbed, hand, Grabbable.Board));
    }

    private void TransferPen(BoardPose newHandPose, HandSide oldHand, HandSide newHand, List<GrabEvent> events)
    {
        // _penPose is already the old holder's current world pose after FollowHeld.
        ReleaseHeldBy(oldHand, events, requireGripRelease: true);
        GrabPen(newHandPose, newHand, events);
    }

    private void TransferBoard(BoardPose newHandPose, HandSide oldHand, HandSide newHand, List<GrabEvent> events)
    {
        ReleaseHeldBy(oldHand, events, requireGripRelease: true);
        GrabBoard(newHandPose, newHand, events);
    }

    private float DistanceToPen(Vector3 point)
    {
        var tip = PenTip;
        var tail = _penPose.LocalToWorld(PenTailLocal);
        var segment = tip - tail;
        var t = Vector3.Dot(point - tail, segment) / segment.LengthSquared();
        return Vector3.Distance(point, tail + segment * Math.Clamp(t, 0, 1));
    }

    private float DistanceToBoard(Vector3 point)
    {
        var local = _boardPose.WorldToLocal(point);
        var size = _boardSize;
        var nearest = new Vector3(
            Math.Clamp(local.X, -size.WidthMetres / 2, size.WidthMetres / 2),
            Math.Clamp(local.Y, -size.HeightMetres / 2, size.HeightMetres / 2), 0);
        return Vector3.Distance(local, nearest);
    }

    private float DistanceToEraser(Vector3 point)
    {
        var local = _eraserPose.WorldToLocal(point);
        return Vector3.Distance(local, Vector3.Clamp(local, -EraserHalfSize, EraserHalfSize));
    }

    private void UpdateGripBookkeeping(HandInput input, HandSide hand, bool valid)
    {
        var i = (int)hand;
        // An unbound/invalid action is commonly reported as false. It is not evidence that
        // the user physically released the grip, so only a valid false input re-arms pickup.
        if (valid && !input.GripDown)
            _requireGripRelease[i] = false;
        _lastGrip[i] = valid && input.GripDown;
    }

    private void RequireGripReleaseForBothHands()
    {
        _requireGripRelease[(int)HandSide.Left] = true;
        _requireGripRelease[(int)HandSide.Right] = true;
    }

    private void ReleaseAll(List<GrabEvent> events, bool requireGripRelease = false)
    {
        ReleaseHeldBy(HandSide.Left, events, requireGripRelease);
        ReleaseHeldBy(HandSide.Right, events, requireGripRelease);
    }

    private void ReleaseHeldBy(HandSide hand, List<GrabEvent> events, bool requireGripRelease = false)
    {
        var held = _penHolder == hand || _boardHolder == hand || _eraserHolder == hand || _resizeHolder == hand;
        if (_penHolder == hand)
        {
            _penHolder = null;
            events.Add(new(GrabEventKind.Released, hand, Grabbable.Pen));
        }
        if (_boardHolder == hand)
        {
            _boardHolder = null;
            events.Add(new(GrabEventKind.Released, hand, Grabbable.Board));
        }
        if (_eraserHolder == hand)
        {
            _eraserHolder = null;
            events.Add(new(GrabEventKind.Released, hand, Grabbable.Eraser));
        }
        if (_resizeHolder == hand)
        {
            if (_resizeByTrigger)
                _requireTriggerRelease[(int)hand] = true;
            ReleaseResize(events);
        }
        if (held && requireGripRelease)
            _requireGripRelease[(int)hand] = true;
    }

    private void ReleaseResize(List<GrabEvent> events)
    {
        if (_resizeHolder is not { } hand)
            return;
        if (_resizeByTrigger)
            _resizeSuppressedHand = hand;
        _resizeHolder = null;
        _resizeByTrigger = false;
        events.Add(new(GrabEventKind.Released, hand, Grabbable.ResizeHandle));
    }

    private void DockFreeTools()
    {
        var dock = BoardResize.PenDock(_boardPose, _boardSize, Handedness);
        if (_penHolder is null)
            _penPose = dock;
        if (_eraserHolder is null)
            _eraserPose = EraserDock(dock);
    }

    private static BoardPose Compose(BoardPose parent, BoardPose child) => new(
        parent.LocalToWorld(child.Position), Quaternion.Normalize(parent.Rotation * child.Rotation));

    private static BoardPose RelativeTo(BoardPose parent, BoardPose child) => new(
        parent.WorldToLocal(child.Position), Quaternion.Normalize(Quaternion.Inverse(Quaternion.Normalize(parent.Rotation)) * child.Rotation));
}
