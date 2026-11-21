using System.Numerics;

namespace HandwriteMemo.Core;

// Names are persisted in settings.json; Touch now means a single completed tap.
public enum WristButtonActivationMode { DoubleTap, Touch }

[Flags]
public enum WristTapEvent { None = 0, ContactStarted = 1, ContactEnded = 2, CueStarted = 4, Confirmed = 8, Cancelled = 16 }

public readonly record struct WristTapState(bool Visible, bool Confirmed, bool Triggered, bool Pending,
    WristTapEvent Events, string? Reason);

public readonly record struct WristTapMeasurements(float Gaze, float PalmFacing, float HeadDistance,
    float FingerDistance, Vector3 FingerInGrip, bool Looking, BoardPose HeadPose, BoardPose GripPose);

/// <summary>Recognizes look-and-tap from poses and a monotonic clock, without VR or rendering dependencies.</summary>
public sealed class WristTapGesture
{
    public const float MinimumGazeDot = .1f, MinimumPalmFacingDot = .1f;
    public const float MinimumHeadDistanceMetres = .15f, MaximumHeadDistanceMetres = 1f;
    public static Vector3 ContactCentre => new(.141f, .192f, -.023f);
    // Repeated taps land a few centimetres apart even after calibration. The release distance (radius + margin)
    // stays at 6.5cm because a fingertip resting just beyond it between taps must still re-arm.
    public const float ContactRadiusMetres = .05f;
    public static Vector3 ContactHalfSize => new(ContactRadiusMetres);
    public const float ReleaseMarginMetres = .015f, DiagnosticNearDistanceMetres = .5f;
    public const float MinimumReturnDirectionDot = .5f;
    public const double LookMinimumSeconds = .10, ContactMaximumSeconds = .5, PairMaximumSeconds = 1.6;
    public const double OutsideMinimumSeconds = .06, CooldownSeconds = .8, MaximumFrameGapSeconds = .2;
    public const double ConfirmationSeconds = .25;
    private double? _lastSeconds, _lookSince, _outsideSince, _contactSince, _firstEntry;
    private double _cooldownUntil = double.NegativeInfinity, _confirmedUntil = double.NegativeInfinity;
    private bool _armed, _secondContact;
    private Vector3 _lastOutside, _entryDirection;
    private WristButtonActivationMode _mode;
    private Vector3? _centre;
    public WristTapMeasurements? Measurements { get; private set; }
    public float? ReturnDirectionDot { get; private set; }

    // A missing sample cannot cancel the refractory period after a completed gesture.
    public void Reset()
    {
        _lastSeconds = _lookSince = null;
        _confirmedUntil = double.NegativeInfinity;
        Measurements = null;
        ReturnDirectionDot = null;
        ClearTap();
    }

    private void ClearTap()
    {
        _armed = _secondContact = false;
        _lastOutside = _entryDirection = Vector3.Zero;
        _outsideSince = _contactSince = _firstEntry = null;
    }

