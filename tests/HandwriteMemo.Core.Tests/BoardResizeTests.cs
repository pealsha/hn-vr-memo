using System.Numerics;
using HandwriteMemo.Core;

[TestClass]
public sealed class BoardResizeTests
{
    private static void Near(float value, float expected, string message) => Check(Math.Abs(value - expected) < 1e-5f, message);
    private static HandInput Hand(Vector3 point, bool grip = false, bool valid = true) =>
        new(new(point, Quaternion.Identity), valid, grip, false, valid);
    private static GrabFrame Hands(HandSide side, HandInput input, HandInput other = default, bool visible = true) =>
        side == HandSide.Left ? new(input, other, visible, false) : new(other, input, visible, false);
    private static GrabScene Scene(BoardPose pose) => new(pose, new(new(10, 10, 10), Quaternion.Identity));
    private static Vector3 TopLeft(GrabFrameResult r) => r.BoardPose.LocalToWorld(new(-r.BoardSize.WidthMetres / 2, r.BoardSize.HeightMetres / 2, 0));

    [TestMethod]
    public void GrabResize()
    {
        var pose = new BoardPose(new(.3f, 1, -.4f), Quaternion.CreateFromYawPitchRoll(.5f, -.4f, .2f));
        foreach (var side in new[] { HandSide.Left, HandSide.Right })
        {
            var scene = Scene(pose);
            var at = scene.ResizeHandlePose.Position;
            var hover = scene.Update(Hands(side, Hand(at)));
            Check((side == HandSide.Left ? hover.LeftCandidate : hover.RightCandidate) == Grabbable.ResizeHandle, "Resize affordance before pressing");
            var start = scene.Update(Hands(side, Hand(at, true)));
            var moved = scene.Update(Hands(side, Hand(at + Vector3.Transform(new Vector3(.16f, -.12f, .06f), pose.Rotation), true)));
            Check(moved.ResizeHolder == side && moved.BoardHolder is null, "Resize is not board transfer");
            Near(moved.BoardSize.WidthMetres, .64f, "Width follows local hand delta");
            Near(moved.BoardSize.HeightMetres, .48f, "Height follows local hand delta");
            Check(Vector3.Distance(TopLeft(start), TopLeft(moved)) < 1e-5, "World top-left must be fixed");
            Check(Math.Abs(Quaternion.Dot(moved.BoardPose.Rotation, pose.Rotation)) > .99999, "Hand rotation does not rotate board");
            Check(Vector3.Distance(moved.Pen.Pose.Position, BoardResize.PenDock(moved.BoardPose, moved.BoardSize).Position) < 1e-5, "Free pen follows dock");
            Check(scene.Update(Hands(side, Hand(at))).ResizeHolder is null, "Release ends resize");
        }
    }

    [TestMethod]
    public void ResizeFollowsMillimetreMotionAfterDeadZone()
    {
        foreach (var handedness in new[] { HandSide.Right, HandSide.Left })
        {
            var scene = Scene(new(Vector3.Zero, Quaternion.Identity));
            scene.Handedness = handedness;
            var at = scene.ResizeHandlePose.Position;
            scene.Update(Hands(handedness, Hand(at)));
            scene.Update(Hands(handedness, Hand(at, true)));
            GrabFrameResult Drag(float distance) => scene.Update(Hands(handedness,
                Hand(at + new Vector3(BoardResize.Direction(handedness) * distance, -distance, 0), true)));
            Check(Drag(.003f).BoardSize == BoardSize.Default, "Movement inside the grip dead zone is ignored");
            foreach (float distance in new[] { .010f, .011f, .012f, .011f })
            {
                var result = Drag(distance);
                Near(result.BoardSize.WidthMetres, BoardSize.Default.WidthMetres + distance, "Width follows millimetre motion");
                Near(result.BoardSize.HeightMetres, BoardSize.Default.HeightMetres + distance, "Height follows millimetre motion");
            }
        }
    }

