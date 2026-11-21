using System.Collections.ObjectModel;

namespace HandwriteMemo.Core;

/// <summary>An immutable v2 page snapshot. Positions and widths are in metres.</summary>
public sealed class MemoDocument
{
    public const int CurrentFormatVersion = 2;
    private readonly ReadOnlyCollection<MemoStroke> _strokes;

    public int FormatVersion { get; }
    public Guid Id { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset UpdatedUtc { get; }
    public BoardSize BoardSize { get; }
    public IReadOnlyList<MemoStroke> Strokes => _strokes;
    public bool IsPinned { get; }
    public string Title { get; }
    // Labels are single-line; the original title is retained in JSON and the editor.
    public string DisplayName => DisplayTitle(Title, CreatedUtc);
    public static string DisplayTitle(string title, DateTimeOffset createdUtc) => title.Length == 0
        ? createdUtc.ToLocalTime().ToString("yyyy/MM/dd  HH:mm") : title.ReplaceLineEndings(" ").Replace('\t', ' ');

    public MemoDocument(int formatVersion, Guid id, DateTimeOffset createdUtc, DateTimeOffset updatedUtc,
        BoardSize boardSize, IReadOnlyList<MemoStroke> strokes, bool isPinned = false, string? title = null)
    {
        if (formatVersion != CurrentFormatVersion)
            throw new NotSupportedException($"Memo format version {formatVersion} is not supported.");
        if (id == Guid.Empty)
            throw new ArgumentException("A memo id is required.", nameof(id));
        if (createdUtc.Offset != TimeSpan.Zero || updatedUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Memo timestamps must be UTC.");
        if (updatedUtc < createdUtc)
            throw new ArgumentOutOfRangeException(nameof(updatedUtc), "Updated time cannot precede creation.");
        boardSize.Validate();
        ArgumentNullException.ThrowIfNull(strokes);
        var copied = strokes.Select(s => s is null
            ? throw new ArgumentException("A stroke cannot be null.", nameof(strokes))
            : new MemoStroke(s.StartedUtc, s.Points, s.Color, s.WidthMetres)).ToList();
        foreach (var stroke in copied)
        {
            // ElapsedSeconds is relative to the recorder clock, NOT StartedUtc.
            double duration = stroke.Points.Count == 0 ? 0 : stroke.Points[^1].ElapsedSeconds - stroke.Points[0].ElapsedSeconds;
            if (stroke.StartedUtc < createdUtc || stroke.StartedUtc > updatedUtc ||
                duration > (updatedUtc - stroke.StartedUtc).TotalSeconds + 1e-7)
                throw new ArgumentOutOfRangeException(nameof(strokes), "Stroke times must be inside the page lifetime.");
        }
        FormatVersion = formatVersion;
        Id = id;
        CreatedUtc = createdUtc;
        UpdatedUtc = updatedUtc;
        BoardSize = boardSize;
        IsPinned = isPinned;
        Title = title ?? "";
        _strokes = copied.AsReadOnly();
    }

    /// <summary>Converts the current UV input to physical page coordinates without changing live input.</summary>
    public static MemoDocument Capture(Guid id, DateTimeOffset createdUtc, DateTimeOffset clockOriginUtc,
        BoardSize size, IReadOnlyList<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        size.Validate();
        var created = Utc(createdUtc, nameof(createdUtc));
        var origin = Utc(clockOriginUtc, nameof(clockOriginUtc));
        var copied = new List<MemoStroke>(strokes.Count);
        var latest = created;
        foreach (var stroke in strokes)
        {
            ArgumentNullException.ThrowIfNull(stroke);
            var points = stroke.Points.Select(p => new MemoPoint(p.Uv.X * size.WidthMetres, p.Uv.Y * size.HeightMetres, p.Seconds)).ToArray();
            var recorder = stroke.RecordedStartUtc ?? origin;
            double offset = stroke.RecordedStartUtc.HasValue ? stroke.RecordedStartSeconds : 0;
            var started = points.Length == 0 ? recorder : recorder.AddSeconds(points[0].ElapsedSeconds - offset);
            var ended = points.Length == 0 ? started : recorder.AddSeconds(points[^1].ElapsedSeconds - offset);
            if (started < created)
                throw new ArgumentOutOfRangeException(nameof(strokes), "Stroke time precedes page creation.");
            if (started > latest)
                latest = started;
            if (ended > latest)
                latest = ended;
            copied.Add(new MemoStroke(started, points, stroke.Color, stroke.WidthMetres));
        }
        // Undo must not move UpdatedUtc back to the newest surviving ink point.
        var updated = DateTimeOffset.UtcNow;
        if (latest > updated)
            updated = latest;
        return new MemoDocument(CurrentFormatVersion, id, created, updated, size, copied);
    }

    private static DateTimeOffset Utc(DateTimeOffset value, string name) => value.Offset == TimeSpan.Zero
        ? value : throw new ArgumentException("Timestamp must be UTC.", name);
}

/// <summary>One continuous line, with its own immutable appearance and points.</summary>
public sealed class MemoStroke
{
    public const string DefaultColor = "#182634";
    public const float DefaultWidthMetres = .0015f;
    private readonly ReadOnlyCollection<MemoPoint> _points;
    public DateTimeOffset StartedUtc { get; }
    public IReadOnlyList<MemoPoint> Points => _points;
    public string Color { get; }
    public float WidthMetres { get; }

    public MemoStroke(DateTimeOffset startedUtc, IReadOnlyList<MemoPoint> points,
        string color = DefaultColor, float widthMetres = DefaultWidthMetres)
    {
        if (startedUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Stroke timestamp must be UTC.", nameof(startedUtc));
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(color);
        if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit))
            throw new ArgumentException("Color must be a #RRGGBB value.", nameof(color));
        if (!float.IsFinite(widthMetres) || widthMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthMetres));
        var copied = points.Select(p => p.Validate()).ToList();
        if (copied.Zip(copied.Skip(1), (a, b) => b.ElapsedSeconds < a.ElapsedSeconds).Any(backwards => backwards))
            throw new ArgumentException("Point elapsed times must not go backwards.", nameof(points));
        StartedUtc = startedUtc;
        Color = color.ToUpperInvariant();
        WidthMetres = widthMetres;
        _points = copied.AsReadOnly();
    }
}

/// <summary>Top-left page origin, X right and Y down in metres; elapsed seconds retain the recorder clock.</summary>
public readonly record struct MemoPoint(float XMetres, float YMetres, double ElapsedSeconds)
{
    internal MemoPoint Validate()
    {
        if (!float.IsFinite(XMetres) || !float.IsFinite(YMetres) || !double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(XMetres), "Points require finite coordinates and non-negative elapsed seconds.");
        return this;
    }
}
