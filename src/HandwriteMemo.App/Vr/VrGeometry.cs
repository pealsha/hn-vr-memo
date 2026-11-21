using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

internal static class VrGeometry
{
    // OpenVR uses column-vector 3x4 transforms; System.Numerics uses row vectors.
    public static BoardPose FromOpenVr(HmdMatrix34_t m)
    {
        var rotation = new Matrix4x4(
            m.m0, m.m4, m.m8, 0,
            m.m1, m.m5, m.m9, 0,
            m.m2, m.m6, m.m10, 0,
            0, 0, 0, 1);
        return new BoardPose(new Vector3(m.m3, m.m7, m.m11), Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation)));
    }

    public static HmdMatrix34_t ToOpenVr(BoardPose pose)
    {
        var m = Matrix4x4.CreateFromQuaternion(pose.Rotation);
        return new HmdMatrix34_t
        {
            m0 = m.M11,
            m1 = m.M21,
            m2 = m.M31,
            m3 = pose.Position.X,
            m4 = m.M12,
            m5 = m.M22,
            m6 = m.M32,
            m7 = pose.Position.Y,
            m8 = m.M13,
            m9 = m.M23,
            m10 = m.M33,
            m11 = pose.Position.Z
        };
    }

    public static BoardPose PlaceBoard(BoardPose head, AppOptions options, BoardSize? pageSize = null)
    {
        var forward = Vector3.Transform(-Vector3.UnitZ, head.Rotation);
        forward.Y = 0;
        if (forward.LengthSquared() < 0.01f)
            throw new InvalidOperationException("Look roughly forward before recentering.");
        forward = Vector3.Normalize(forward);
        var yaw = MathF.Atan2(-forward.X, -forward.Z);
        var size = pageSize ?? BoardSize.Default;
        // Retain the familiar viewing angle without putting the enlarged top edge at eye level.
        float scale = Math.Max(1, Math.Max(size.WidthMetres / BoardSize.Default.WidthMetres, size.HeightMetres / BoardSize.Default.HeightMetres));
        float distance = options.BoardDistanceMeters * MathF.Sqrt(scale);
        var tilt = -MathF.Atan2(options.BoardDownMeters, distance);
        return new BoardPose(head.Position + forward * distance - Vector3.UnitY * options.BoardDownMeters,
            Quaternion.CreateFromYawPitchRoll(yaw, tilt, 0));
    }
}
