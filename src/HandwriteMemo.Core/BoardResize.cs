using System.Numerics;

namespace HandwriteMemo.Core;

/// <summary>Page area changes about its top-left corner; no ink or UI scaling.</summary>
public static class BoardResize
{
    public static BoardSize Maximum => new(.80f, .60f);
    public const float HandleWidthMetres = .080f;
    public const float HandleRadiusMetres = .085f;
    public const float DeadZoneMetres = .008f;

    // The forgiving target stays entirely outside the paper. A hand over even the
    // bottom-right paper corner still moves the board, rather than resizing it.
    public const float HandleOffsetMetres = .085f;
    public static float Direction(HandSide handedness) => handedness == HandSide.Left ? -1 : 1;
    public static Vector3 HandleLocal(BoardSize size, HandSide handedness = HandSide.Right) => new(Direction(handedness) * (size.WidthMetres / 2 + HandleOffsetMetres),
        -size.HeightMetres / 2 - HandleOffsetMetres, .003f);
    public static BoardPose PenDock(BoardPose board, BoardSize size, HandSide handedness = HandSide.Right) => new(
        board.LocalToWorld(new(Direction(handedness) * (size.WidthMetres / 2 + .06f), Math.Max(.14f, size.HeightMetres / 2 - .04f), .03f)), board.Rotation);

    public static BoardSize Clamp(BoardSize requested, BoardSize minimum, BoardSize current) => new(
        Math.Clamp(requested.WidthMetres, minimum.WidthMetres, Math.Max(minimum.WidthMetres, Math.Max(Maximum.WidthMetres, current.WidthMetres))),
        Math.Clamp(requested.HeightMetres, minimum.HeightMetres, Math.Max(minimum.HeightMetres, Math.Max(Maximum.HeightMetres, current.HeightMetres))));
}
