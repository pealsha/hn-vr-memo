namespace HandwriteMemo.Core;

/// <summary>Two fresh presses wholly outside page controls/ink. Interruptions require a valid release.</summary>
public sealed class DoubleTriggerUndo
{
    public const double MaximumIntervalSeconds = .5;
    public const double MaximumPressSeconds = .25;
    public const double MinimumReleaseSeconds = .045;
    private HandSide? _owner;
    private Guid _page;
    private long _revision;
    private double? _firstPress, _releasedAt, _lastTime;
    private bool _wasDown, _armed;

    public bool Update(HandSide? holder, Guid pageId, bool down, bool eligible, double seconds, long documentRevision = 0)
    {
        bool interrupted = holder != _owner || pageId != _page || documentRevision != _revision || !double.IsFinite(seconds) ||
            _lastTime is { } previous && (seconds < previous || seconds - previous > .2);
        _owner = holder;
        _page = pageId;
        _revision = documentRevision;
        _lastTime = seconds;
        if (interrupted || !eligible || holder is null)
        {
            _firstPress = _releasedAt = null;
            _armed = false;
            _wasDown = down;
            return false;
        }
        if (_firstPress is { } first && seconds - first > MaximumIntervalSeconds)
            _firstPress = _releasedAt = null;
        if (!down)
        {
            if (_wasDown)
            {
                if (_firstPress is { } start && seconds - start <= MaximumPressSeconds)
                    _releasedAt = seconds;
                else
                    _firstPress = _releasedAt = null;
            }
            _armed = true;
            _wasDown = false;
            return false;
        }
        if (_wasDown || !_armed)
        {
            _wasDown = true;
            return false;
        }
        _wasDown = true;
        _armed = false;
        if (_firstPress is not null && _releasedAt is { } release && seconds - release >= MinimumReleaseSeconds)
        {
            _firstPress = _releasedAt = null;
            return true;
        }
        _firstPress = seconds;
        _releasedAt = null;
        return false;
    }

    public void Reset()
    {
        _owner = null;
        _firstPress = _releasedAt = _lastTime = null;
        _wasDown = _armed = false;
    }
}
