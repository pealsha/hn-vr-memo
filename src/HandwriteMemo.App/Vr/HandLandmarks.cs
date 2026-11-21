using System.Numerics;
using System.Text.Json.Serialization;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed record OffsetMeters(float X = 0, float Y = 0, float Z = 0)
{
    [JsonIgnore] public Vector3 Vector => new(X, Y, Z);
    public void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || Vector.Length() > .25f)
            throw new ArgumentException("Hand offsets must be finite and within 0.25 metres.");
    }
}

internal static class HandLandmarks
{
    public static BoardPose Palm(BoardPose gripPose, OffsetMeters offset) =>
        new(gripPose.LocalToWorld(offset.Vector), gripPose.Rotation);
    private static Vector3 Mirror(Vector3 offset, AppOptions options) => options.Handedness == HandSide.Left ? offset with { X = -offset.X } : offset;
    public static Vector3 FingerTip(BoardPose palm, AppOptions options) => palm.LocalToWorld(Mirror(options.RightIndexTipOffset.Vector, options));
}
