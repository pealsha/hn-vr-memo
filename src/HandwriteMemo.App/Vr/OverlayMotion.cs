using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// The grip action is NOT the raw device origin (Quest's difference was about 9.7cm).
// Capture their rigid relationship on pickup; never feed sampled world motion back into a held overlay.
internal sealed class OverlayMotion
{
    private HandSide? _owner;
    private uint _device;
    private BoardPose _gripInDevice;

    public BoardPose RelativePose(BoardPose world, HandSide holder, uint index, BoardPose rawDevice, BoardPose grip)
    {
        if (_owner != holder || _device != index)
        {
            _owner = holder;
            _device = index;
            _gripInDevice = Relative(rawDevice, grip);
        }
        return Compose(_gripInDevice, Relative(grip, world));
    }

    public void Release() => _owner = null;
    internal static BoardPose Relative(BoardPose parent, BoardPose child) => new(parent.WorldToLocal(child.Position),
        Quaternion.Normalize(Quaternion.Inverse(parent.Rotation) * child.Rotation));
    internal static BoardPose Compose(BoardPose parent, BoardPose child) => new(parent.LocalToWorld(child.Position),
        Quaternion.Normalize(parent.Rotation * child.Rotation));
    internal static float PredictionSeconds(float hz, float sinceVsync, float toPhotons) =>
        Math.Clamp(1 / hz - sinceVsync + toPhotons, 0, .05f);
}