    public WristTapState Update(BoardPose head, BoardPose grip, HandSide wristHand, Vector3 finger,
        bool available, double seconds, WristButtonActivationMode mode = WristButtonActivationMode.DoubleTap, Vector3? contactCentre = null)
    {
        bool interrupted = _firstEntry is not null || double.IsFinite(_confirmedUntil);
        string? invalid = !available ? "unavailable" : !Enum.IsDefined(mode) || !Enum.IsDefined(wristHand) ? "invalid mode/hand" :
            !double.IsFinite(seconds) || contactCentre is { } centre && (!Finite(centre) || !float.IsFinite(centre.LengthSquared())) ||
                !Valid(head) || !Valid(grip) || !Finite(finger) ? "invalid sample" :
            _lastSeconds is { } last && (seconds <= last || seconds - last > MaximumFrameGapSeconds) ? "clock discontinuity" :
            _mode != mode ? "mode changed" : _centre != contactCentre ? "centre changed" : null;
        if (invalid is not null)
        {
            Reset();
            _mode = mode;
            _centre = contactCentre;
            if (double.IsFinite(seconds))
                _lastSeconds = seconds;
            return new(false, false, false, false, interrupted ? WristTapEvent.Cancelled : WristTapEvent.None, invalid);
        }
        _lastSeconds = seconds;
        var toWrist = grip.Position - head.Position;
        float distance = toWrist.Length();
        var local = grip.WorldToLocal(finger);
        if (!float.IsFinite(distance) || distance < .001f || !Finite(local) || !float.IsFinite(local.LengthSquared()))
        {
            Reset();
            return new(false, false, false, false, WristTapEvent.Cancelled, "invalid geometry");
        }
        var direction = toWrist / distance;
        float gaze = Vector3.Dot(Vector3.Transform(-Vector3.UnitZ, Quaternion.Normalize(head.Rotation)), direction);
        float facing = Vector3.Dot(Vector3.Transform(wristHand == HandSide.Left ? Vector3.UnitX : -Vector3.UnitX,
            Quaternion.Normalize(grip.Rotation)), -direction);
        bool looking = distance >= MinimumHeadDistanceMetres && distance <= MaximumHeadDistanceMetres &&
            gaze >= MinimumGazeDot && facing >= MinimumPalmFacingDot;
        Measurements = new(gaze, facing, distance, local.Length(), local, looking, head, grip);
        if (!looking)
        {
            _lookSince = null;
            _confirmedUntil = double.NegativeInfinity;
            ClearTap();
            return new(false, false, false, false, interrupted ? WristTapEvent.Cancelled : WristTapEvent.None, "look away");
        }
        _lookSince ??= seconds;
        var p = local with { X = wristHand == HandSide.Left ? local.X : -local.X };
        var centrePoint = contactCentre ?? ContactCentre;
        float distanceSquared = Vector3.DistanceSquared(p, centrePoint);
        bool inside = distanceSquared <= ContactRadiusMetres * ContactRadiusMetres;
        bool outside = distanceSquared > (ContactRadiusMetres + ReleaseMarginMetres) * (ContactRadiusMetres + ReleaseMarginMetres);
        WristTapEvent events = WristTapEvent.None;
        string? reason = null;
        if (_firstEntry is { } first && seconds - first > PairMaximumSeconds ||
            _contactSince is { } contact && seconds - contact > ContactMaximumSeconds)
        {
            reason = _contactSince is { } start && seconds - start > ContactMaximumSeconds ? "contact too long" : "pair timeout";
            events |= WristTapEvent.Cancelled;
            ClearTap();
        }
        if (seconds < _cooldownUntil || seconds - _lookSince.Value < LookMinimumSeconds)
        {
            ClearTap();
            return State(seconds, false, events, seconds < _cooldownUntil ? "cooldown" : "look settling");
        }
        bool triggered = false;
        if (outside)
        {
            if (_contactSince is not null)
            {
                events |= WristTapEvent.ContactEnded;
                _contactSince = null;
                ReturnDirectionDot = Vector3.Dot(_entryDirection, RegionDirection(p, centrePoint));
                if (ReturnDirectionDot < MinimumReturnDirectionDot)
                {
                    ClearTap();
                    return State(seconds, false, events | WristTapEvent.Cancelled, "crossed wrist region");
                }
                if (mode == WristButtonActivationMode.Touch || _secondContact)
                {
                    triggered = true;
                    events |= WristTapEvent.Confirmed;
                    _confirmedUntil = seconds + ConfirmationSeconds;
                    _cooldownUntil = seconds + CooldownSeconds;
                    ClearTap();
                }
            }
            if (!triggered)
            {
                _outsideSince ??= seconds;
                _lastOutside = p;
                _armed = seconds - _outsideSince.Value >= OutsideMinimumSeconds;
            }
        }
        else if (inside && _contactSince is null)
        {
            if (_armed)
            {
                _secondContact = _firstEntry is not null;
                _entryDirection = RegionDirection(_lastOutside, centrePoint);
                ReturnDirectionDot = null;
                _firstEntry ??= seconds;
                _contactSince = seconds;
                events |= WristTapEvent.ContactStarted;
                if (!_secondContact && mode == WristButtonActivationMode.DoubleTap)
                    events |= WristTapEvent.CueStarted;
            }
            _armed = false;
            _outsideSince = null;
        }
        return State(seconds, triggered, events, reason);
    }

    private WristTapState State(double seconds, bool triggered, WristTapEvent events, string? reason)
    {
        bool pending = _firstEntry is not null && _mode == WristButtonActivationMode.DoubleTap;
        bool confirmed = seconds < _confirmedUntil;
        return new(pending || confirmed, confirmed, triggered, pending, events, reason);
    }

    private static Vector3 RegionDirection(Vector3 point, Vector3 centre) => Vector3.Normalize(point - centre);
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool Valid(BoardPose p) => Finite(p.Position) &&
        float.IsFinite(p.Rotation.X) && float.IsFinite(p.Rotation.Y) && float.IsFinite(p.Rotation.Z) && float.IsFinite(p.Rotation.W) &&
        float.IsFinite(p.Rotation.LengthSquared()) && p.Rotation.LengthSquared() > 1e-12f;
}
