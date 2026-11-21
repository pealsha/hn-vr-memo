using System.Numerics;
using HandwriteMemo.Core;

[TestClass]
public sealed class PagePoseTests
{
    private static GrabScene Scene() => new(new(new(.7f, 1.2f, -.4f), Quaternion.CreateFromYawPitchRoll(.6f, -.3f, .2f)),
        new(new(2, 1, -.2f), Quaternion.CreateFromYawPitchRoll(.2f, .1f, -.3f)));
    private static Vector3 TopLeft(GrabScene scene) => scene.BoardPose.LocalToWorld(new(-scene.BoardSize.WidthMetres / 2, scene.BoardSize.HeightMetres / 2, 0));
    private static HandInput Hand(BoardPose pose, bool grip = false) => new(pose, true, grip, false, true);
    private static GrabFrame Frame(HandSide side, HandInput hand) => side == HandSide.Left ?
        new(hand, Hand(new(new(10), Quaternion.Identity)), true, false) :
        new(Hand(new(new(10), Quaternion.Identity)), hand, true, false);
    private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < 1e-5f, message);
    private static void Pose(BoardPose a, BoardPose b, string message)
    {
        Near(a.Position, b.Position, message + " position");
        Check(Math.Abs(Quaternion.Dot(a.Rotation, b.Rotation)) > .99999f, message + " rotation");
    }

    [TestMethod]
    public void FreeObjects()
    {
        var scene = Scene();
        var anchor = TopLeft(scene);
        var initial = scene.BoardPose;
        var pen = scene.PenPose;
        var eraser = scene.EraserPose;
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.64f, .44f), BoardResize.Maximum, BoardSize.Default })
        {
            scene.SetPageSize(size, BoardSize.Default);
            Near(TopLeft(scene), anchor, "Page top-left is fixed");
            Pose(scene.PenPose, pen, "Free pen stays put");
            Pose(scene.EraserPose, eraser, "Free eraser stays put");
            Check(scene.BoardPose.Rotation == initial.Rotation, "Page change cannot rotate board");
        }
        Pose(scene.BoardPose, initial, "Returning to original dimensions restores centre");
    }

    [TestMethod]
    public void HeldObjects()
    {
        foreach (var side in new[] { HandSide.Left, HandSide.Right })
            foreach (var item in new[] { Grabbable.Pen, Grabbable.Board, Grabbable.Eraser })
            {
                var scene = Scene();
                var pose = item switch
                {
                    Grabbable.Pen => scene.PenPose,
                    Grabbable.Board => scene.BoardPose,
                    _ => scene.EraserPose
                };
                scene.Update(Frame(side, Hand(pose)));
                var grabbed = scene.Update(Frame(side, Hand(pose, true)));
                Check(grabbed.Events.Any(e => e.Kind == GrabEventKind.Grabbed && e.Object == item), "Acquire " + item);
                var anchor = TopLeft(scene);
                var pen = scene.PenPose;
                var eraser = scene.EraserPose;
                var localAnchor = pose.WorldToLocal(anchor);
                foreach (var size in new[] { new BoardSize(.64f, .44f), BoardResize.Maximum, BoardSize.Default })
                {
                    scene.SetPageSize(size, BoardSize.Default);
                    var result = scene.Update(Frame(side, Hand(pose, true)));
                    Near(TopLeft(scene), anchor, "Holding cannot snap board after page change");
                    Pose(scene.PenPose, pen, "Pen pose during page change");
                    Pose(scene.EraserPose, eraser, "Eraser pose during page change");
                    Check(result.Events.Count == 0 && !scene.NeedsGripRelease(side), "No release or grip reset");
                    Check((item switch
                    {
                        Grabbable.Pen => result.Pen.Holder,
                        Grabbable.Board => result.BoardHolder,
                        _ => result.Eraser.Holder
                    }) == side, "Holder persists");
                }
                // A size change while held must also update the saved hand-relative board pose.
                scene.SetPageSize(BoardResize.Maximum, BoardSize.Default);
                var moved = new BoardPose(pose.Position + new Vector3(.2f, -.1f, .3f), Quaternion.CreateFromYawPitchRoll(-.2f, .5f, .1f));
                scene.Update(Frame(side, Hand(moved, true)));
                if (item == Grabbable.Board)
                    Near(TopLeft(scene), moved.LocalToWorld(localAnchor), "Top-left follows the same hand-relative anchor");
                else
                {
                    Pose(item == Grabbable.Pen ? scene.PenPose : scene.EraserPose, moved, "Held tool still follows hand");
                    Near(TopLeft(scene), anchor, "Tool motion cannot move board");
                }
            }
    }
}
