using System.Numerics;

namespace HandwriteMemo.Core;

/// <summary>In-memory projected-pen board interaction state, independent of rendering and persistence.</summary>
public sealed class HandwriteEngine
{
    private readonly List<Stroke> _strokes = [];
    private readonly Stack<Stroke[]> _undo = new();
    private readonly Stack<Stroke[]> _redo = new();
    public const int HistoryLimit = 100;
    private const float MinimumBoardInkMarginMetres = .002f;

    /// <summary>Session-only history. The host keeps this with the corresponding saved page.</summary>
    public sealed class EditHistory
    {
        internal Stroke[][] Undo { get; }
        internal Stroke[][] Redo { get; }
        internal EditHistory(Stroke[][] undo, Stroke[][] redo)
        {
            Undo = undo;
            Redo = redo;
        }
    }

    public EditHistory CaptureHistory() => new(_undo.ToArray(), _redo.ToArray());

    /// <summary>Call after loading the same page; history strokes use that page's current size.</summary>
    public void RestoreHistory(EditHistory history)
    {
        _undo.Clear();
        _redo.Clear();
        foreach (var state in history.Undo.Reverse())
            _undo.Push(state);
        foreach (var state in history.Redo.Reverse())
            _redo.Push(state);
        InvalidateMinimumBoardSize();
    }

    private void RecordUndo()
    {
        _undo.Push(_strokes.ToArray());
        if (_undo.Count <= HistoryLimit)
            return;
        var retained = _undo.Take(HistoryLimit).ToArray();
        _undo.Clear();
        foreach (var state in retained.Reverse())
            _undo.Push(state);
        InvalidateMinimumBoardSize();
    }
    private readonly InkSettings _settings;
    private BoardPose _pose;
    private bool _trackingEnabled = true;
    private Stroke? _activeStroke;
    private bool _erasing;
    private bool _eraseRecorded;
    private Vector2? _lastEraseUv;
    private DrawingTool _tool;
    private Origin _origin;
    private bool _triggerWasDown;
    private bool _requiresTriggerRelease;
    private bool _pageList;
    private double _lastSeconds = double.NegativeInfinity;
    // Rendering hosts use these to retain completed ink while a live stroke is appended.
    // InkRevision changes when an existing image can no longer be reused wholesale.
    private long _inkRevision;
    private long _activeStrokeRevision;
    private BoardSize? _minimumBoardSize;
    private BoardSize? _minimumBoardSizeFromRight;

    public HandwriteEngine(BoardPose pose, BoardSize? boardSize = null, InkSettings? settings = null)
    {
        BoardSize = boardSize ?? BoardSize.Default;
        BoardSize.Validate();
        _settings = settings ?? InkSettings.Default;
        _settings.Validate();
        pose.Validate();
        _pose = pose;
    }

    public BoardSize BoardSize { get; private set; }
    public IReadOnlyList<Stroke> Strokes => _strokes;
    public bool IsWriting => _activeStroke is not null;
    public bool IsErasing => _erasing;
    // Also covers a held press that left the ink area and cannot start another action.
    public bool RequiresTriggerRelease => _requiresTriggerRelease || (_triggerWasDown && _origin == Origin.None);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string PenColor { get; private set; } = MemoStroke.DefaultColor;
    public float PenWidthMetres { get; private set; } = MemoStroke.DefaultWidthMetres;
    public EraserMode EraserMode { get; private set; } = EraserMode.Stroke;
    public long InkRevision => _inkRevision;
    public long ActiveStrokeRevision => _activeStrokeRevision;
    public Stroke? ActiveStroke => _activeStroke;

    /// <summary>Appearance is captured when the next stroke starts; existing ink is never restyled.</summary>
    public void SetPenStyle(string color, float widthMetres)
    {
        var style = new Stroke(color, widthMetres);
        PenColor = style.Color;
        PenWidthMetres = style.WidthMetres;
    }

    public void SetEraserMode(EraserMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        EraserMode = mode;
    }

    public bool IsPaletteSelected(int index) => index switch
    {
        >= 0 and < 4 => PenColor == ToolPalette.Colors[index],
        >= 4 and < 7 => PenWidthMetres == ToolPalette.Widths[index - 4],
        7 => EraserMode == EraserMode.Stroke,
        8 => EraserMode == EraserMode.Partial,
        _ => false,
    };

    public void SetBoardPose(BoardPose pose)
    {
        pose.Validate();
        _pose = pose;
    }

    // Include both histories: shrinking must leave room for restored ink, its width and a small margin.
    public BoardSize MinimumBoardSize => _minimumBoardSize ??= CalculateMinimumBoardSize(rightAnchor: false);

