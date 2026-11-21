using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

/// <summary>Six overlay faces form a block, visible from every direction. Local +Y is the erasing tip.</summary>
internal static class EraserPainter
{
    public const int FaceCount = 6;
    public static float FaceWidthMetres(int face) => face is 1 or 3 ? .03f : .05f;
    public static float FaceHeightMetres(int face) => face < 4 ? .07f : .03f;

    public static BoardPose FacePose(BoardPose pose, int face)
    {
        var rotation = face < 4 ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, face * MathF.PI / 2) :
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, face == 4 ? -MathF.PI / 2 : MathF.PI / 2);
        var normal = Vector3.Transform(Vector3.UnitZ, rotation);
        float halfDepth = face is 1 or 3 ? .025f : face < 4 ? .015f : .035f;
        return new(pose.LocalToWorld(normal * halfDepth), Quaternion.Normalize(pose.Rotation * rotation));
    }

    public static Bitmap MakeFace(int face, bool highlighted = false)
    {
        int width = (int)MathF.Round(FaceWidthMetres(face) * 4800);
        int height = (int)MathF.Round(FaceHeightMetres(face) * 4800);
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(highlighted ? Color.Goldenrod : Color.SlateBlue);
        if (face < 4)
            g.FillRectangle(Brushes.Salmon, 0, 0, width, height * .28f);
        else if (face == 4)
            g.Clear(Color.Salmon);
        using var outline = new Pen(highlighted ? Color.Yellow : Color.WhiteSmoke, highlighted ? 12 : 5);
        g.DrawRectangle(outline, 3, 3, width - 6, height - 6);
        return image;
    }
}
