using System.Numerics;

namespace HandwriteMemo.Core;

/// <summary>Physical dimensions of the visible board, in metres.</summary>
public readonly record struct BoardSize(float WidthMetres, float HeightMetres)
{
    public static BoardSize Default => new(BoardMetrics.ReferenceWidthMetres, BoardMetrics.ReferenceHeightMetres);

    public void Validate()
    {
        if (!float.IsFinite(WidthMetres) || !float.IsFinite(HeightMetres) || WidthMetres <= 0 || HeightMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(WidthMetres), "Board dimensions must be positive.");
    }
}

/// <summary>Local +Z faces the user; UV (0,0) is the top-left corner.</summary>
public readonly record struct BoardPose(Vector3 Position, Quaternion Rotation)
{
    public void Validate()
    {
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z) ||
            !float.IsFinite(Rotation.X) || !float.IsFinite(Rotation.Y) || !float.IsFinite(Rotation.Z) || !float.IsFinite(Rotation.W) ||
            Rotation.LengthSquared() < 1e-12f)
            throw new ArgumentOutOfRangeException(nameof(Rotation), "Board pose must contain finite values and a non-zero rotation.");
    }

    public Vector3 WorldToLocal(Vector3 worldPoint)
    {
        var rotation = Quaternion.Normalize(Rotation);
        return Vector3.Transform(worldPoint - Position, Quaternion.Inverse(rotation));
    }

    public Vector3 LocalToWorld(Vector3 localPoint) => Position + Vector3.Transform(localPoint, Quaternion.Normalize(Rotation));
    public Vector2 LocalToUv(Vector3 localPoint, BoardSize size) => new((localPoint.X / size.WidthMetres) + 0.5f, 0.5f - (localPoint.Y / size.HeightMetres));
    public Vector2 WorldToUv(Vector3 worldPoint, BoardSize size) => LocalToUv(WorldToLocal(worldPoint), size);
}

public readonly record struct TipSample(Vector3 WorldPosition, bool IsTracked);

/// <summary>Renderer-facing projected-pen state: unavailable, hovering, or writing.</summary>
public enum ContactState
{
    None,
    Proximity,
    Contact
}

public enum BoardCommand
{
    Undo,
    NewPage,
    Close,
    PageList,
    BackToPage,
    RefreshList,
    OpenPage,
    PreviousPage,
    NextPage,
    TogglePin,
    Redo
}

/// <summary>One live input point; persistence converts UV to page-space metres when capturing a snapshot.</summary>
public readonly record struct InkPoint(Vector2 Uv, double Seconds);

public sealed class Stroke
{
    private readonly List<InkPoint> _points = [];
    public IReadOnlyList<InkPoint> Points => _points;
    public string Color { get; }
    public float WidthMetres { get; }
    // Loaded lines retain their recorder clock, including after partial erasure.
    internal DateTimeOffset? RecordedStartUtc { get; init; }
    internal double RecordedStartSeconds { get; init; }

    public Stroke(string color = MemoStroke.DefaultColor, float widthMetres = MemoStroke.DefaultWidthMetres)
    {
        // Keep the live and persisted style validation identical.
        var style = new MemoStroke(DateTimeOffset.UnixEpoch, [], color, widthMetres);
        Color = style.Color;
        WidthMetres = style.WidthMetres;
    }
    internal void Add(InkPoint point) => _points.Add(point);
    /// <summary>Preserves physical ink positions while a board changes size. With rightAnchor, the right edge stays fixed.</summary>
    internal void Rebase(BoardSize previous, BoardSize next, bool rightAnchor = false)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            float xMetres = p.Uv.X * previous.WidthMetres + (rightAnchor ? next.WidthMetres - previous.WidthMetres : 0);
            _points[i] = p with
            {
                Uv = new(xMetres / next.WidthMetres,
                p.Uv.Y * previous.HeightMetres / next.HeightMetres)
            };
        }
    }
}

public readonly record struct InkSettings(float MinimumPointDistanceUv = InkSettings.DefaultMinimumPointDistanceUv)
{
    public const float DefaultMinimumPointDistanceUv = 0.0015f;
    public static InkSettings Default => new(DefaultMinimumPointDistanceUv);
    public void Validate()
    {
        if (!float.IsFinite(MinimumPointDistanceUv) || MinimumPointDistanceUv < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumPointDistanceUv));
    }
}

public readonly record struct FrameResult(
    ContactState Contact,
    Vector2? CursorUv,
    IReadOnlyList<InkPoint> AddedPoints,
    IReadOnlyList<BoardCommand> Commands,
    bool StrokeEnded,
    bool DocumentChanged = false,
    DrawingTool Tool = DrawingTool.Pen,
    int? PaletteSelection = null);