    /// <summary>Minimum size when the board's right edge is fixed and its left edge moves.</summary>
    public BoardSize MinimumBoardSizeFromRight => _minimumBoardSizeFromRight ??= CalculateMinimumBoardSize(rightAnchor: true);

    public bool ResizeBoard(BoardSize requested, bool anchorRight = false)
    {
        requested.Validate();
        var next = BoardResize.Clamp(requested, anchorRight ? MinimumBoardSizeFromRight : MinimumBoardSize, BoardSize);
        if (next == BoardSize)
            return false;
        Interrupt();
        foreach (var stroke in HistoryStrokes())
            stroke.Rebase(BoardSize, next, anchorRight);
        BoardSize = next;
        InvalidateMinimumBoardSize();
        InvalidateInk();
        return true;
    }

    /// <summary>Prevents a trigger already held during a pen-owner change from starting ink.</summary>
    public void RequireTriggerRelease() => _requiresTriggerRelease = true;

    /// <summary>Disabling tracking finalizes ink and requires a fresh trigger release when restored.</summary>
    public FrameResult SetTracking(bool isTracked, double seconds)
    {
        CheckTime(seconds);
        _trackingEnabled = isTracked;
        return isTracked ? Empty(null) : Interrupt();
    }

    /// <summary>
    /// Clears the current in-memory page. When called after a <see cref="BoardCommand.NewPage"/>
    /// request, the host must persist the old page before calling this method.
    /// </summary>
    public void StartNewPage(double seconds)
    {
        CheckTime(seconds);
        FinishAction();
        _strokes.Clear();
        _undo.Clear();
        _redo.Clear();
        _origin = Origin.None;
        BoardSize = BoardSize.Default;
        InvalidateMinimumBoardSize();
        InvalidateInk();
    }

    /// <summary>Loads saved ink without turning old recorder times into the current app clock.</summary>
    public void LoadPage(MemoDocument document, double seconds)
    {
        ArgumentNullException.ThrowIfNull(document);
        StartNewPage(seconds);
        BoardSize = document.BoardSize;
        foreach (var line in document.Strokes)
        {
            var stroke = new Stroke(line.Color, line.WidthMetres)
            {
                RecordedStartUtc = line.StartedUtc,
                RecordedStartSeconds = line.Points.Count == 0 ? 0 : line.Points[0].ElapsedSeconds,
            };
            foreach (var point in line.Points)
                stroke.Add(new(new(point.XMetres / BoardSize.WidthMetres, point.YMetres / BoardSize.HeightMetres), point.ElapsedSeconds));
            _strokes.Add(stroke);
        }
        _triggerWasDown = false;
        _requiresTriggerRelease = false;
        InvalidateMinimumBoardSize();
        InvalidateInk();
    }

    /// <summary>Projects a front-side pen onto the board. Ink begins only on a fresh trigger press.</summary>
    public FrameResult UpdateProjectedTip(TipSample sample, bool triggerDown, float maxDistanceMetres, double seconds,
        DrawingTool tool = DrawingTool.Pen, bool pageList = false)
    {
        if (!float.IsFinite(maxDistanceMetres) || maxDistanceMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDistanceMetres));
        if (!_trackingEnabled || !sample.IsTracked)
        {
            CheckTime(seconds);
            var ended = FinishAction();
            _requiresTriggerRelease |= triggerDown;
            _triggerWasDown = triggerDown;
            _origin = Origin.None;
            return new(ContactState.None, null, [], [], ended, Tool: tool);
        }