    [TestMethod]
    public void ResizeApproach()
    {
        foreach (var size in new[] { BoardSize.Default, BoardResize.Maximum })
            foreach (var side in new[] { HandSide.Left, HandSide.Right })
                foreach (var rotation in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(.5f, -.4f, .2f) })
                {
                    var scene = Scene(new(new(.3f, 1, -.4f), rotation));
                    scene.SetPageSize(size, BoardSize.Default);
                    var pose = scene.BoardPose;
                    var handle = BoardResize.HandleLocal(size);
                    GrabFrameResult At(Vector3 local, bool grip = false) => scene.Update(Hands(side, Hand(pose.LocalToWorld(local), grip)));
                    Grabbable? Candidate(GrabFrameResult r) => side == HandSide.Left ? r.LeftCandidate : r.RightCandidate;
                    var corner = new Vector3(size.WidthMetres / 2, -size.HeightMetres / 2, 0);
                    foreach (var offset in new[] { Vector3.Zero, new Vector3(-.03f, 0, .03f), new(0, .03f, -.03f), new(.02f, -.02f, 0) })
                        Check(Candidate(At(corner + offset)) == Grabbable.Board, "Paper corner and its near edge still preview movement");

                    Check(Candidate(At(handle + new Vector3(-.035f, .015f, .12f))) is null, "Distant hand has no target");
                    for (int i = 0; i < 80; i++)
                    {
                        var offset = new Vector3(-.035f + (i % 2 == 0 ? .005f : -.005f), .015f, .025f);
                        Check(Candidate(At(handle + offset)) == Grabbable.ResizeHandle,
                            "Off-centre hand with 1cm oscillation consistently previews resize");
                    }
                    var start = handle + new Vector3(-.035f, .015f, .025f);
                    Check(At(start, true).ResizeHolder == side, "Off-centre fresh grip acquires resize");
                    var delta = size == BoardSize.Default ? new Vector3(.10f, -.08f, 0) : new Vector3(-.10f, .08f, 0);
                    var resized = At(start + delta, true);
                    Near(resized.BoardSize.WidthMetres, size.WidthMetres + delta.X, "Off-centre drag changes width without snapping");
                    Near(resized.BoardSize.HeightMetres, size.HeightMetres - delta.Y, "Off-centre drag changes height without snapping");
                    Check(Vector3.Distance(TopLeft(resized), pose.LocalToWorld(new(-size.WidthMetres / 2, size.HeightMetres / 2, 0))) < 1e-5,
                        "Off-centre resize preserves rotated top-left");
                    At(start + delta);

                    // The closest part of the paper must still acquire the board, even after resizing.
                    pose = scene.BoardPose;
                    corner = new(scene.BoardSize.WidthMetres / 2, -scene.BoardSize.HeightMetres / 2, .02f);
                    Check(Candidate(At(corner)) == Grabbable.Board, "Paper corner previews board after resizing");
                    var before = scene.BoardSize;
                    Check(At(corner, true).BoardHolder == side, "Paper corner acquires board instead of resize");
                    var moved = At(corner + new Vector3(-.03f, .04f, 0), true);
                    Check(moved.BoardSize == before && moved.ResizeHolder is null, "Moving from paper corner never resizes");
                }
    }

    [TestMethod]
    public void ResizeLimits()
    {
        var scene = Scene(new(Vector3.Zero, Quaternion.Identity));
        var at = scene.ResizeHandlePose.Position;
        scene.Update(Hands(HandSide.Left, Hand(at)));
        scene.Update(Hands(HandSide.Left, Hand(at, true)));
        var jitter = scene.Update(Hands(HandSide.Left, Hand(at + new Vector3(.003f, -.003f, .1f), true)));
        Check(jitter.BoardSize == BoardSize.Default, "Jitter and depth must not resize");
        var max = scene.Update(Hands(HandSide.Left, Hand(at + new Vector3(2, -2, 0), true), Hand(Vector3.Zero, true)));
        Check(max.BoardSize == BoardResize.Maximum && max.BoardHolder is null && max.ResizeHolder == HandSide.Left, "Upper bound and no transfer");
        var min = scene.Update(Hands(HandSide.Left, Hand(at + new Vector3(-2, 2, 0), true)));
        Check(min.BoardSize == BoardSize.Default, "Lower bound");
        scene.Update(Hands(HandSide.Left, Hand(at)));
        scene.SetPageSize(new(.7f, .5f), new(.61f, .44f));
        at = scene.ResizeHandlePose.Position;
        scene.Update(Hands(HandSide.Left, Hand(at, true)));
        min = scene.Update(Hands(HandSide.Left, Hand(at + new Vector3(-1, 1, 0), true)));
        Near(min.BoardSize.WidthMetres, .61f, "Ink width floor");
        Near(min.BoardSize.HeightMetres, .44f, "Ink height floor");
    }

    [TestMethod]
    public void InterruptAndMove()
    {
        foreach (var kind in new[] { "tracking", "hide", "reset", "disabled" })
        {
            var scene = Scene(new(Vector3.Zero, Quaternion.Identity));
            var at = scene.ResizeHandlePose.Position;
            scene.Update(Hands(HandSide.Right, Hand(at)));
            scene.Update(Hands(HandSide.Right, Hand(at, true)));
            if (kind == "reset")
                scene.Reset(scene.BoardPose, new(new(10), Quaternion.Identity));
            else if (kind == "disabled")
                scene.ResizeEnabled = false;
            var interrupted = scene.Update(Hands(HandSide.Right, Hand(at, true, kind != "tracking"), visible: kind != "hide"));
            Check(interrupted.ResizeHolder is null, "Interrupt must release resize: " + kind);
            scene.ResizeEnabled = true;
            Check(scene.Update(Hands(HandSide.Right, Hand(at, true))).ResizeHolder is null, "Held grip remains blocked: " + kind);
            scene.Update(Hands(HandSide.Right, Hand(at)));
            Check(scene.Update(Hands(HandSide.Right, Hand(at, true))).ResizeHolder == HandSide.Right, "Fresh grip rearms: " + kind);
        }
        var move = Scene(new(Vector3.Zero, Quaternion.Identity));
        move.SetPageSize(BoardResize.Maximum, BoardSize.Default);
        var initialCentre = move.BoardPose.Position;
        var body = new Vector3(.35f, .1f, 0); // Outside the initial board bounds, inside the resized board.
        Check(move.Update(new(Hand(body), default, true, false)).LeftCandidate == Grabbable.Board, "Whole enlarged rectangle is movable");
        move.Update(new(Hand(body, true), default, true, false));
        var result = move.Update(new(Hand(body + new Vector3(.2f, 0, 0), true), default, true, false));
        Near(result.BoardPose.Position.X, initialCentre.X + .2f, "Normal movement retained");
        Check(result.BoardSize == BoardResize.Maximum && result.ResizeHolder is null, "Moving does not resize");
        move.Update(new(Hand(body + new Vector3(.2f, 0, 0), true), Hand(result.BoardPose.Position), true, false));
        var transfer = move.Update(new(Hand(body + new Vector3(.2f, 0, 0), true), Hand(result.BoardPose.Position, true), true, false));
        Check(transfer.BoardHolder == HandSide.Right && transfer.ResizeHolder is null, "Enlarged board can change hands");
    }

    private static MemoDocument Snapshot(HandwriteEngine engine) => MemoDocument.Capture(Guid.NewGuid(), DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, engine.BoardSize, engine.Strokes);
    [TestMethod]
    public void InkAndUndo()
    {
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        engine.SetPenStyle("#C53939", .003f);
        engine.UpdatePagePoint(new(.8f, .8f), true, 1);
        engine.UpdatePagePoint(new(.9f, .8f), true, 2);
        engine.UpdatePagePoint(null, false, 3);
        var original = Snapshot(engine).Strokes[0];
        engine.ResizeBoard(BoardResize.Maximum);
        var resized = Snapshot(engine).Strokes[0];
        Near(resized.Points[0].XMetres, original.Points[0].XMetres, "Physical X unchanged");
        Near(resized.Points[0].YMetres, original.Points[0].YMetres, "Physical Y unchanged");
        Check(resized.WidthMetres == original.WidthMetres && resized.Color == original.Color && resized.StartedUtc == original.StartedUtc,
            "Style and recorder time unchanged");
        engine.UpdatePagePoint(new(.72f / .8f, .50f / .6f), true, 4);
        engine.UpdatePagePoint(null, false, 5);
        var beforeErase = Snapshot(engine);
        engine.UpdatePagePoint(new(.72f / .8f, .50f / .6f), false, 6, DrawingTool.Eraser);
        engine.UpdatePagePoint(new(.72f / .8f, .50f / .6f), true, 7, DrawingTool.Eraser);
        engine.UpdatePagePoint(null, false, 8, DrawingTool.Eraser);
        Check(engine.Strokes.Count == 1, "Erase new-region dot");
        engine.ResizeBoard(new(.01f, .01f));
        Check(engine.BoardSize.WidthMetres >= .723f, "Undo ink protects shrinking horizontally");
        Near(engine.BoardSize.HeightMetres, .5035f, "Undo ink retains its radius and margin without an obsolete palette gap");
        Check(engine.Undo() && engine.Strokes.Count == 2, "Erase undo survives resizing");
        var restored = Snapshot(engine);
        Near(restored.Strokes[1].Points[0].XMetres, beforeErase.Strokes[1].Points[0].XMetres, "Undone physical X");
        Near(restored.Strokes[1].Points[0].YMetres, beforeErase.Strokes[1].Points[0].YMetres, "Undone physical Y");
        engine.StartNewPage(9);
        Check(engine.BoardSize == BoardSize.Default && engine.Strokes.Count == 0, "New page is standard sized");
    }

    [TestMethod]
    public void PhysicalControls()
    {
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.64f, .44f), BoardResize.Maximum })
        {
            Vector2 Uv(float x, float y) => new(x / size.WidthMetres, y / size.HeightMetres);
            for (int i = 0; i < BoardControls.EditorButtons.Count; i++)
            {
                var b = BoardControls.ButtonBounds(i, false);
                Check(BoardControls.HitTest(Uv((b.Left + b.Width / 2) * .48f, (b.Top + b.Height / 2) * .36f), false, size)
                    == BoardControls.EditorButtons[i], "Toolbar physical targets");
            }
            for (int i = 0; i < 9; i++)
                Check(ToolPalette.HitTest(Uv(ToolPalette.Center(i).X * .48f, ToolPalette.Center(i).Y * .36f), size) == i, "Palette physical targets");
            for (int row = 0; row < 3; row++)
                Check(BoardControls.CardAt(Uv((.175f + row * .325f) * .48f, .5f * .36f), size) == row, "List physical card targets");
            var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity), size);
            engine.UpdatePagePoint(Uv(ToolPalette.Center(8).X * .48f, ToolPalette.Center(8).Y * .36f), true, 0);
            Check(engine.EraserMode == EraserMode.Partial && engine.Strokes.Count == 0, "Palette remains wired to Core tools");
            engine.UpdatePagePoint(null, false, 1);
            engine.UpdatePagePoint(new(.5f, 0), true, 1.1);
            Check(engine.IsWriting && engine.Strokes[^1].Points.Single().Uv.Y == 0, "Paper top edge accepts ink");
            engine.UpdatePagePoint(null, false, 1.2);
            engine.UpdatePagePoint(Uv(size.WidthMetres - .05f, size.HeightMetres - .06f), true, 2);
            Check(engine.IsWriting, "Expanded region accepts ink");
        }
    }

    [TestMethod]
    public void RightAnchoredInk()
    {
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity), new(.72f, .5f));
        engine.UpdatePagePoint(new(.15f, .6f), true, 1);
        engine.UpdatePagePoint(new(.25f, .6f), true, 2);
        engine.UpdatePagePoint(null, false, 3);
        var before = Snapshot(engine).Strokes.Single();
        float oldRightDistance = engine.BoardSize.WidthMetres - before.Points[0].XMetres;
        Check(engine.ResizeBoard(new(.62f, .5f), anchorRight: true), "Right-anchored resize applies");
        var after = Snapshot(engine).Strokes.Single();
        Near(engine.BoardSize.WidthMetres - after.Points[0].XMetres, oldRightDistance, "Right edge distance remains fixed");
        Check(engine.ResizeBoard(new(.01f, .01f), anchorRight: true), "Right-anchored resize clamps");
        float expectedFloor = .72f - before.Points[0].XMetres + before.WidthMetres / 2 + .002f;
        Check(engine.BoardSize.WidthMetres >= expectedFloor - 1e-5f,
            $"History ink at the moving left edge sets the right-anchored width floor (actual {engine.BoardSize.WidthMetres}, expected {expectedFloor})");
        Check(engine.Undo() && engine.Redo(), "Right-anchored resize leaves undo/redo histories usable");
    }

    [TestMethod]
    public void MinimumSizeCacheTracksInputAndHistory()
    {
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity), new(.7f, .5f));
        Check(engine.MinimumBoardSize == BoardSize.Default && engine.MinimumBoardSizeFromRight == BoardSize.Default,
            "Empty page caches the default minimum");
        engine.UpdatePagePoint(new(.99f, .5f), true, 1);
        Check(engine.MinimumBoardSize.WidthMetres > BoardSize.Default.WidthMetres,
            "A live point invalidates the left-anchored minimum cache");
        Check(engine.MinimumBoardSizeFromRight.WidthMetres == BoardSize.Default.WidthMetres,
            "Right-anchored minimum still permits the default for far-right ink");
        engine.UpdatePagePoint(null, false, 2);
        engine.UpdatePagePoint(new(.2f, .6f), true, 3); // records the far-right line in undo history
        var history = engine.CaptureHistory();
        var restored = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity), new(.7f, .5f));
        Check(restored.MinimumBoardSize == BoardSize.Default,
            "New page invalidates the cached current minimum");
        restored.RestoreHistory(history);
        Check(restored.MinimumBoardSize.WidthMetres > BoardSize.Default.WidthMetres,
            "Restored history invalidates and contributes to the minimum");
    }

    [TestMethod]
    public void BottomInk()
    {
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.64f, .44f), BoardResize.Maximum })
            foreach (var mode in new[] { EraserMode.Stroke, EraserMode.Partial })
            {
                var pose = new BoardPose(new(.2f, 1, -.4f), Quaternion.CreateFromYawPitchRoll(.3f, -.2f, .4f));
                var engine = new HandwriteEngine(pose, size);
                double time = 0;
                FrameResult At(float u, float v, bool down = false, DrawingTool tool = DrawingTool.Pen) =>
                    engine.UpdateProjectedTip(new(pose.LocalToWorld(new((u - .5f) * size.WidthMetres,
                        (.5f - v) * size.HeightMetres, .02f)), true), down, .15f, time += .01, tool);
                At(.2f, .4f, true);
                At(.4f, .4f, true);
                At(.4f, .4f);
                var original = Snapshot(engine).Strokes.Single();
                float bottom = 1 - .012f / size.HeightMetres;
                At(.2f, bottom, true);
                At(.8f, bottom, true);
                At(.8f, bottom);
                Check(engine.Strokes.Count == 2, "Old bottom strip accepts projected ink at every size");
                var lower = engine.Strokes[1];
                Near(Snapshot(engine).Strokes[1].Points[0].YMetres, size.HeightMetres - .012f, "Bottom ink retains physical position");
                Check(Snapshot(engine).Strokes[0].Points.SequenceEqual(original.Points), "Existing physical ink never shifts");
                Check(engine.Undo() && engine.Strokes.Count == 1 && engine.Redo() && engine.Strokes[1] == lower,
                    "Bottom drawing supports undo and exact redo");
                engine.SetEraserMode(mode);
                At(.5f, bottom, false, DrawingTool.Eraser);
                At(.5f, bottom, true, DrawingTool.Eraser);
                At(.5f, bottom, false, DrawingTool.Eraser);
                Check(engine.Strokes.Count == (mode == EraserMode.Stroke ? 1 : 3), "Both erasers operate along the bottom of the full-page canvas");
                Check(engine.Undo() && engine.Strokes[1] == lower && engine.Redo(), "Bottom erasure supports undo and redo");
                At(.5f, 1);
                At(.5f, 1, true);
                At(.5f, 1);
                Near(engine.Strokes[^1].Points.Single().Uv.Y, 1, "Paper bottom edge accepts ink");
                int count = engine.Strokes.Count;
                At(.5f, 1.01f, true);
                At(.5f, 1.01f);
                Check(engine.Strokes.Count == count, "Outside the paper still rejects ink");
            }
    }
}
