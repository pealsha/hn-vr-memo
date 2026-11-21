using System.Numerics;

namespace HandwriteMemo.Core;

public enum WristCalibrationStatus { Idle, Preparing, Collecting, Retry, Succeeded, TimedOut, Cancelled }

/// <summary>Collects a stationary fingertip in grip coordinates using a monotonic clock.</summary>
public sealed class WristCalibration
{
    public const double PreparationSeconds = 3, CollectionSeconds = 2, TimeoutSeconds = 30, MaximumFrameGapSeconds = .2;
    public const float MaximumMovementMetres = .015f;
    private double _started, _last, _collectionStart;
    private Vector3 _anchor, _sum;
    private int _count;
    private HandSide _hand;
    public WristCalibrationStatus Status { get; private set; }
    public Vector3? ResultCentre { get; private set; }

    public void Start(double seconds, HandSide hand)
    {
        ResultCentre = null;
        if (!double.IsFinite(seconds) || !Enum.IsDefined(hand))
        {
            Status = WristCalibrationStatus.Cancelled;
            return;
        }
        _started = _last = seconds;
        _hand = hand;
        _count = 0;
        Status = WristCalibrationStatus.Preparing;
    }

    public void Cancel()
    {
        _count = 0;
        ResultCentre = null;
        Status = WristCalibrationStatus.Cancelled;
    }

    public WristCalibrationStatus Update(double seconds, HandSide hand, Vector3? fingerInGrip)
    {
        if (Status is WristCalibrationStatus.Idle or WristCalibrationStatus.Succeeded or WristCalibrationStatus.TimedOut or WristCalibrationStatus.Cancelled)
            return Status;
        if (!double.IsFinite(seconds) || seconds <= _last || hand != _hand || !Enum.IsDefined(hand))
        {
            Cancel();
            return Status;
        }
        bool gap = seconds - _last > MaximumFrameGapSeconds;
        _last = seconds;
        if (seconds - _started >= TimeoutSeconds)
            return Status = WristCalibrationStatus.TimedOut;
        if (seconds - _started < PreparationSeconds)
            return Status = WristCalibrationStatus.Preparing;
        if (gap || fingerInGrip is not { } p || !Finite(p) || !float.IsFinite(p.LengthSquared()))
        {
            _count = 0;
            return Status = WristCalibrationStatus.Retry;
        }
        if (hand == HandSide.Right)
            p.X = -p.X;
        if (_count > 0 && Vector3.DistanceSquared(p, _anchor) > MaximumMovementMetres * MaximumMovementMetres)
        {
            _count = 0;
            return Status = WristCalibrationStatus.Retry;
        }
        if (_count == 0)
        {
            _anchor = p;
            _sum = Vector3.Zero;
            _collectionStart = seconds;
        }
        _sum += p;
        _count++;
        if (seconds - _collectionStart >= CollectionSeconds)
        {
            ResultCentre = _sum / _count;
            return Status = WristCalibrationStatus.Succeeded;
        }
        return Status = WristCalibrationStatus.Collecting;
    }

    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