        var local = _pose.WorldToLocal(sample.WorldPosition);
        var uv = _pose.LocalToUv(local, BoardSize);
        var inBoard = IsInBoard(uv);
        var inDistance = local.Z >= 0 && local.Z <= maxDistanceMetres;
        Vector2? cursor = inDistance && inBoard ? uv : null;
        return UpdatePagePoint(cursor, triggerDown, seconds, tool, pageList);
    }

    /// <summary>
    /// Shared page input for VR projection and desktop pointer input. UV is top-left 0..1;
    /// The VR palette is above the paper (negative Y); only its visible tiles are active there.
    /// null means unavailable/outside. A press must start inside the ink area or on a button.
    /// In pageList mode only browser controls are active; ink, erasure and the palette are suppressed.
    /// </summary>
    public FrameResult UpdatePagePoint(Vector2? cursor, bool triggerDown, double seconds, DrawingTool tool = DrawingTool.Pen,
        bool pageList = false)
    {
        CheckTime(seconds);
        if (!Enum.IsDefined(tool))
            throw new ArgumentOutOfRangeException(nameof(tool));
        bool endedByToolChange = false;
        if (_pageList != pageList)
        {
            endedByToolChange = FinishAction();
            _requiresTriggerRelease |= _triggerWasDown;
            _origin = Origin.None;
            _pageList = pageList;
        }
        if (_tool != tool)
        {
            endedByToolChange |= FinishAction();
            _requiresTriggerRelease |= _triggerWasDown;
            _origin = Origin.None;
            _tool = tool;
        }
        if (!_trackingEnabled || cursor is not { } uv || !IsInBoard(uv))
        {
            var ended = FinishAction() || endedByToolChange;
            if (!_trackingEnabled)
                _requiresTriggerRelease |= triggerDown;
            else if (!triggerDown)
                _requiresTriggerRelease = false;
            _triggerWasDown = triggerDown;
            _origin = Origin.None;
            return new(ContactState.None, null, [], [], ended, Tool: tool);
        }
        var hoverState = ContactState.Proximity;

        if (!triggerDown)
        {
            _requiresTriggerRelease = false;
            _triggerWasDown = false;
            var ended = FinishAction() || endedByToolChange;
            _origin = Origin.None;
            return new(hoverState, cursor, [], [], ended, Tool: tool);
        }

        var triggerPressed = !_triggerWasDown;
        _triggerWasDown = true;
        if (_requiresTriggerRelease)
        {
            var ended = FinishAction() || endedByToolChange;
            _origin = Origin.None;
            return new(ContactState.None, cursor, [], [], ended, Tool: tool);
        }

        var added = new List<InkPoint>();
        var commands = new List<BoardCommand>();
        var endedStroke = endedByToolChange;
        var changed = false;
        int? paletteSelection = null;
        if (triggerPressed)
        {
            _origin = Classify(uv);
            if (_origin == Origin.Ink)
            {
                if (tool == DrawingTool.Pen)
                    StartStroke(uv, seconds, added);
                else
                {
                    _erasing = true;
                    changed = EraseAt(uv);
                }
            }
            else if (_origin == Origin.Palette)
            {
                paletteSelection = ToolPalette.HitTest(uv, BoardSize);
                SelectPalette(paletteSelection.Value);
            }
            else if (_origin == Origin.Command && BoardControls.HitTest(uv, pageList, BoardSize) is { } command)
            {
                if (command == BoardCommand.Undo)
                    changed = Undo();
                else if (command == BoardCommand.Redo)
                    changed = Redo();
                commands.Add(command);
            }
        }
        else if (_origin == Origin.Ink && (IsWriting || IsErasing))
        {
            if (Classify(uv) == Origin.Ink)
            {
                if (tool == DrawingTool.Pen)
                    AddPointIfFarEnough(uv, seconds, added);
                else
                    changed = EraseAt(uv);
            }
            else
                endedStroke = FinishAction();
        }

        return new(IsWriting || IsErasing ? ContactState.Contact : hoverState, cursor, added, commands, endedStroke,
            changed || added.Count > 0, tool, paletteSelection);
    }

    private FrameResult Interrupt()
    {
        var ended = FinishAction();
        _origin = Origin.None;
        _requiresTriggerRelease |= _triggerWasDown;
        return new(ContactState.None, null, [], [], ended);
    }

    private void StartStroke(Vector2 uv, double seconds, List<InkPoint> added)
    {
        bool discardedRedo = _redo.Count > 0;
        RecordUndo();
        _redo.Clear();
        _activeStroke = new Stroke(PenColor, PenWidthMetres);
        _strokes.Add(_activeStroke);
        if (discardedRedo)
            InvalidateMinimumBoardSize();
        AddPointIfFarEnough(uv, seconds, added);
    }

    private void AddPointIfFarEnough(Vector2 uv, double seconds, List<InkPoint> added)
    {
        if (_activeStroke is null ||
            _activeStroke.Points.Count > 0 && Vector2.DistanceSquared(_activeStroke.Points[^1].Uv, uv) < _settings.MinimumPointDistanceUv * _settings.MinimumPointDistanceUv)
            return;
        var point = new InkPoint(uv, seconds);
        _activeStroke.Add(point);
        UpdateMinimumBoardSize(point, _activeStroke);
        _activeStrokeRevision++;
        added.Add(point);
    }

    private bool FinishAction()
    {
        var wasActive = IsWriting || IsErasing;
        _activeStroke = null;
        _erasing = false;
        _eraseRecorded = false;
        _lastEraseUv = null;
        return wasActive;
    }

    /// <summary>Restores one pen stroke or eraser gesture. A no-op erase creates no history entry.</summary>
    public bool Undo()
    {
        FinishAction();
        _origin = Origin.None;
        if (!_undo.TryPop(out var previous))
            return false;
        _redo.Push(_strokes.ToArray());
        _strokes.Clear();
        _strokes.AddRange(previous);
        InvalidateMinimumBoardSize();
        InvalidateInk();
        return true;
    }

    /// <summary>Reapplies one undone gesture. Only a new ink edit discards redo history.</summary>
    public bool Redo()
    {
        FinishAction();
        _origin = Origin.None;
        if (!_redo.TryPop(out var next))
            return false;
        RecordUndo();
        _strokes.Clear();
        _strokes.AddRange(next);
        InvalidateMinimumBoardSize();
        InvalidateInk();
        return true;
    }

    private IEnumerable<Stroke> HistoryStrokes() =>
        _strokes.Concat(_undo.SelectMany(s => s)).Concat(_redo.SelectMany(s => s)).Distinct();

    private bool EraseAt(Vector2 uv)
    {
        var replacement = StrokeEraser.Erase(_strokes, BoardSize, _lastEraseUv ?? uv, uv,
            ToolPalette.EraserRadiusMetres, EraserMode);
        _lastEraseUv = uv;
        if (replacement is null)
            return false;
        if (!_eraseRecorded)
        {
            RecordUndo();
            _redo.Clear();
            _eraseRecorded = true;
        }
        _strokes.Clear();
        _strokes.AddRange(replacement);
        InvalidateMinimumBoardSize();
        InvalidateInk();
        return true;
    }

    private void InvalidateInk()
    {
        _inkRevision++;
        _activeStrokeRevision++;
    }

    private void InvalidateMinimumBoardSize()
    {
        _minimumBoardSize = null;
        _minimumBoardSizeFromRight = null;
    }

    private BoardSize CalculateMinimumBoardSize(bool rightAnchor)
    {
        float w = BoardSize.Default.WidthMetres, h = BoardSize.Default.HeightMetres;
        foreach (var stroke in HistoryStrokes())
            foreach (var point in stroke.Points)
            {
                float x = point.Uv.X * BoardSize.WidthMetres;
                w = Math.Max(w, (rightAnchor ? BoardSize.WidthMetres - x : x) + stroke.WidthMetres / 2 + MinimumBoardInkMarginMetres);
                h = Math.Max(h, point.Uv.Y * BoardSize.HeightMetres + stroke.WidthMetres / 2 + MinimumBoardInkMarginMetres);
            }
        return new(w, h);
    }

    // Adding a point cannot reduce either bound. If a caller has already requested the cached
    // minimum during this stroke, update it in O(1) instead of rescanning the page every frame.
    private void UpdateMinimumBoardSize(InkPoint point, Stroke stroke)
    {
        float x = point.Uv.X * BoardSize.WidthMetres;
        float y = point.Uv.Y * BoardSize.HeightMetres + stroke.WidthMetres / 2 + MinimumBoardInkMarginMetres;
        if (_minimumBoardSize is { } left)
            _minimumBoardSize = new(Math.Max(left.WidthMetres, x + stroke.WidthMetres / 2 + MinimumBoardInkMarginMetres), Math.Max(left.HeightMetres, y));
        if (_minimumBoardSizeFromRight is { } right)
            _minimumBoardSizeFromRight = new(Math.Max(right.WidthMetres, BoardSize.WidthMetres - x + stroke.WidthMetres / 2 + MinimumBoardInkMarginMetres), Math.Max(right.HeightMetres, y));
    }

    private void SelectPalette(int index)
    {
        if (index is >= 0 and < 4)
            SetPenStyle(ToolPalette.Colors[index], PenWidthMetres);
        else if (index is >= 4 and < 7)
            SetPenStyle(PenColor, ToolPalette.Widths[index - 4]);
        else if (index == 7)
            SetEraserMode(EraserMode.Stroke);
        else if (index == 8)
            SetEraserMode(EraserMode.Partial);
    }

    private Origin Classify(Vector2 uv) => BoardControls.HitTest(uv, _pageList, BoardSize) is not null ? Origin.Command :
        !_pageList && ToolPalette.HitTest(uv, BoardSize) >= 0 ? Origin.Palette :
        _pageList || uv.Y < 0 || uv.Y > 1 ? Origin.None : Origin.Ink;

    private bool IsInBoard(Vector2 uv) => (uv.X is >= 0 and <= 1 && uv.Y is >= 0 and <= 1) ||
        BoardControls.HitTest(uv, _pageList, BoardSize) is not null ||
        (!_pageList && ToolPalette.HitTest(uv, BoardSize) >= 0);
    private FrameResult Empty(Vector2? cursor) => new(ContactState.None, cursor, [], [], false);

    private void CheckTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < _lastSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Timestamps must be finite and monotonic.");
        _lastSeconds = seconds;
    }

    private enum Origin
    {
        None,
        Ink,
        Command,
        Palette
    }
}
