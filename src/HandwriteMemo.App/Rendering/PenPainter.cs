using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal static class PenPainter
{
    public const int FaceCount = 6;
    public const float WidthMetres = .024f;
    public const float HeightMetres = .192f;
    private const float CenterY = .01f, HalfShaft = .006f;
    public static float PixelY(float localY) => (CenterY + HeightMetres / 2 - localY) / HeightMetres * 256;
    public static float FaceWidthMetres(int face) => WidthMetres;
    public static BoardPose FacePose(BoardPose pose, int face)
    {
        var rotation = face < 4 ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, face * MathF.PI / 2) :
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, face == 4 ? -MathF.PI / 2 : MathF.PI / 2);
        var center = face < 4 ? new Vector3(0, CenterY, 0) + Vector3.Transform(Vector3.UnitZ, rotation) * HalfShaft :
            face == 4 ? GrabScene.PenTipLocal : GrabScene.PenTailLocal;
        return new(pose.LocalToWorld(center), Quaternion.Normalize(pose.Rotation * rotation));
    }

    public static Bitmap MakePen(bool highlighted = false) => MakeFace(0, highlighted);

    // Closed, separated faces prevent intersecting surfaces; padding and a broad band keep the pen readable.
    public static Bitmap MakeFace(int face, bool highlighted = false)
    {
        var image = new Bitmap(32, face < 4 ? 256 : 32, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.Transparent);
        using var shaft = new SolidBrush(highlighted ? Color.Gold : Color.Teal);
        if (face < 4)
        {
            float top = PixelY(GrabScene.PenTipLocal.Y), bottom = PixelY(GrabScene.PenTailLocal.Y);
            g.FillRectangle(shaft, 8, top, 16, bottom - top);
            g.FillRectangle(Brushes.OrangeRed, 8, top, 16, 24);
            g.FillRectangle(highlighted ? Brushes.Yellow : Brushes.WhiteSmoke, 8, PixelY(.012f), 16, 12);
        }
        else
            g.FillRectangle(face == 4 ? Brushes.OrangeRed : shaft, 8, 8, 16, 16);
        return image;
    }
}
