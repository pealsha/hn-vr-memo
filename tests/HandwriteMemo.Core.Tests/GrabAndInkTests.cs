using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.Core.Tests;

[TestClass]
public sealed class GrabAndInkTests
{
    private static HandwriteEngine NewEngine(BoardPose? pose = null) => new(pose ?? new BoardPose(Vector3.Zero, Quaternion.Identity));
    private static TipSample Tip(float x, float y, float z, bool tracked = true) => new(new Vector3(x, y, z), tracked);
    private static TipSample ToolbarTip(int index)
    {
        var b = BoardControls.ButtonBounds(index, false);
        var x = (b.Left + b.Width / 2 - .5f) * BoardSize.Default.WidthMetres;
        var y = (.5f - b.Top - b.Height / 2) * BoardSize.Default.HeightMetres;
        return Tip(x, y, .01f);
    }
    private static HandInput Hand(Vector3 position, Quaternion? rotation = null, bool grip = false, bool trigger = false, bool tracked = true, bool buttonsValid = true) =>
        new(new BoardPose(position, rotation ?? Quaternion.Identity), tracked, grip, trigger, buttonsValid);
    private static GrabFrame Frame(HandInput? left = null, HandInput? right = null, bool visible = true, bool recenter = false) => new(left ?? default, right ?? default, visible, recenter);
    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    private static void Nearly(float actual, float expected, string label) => Expect(MathF.Abs(actual - expected) < 0.0001f, $"{label}: expected {expected}, got {actual}");
    private static void SamePose(BoardPose actual, BoardPose expected, string label)
    {
        Nearly(actual.Position.X, expected.Position.X, label + " x");
        Nearly(actual.Position.Y, expected.Position.Y, label + " y");
        Nearly(actual.Position.Z, expected.Position.Z, label + " z");
        Expect(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(actual.Rotation), Quaternion.Normalize(expected.Rotation))) > .9999f, label + " rotation");
    }

    [TestMethod]
    public void RotatedBoardMapsWorldPoint()
    {
        var pose = new BoardPose(new Vector3(2, 1, -3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        var uv = pose.WorldToUv(pose.LocalToWorld(new Vector3(-0.12f, 0.09f, 0.02f)), BoardSize.Default);
        Nearly(uv.X, .25f, "u");
        Nearly(uv.Y, .25f, "v");
    }

    [TestMethod]
    public void ProjectedTipGatesInk()
    {
        var engine = NewEngine();
        var hover = engine.UpdateProjectedTip(Tip(0, 0, .10f), false, .15f, 0);
        Expect(hover.Contact == ContactState.Proximity && hover.CursorUv is { } cursor && cursor == new Vector2(.5f, .5f), "Front-side pen in range should hover at projected UV.");
        var draw = engine.UpdateProjectedTip(Tip(0, 0, .10f), true, .15f, .1);
        Expect(draw.Contact == ContactState.Contact && draw.AddedPoints.Count == 1 && engine.IsWriting, "Fresh trigger press starts projected ink.");
        var far = engine.UpdateProjectedTip(Tip(0, 0, .16f), true, .15f, .2);
        Expect(far.StrokeEnded && far.Contact == ContactState.None && !engine.IsWriting, "Leaving front drawing distance ends ink.");
        var behind = engine.UpdateProjectedTip(Tip(0, 0, -.01f), false, .15f, .3);
        Expect(behind.CursorUv is null && behind.Contact == ContactState.None, "Projection must reject the board back side.");
    }

    [TestMethod]
    public void ProjectedBreaksAndRearmsStroke()
    {
        var engine = NewEngine();
        engine.UpdateProjectedTip(Tip(0, 0, .01f), true, .15f, 0);
        var lost = engine.UpdateProjectedTip(Tip(0, 0, .01f, tracked: false), true, .15f, .1);
        Expect(lost.StrokeEnded && !engine.IsWriting, "Tracking loss must end ink.");
        engine.UpdateProjectedTip(Tip(0, 0, .01f), true, .15f, .2);
        Expect(!engine.IsWriting, "Restoring tracking while trigger remains down must not resume ink.");
        engine.UpdateProjectedTip(Tip(0, 0, .01f), false, .15f, .3);
        var restarted = engine.UpdateProjectedTip(Tip(0, 0, .01f), true, .15f, .4);
        Expect(restarted.AddedPoints.Count == 1 && engine.IsWriting, "Release followed by a fresh press must rearm ink.");
    }

    [TestMethod]
    public void ToolbarCommandsRespectHostOwnership()
    {
        var engine = NewEngine();
        engine.UpdateProjectedTip(Tip(0, 0, .01f), true, .15f, 0);
        engine.UpdateProjectedTip(Tip(0, 0, .01f), false, .15f, .1);
        var undo = engine.UpdateProjectedTip(ToolbarTip(0), true, .15f, .2);
        Expect(undo.Commands.SequenceEqual([BoardCommand.Undo]) && engine.Strokes.Count == 0, "Undo should update this in-memory page.");
        engine.UpdateProjectedTip(ToolbarTip(0), false, .15f, .3);
        engine.UpdateProjectedTip(Tip(.02f, 0, .01f), true, .15f, .4);
        engine.UpdateProjectedTip(Tip(.02f, 0, .01f), false, .15f, .5);
        var newPage = engine.UpdateProjectedTip(ToolbarTip(6), true, .15f, .6);
        Expect(newPage.Commands.SequenceEqual([BoardCommand.NewPage]) && engine.Strokes.Count == 1, "NewPage must request host persistence without clearing.");
        var heldNewPage = engine.UpdateProjectedTip(ToolbarTip(4), true, .15f, .65);
        Expect(heldNewPage.Commands.Count == 0 && engine.Strokes.Count == 1, "Holding or sliding a toolbar press must not repeat its command.");
        engine.StartNewPage(.7);
        Expect(engine.Strokes.Count == 0, "Host calls StartNewPage only after saving the old page.");
        engine.UpdateProjectedTip(ToolbarTip(4), false, .15f, .8);
        engine.UpdateProjectedTip(Tip(.02f, 0, .01f), true, .15f, .9);
        engine.UpdateProjectedTip(Tip(.02f, 0, .01f), false, .15f, 1.0);
        var close = engine.UpdateProjectedTip(ToolbarTip(7), true, .15f, 1.1);
        Expect(close.Commands.SequenceEqual([BoardCommand.Close]) && engine.Strokes.Count == 1, "Close must only request host save and hide.");
    }

    [TestMethod]
    public void ToolbarIgnoresInkDrag()
    {
        var engine = NewEngine();
        engine.UpdateProjectedTip(Tip(0, 0, .01f), true, .15f, 0);
        var ontoToolbar = engine.UpdateProjectedTip(ToolbarTip(0), true, .15f, .1);
        Expect(ontoToolbar.Commands.Count == 0 && ontoToolbar.StrokeEnded, "Dragging ink onto a toolbar button must only end the stroke.");
    }

    [TestMethod]
    public void PenWinsGrabPriority()
    {
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), new BoardPose(Vector3.Zero, Quaternion.Identity));
        var result = scene.Update(Frame(Hand(Vector3.Zero, grip: true)));
        Expect(result.Pen.Holder == HandSide.Left && result.BoardHolder is null, "Pen should win when a grip can reach both objects.");
    }

    [TestMethod]
    public void PenPreservesGrabRelativePose()
    {
        var pen = new BoardPose(new Vector3(.02f, .01f, .03f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f));
        var scene = new GrabScene(new BoardPose(new Vector3(2, 0, 0), Quaternion.Identity), pen);
        var grabHand = Hand(new Vector3(0, -.04f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, .2f), grip: true);
        var grabbed = scene.Update(Frame(grabHand));
        SamePose(grabbed.Pen.Pose, pen, "grab must not snap pen");
        var movedPose = new BoardPose(new Vector3(.4f, .5f, -.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f));
        var relative = new BoardPose(grabHand.Pose.WorldToLocal(pen.Position), Quaternion.Normalize(Quaternion.Inverse(Quaternion.Normalize(grabHand.Pose.Rotation)) * pen.Rotation));
        var moved = scene.Update(Frame(new HandInput(movedPose, true, true, false, true)));
        var expected = new BoardPose(movedPose.LocalToWorld(relative.Position), Quaternion.Normalize(movedPose.Rotation * relative.Rotation));
        SamePose(moved.Pen.Pose, expected, "pen follows preserved relation");
    }

    [TestMethod]
    public void BoardPreservesGrabRelativePose()
    {
        var board = new BoardPose(new Vector3(0, 0, .03f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .2f));
        var scene = new GrabScene(board, new BoardPose(new Vector3(5, 0, 0), Quaternion.Identity));
        var hand = Hand(new Vector3(0, -.04f, 0), Quaternion.Identity, grip: true);
        scene.Update(Frame(hand));
        var movedPose = new BoardPose(new Vector3(.3f, .2f, -.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .5f));
        var result = scene.Update(Frame(new HandInput(movedPose, true, true, false, true)));
        var relative = new BoardPose(hand.Pose.WorldToLocal(board.Position), board.Rotation);
        var expected = new BoardPose(movedPose.LocalToWorld(relative.Position), Quaternion.Normalize(movedPose.Rotation * relative.Rotation));
        SamePose(result.BoardPose, expected, "board follows preserved relation");
    }

    [TestMethod]
    public void ReleaseAndRecenterAreSafe()
    {
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), new BoardPose(Vector3.Zero, Quaternion.Identity));
        scene.Update(Frame(Hand(Vector3.Zero, grip: true)));
        var released = scene.Update(Frame(Hand(Vector3.Zero, grip: false)));
        Expect(released.Pen.Holder is null && released.Events.Any(e => e.Kind == GrabEventKind.Released), "Grip release drops pen.");
        var reset = scene.Update(Frame(Hand(Vector3.Zero, grip: true), recenter: true));
        Expect(reset.Pen.Holder is null, "Recenter must block a grip already held through the reset.");
        scene.Update(Frame(Hand(Vector3.Zero, grip: false)));
        var regrabbed = scene.Update(Frame(Hand(Vector3.Zero, grip: true)));
        Expect(regrabbed.Pen.Holder == HandSide.Left, "A valid grip release re-enables pickup.");
    }

    [TestMethod]
    public void GrabDistanceIsRotationAndTranslationInvariant()
    {
        var rotation = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(.8f, -.5f, .3f));
        var translation = new Vector3(4, -2, .7f);
        var pen = new BoardPose(translation, rotation);
        var radius = .10f;
        var scene = new GrabScene(new BoardPose(new Vector3(20, 0, 0), Quaternion.Identity), pen, new GrabSettings(radius));
        var mid = pen.LocalToWorld((GrabScene.PenTipLocal + GrabScene.PenTailLocal) / 2);
        var normal = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rotation));
        var inside = scene.Update(Frame(Hand(mid + normal * radius, grip: true)));
        Expect(inside.Pen.Holder == HandSide.Left, "A rotated pen must accept a point exactly on its radius.");

        var board = new BoardPose(translation, rotation);
        var boardAxisPoints = new[]
        {
            new Vector3(BoardSize.Default.WidthMetres / 2 + radius, 0, 0),
            new Vector3(0, BoardSize.Default.HeightMetres / 2 + radius, 0),
            new Vector3(0, 0, radius),
        };
        foreach (var localPoint in boardAxisPoints)
        {
            var boardScene = new GrabScene(board, new BoardPose(new Vector3(20, 0, 0), Quaternion.Identity), new GrabSettings(radius));
            var boardInside = boardScene.Update(Frame(Hand(board.LocalToWorld(localPoint), grip: true)));
            Expect(boardInside.BoardHolder == HandSide.Left, "Board distance must use each rotated local axis.");
        }

        var outsideScene = new GrabScene(new BoardPose(new Vector3(20, 0, 0), Quaternion.Identity), pen, new GrabSettings(radius));
        var outside = outsideScene.Update(Frame(Hand(mid + normal * (radius + .001f), grip: true)));
        Expect(outside.Pen.Holder is null, "A point beyond the radius must not grab after rotation and translation.");
    }

    [TestMethod]
    public void TransferPreservesPoseAndBlocksOldGrip()
    {
        var pen = new BoardPose(new Vector3(.02f, .01f, .03f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .4f));
        var scene = new GrabScene(new BoardPose(new Vector3(3, 0, 0), Quaternion.Identity), pen);
        var oldPose = new BoardPose(pen.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitX, .2f));
        var initial = scene.Update(Frame(new HandInput(oldPose, true, true, false, true), Hand(pen.Position)));
        Expect(initial.Pen.Holder == HandSide.Left, "Setup must grab the pen with the old hand.");
        var beforeTransfer = scene.PenPose;
        var newPose = new BoardPose(beforeTransfer.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f));
        var transfer = scene.Update(Frame(new HandInput(oldPose, true, true, false, true), new HandInput(newPose, true, true, false, true)));
        SamePose(transfer.Pen.Pose, beforeTransfer, "transfer must not snap world pose");
        Expect(transfer.Pen.Holder == HandSide.Right, "Fresh opposite grip should receive the pen.");
        Expect(transfer.Events.SequenceEqual([
            new GrabEvent(GrabEventKind.Released, HandSide.Left, Grabbable.Pen),
            new GrabEvent(GrabEventKind.Grabbed, HandSide.Right, Grabbable.Pen)]), "Transfer events must release old owner before grabbing new owner.");

        var held = scene.Update(Frame(new HandInput(oldPose, true, true, false, true), new HandInput(newPose, true, true, false, true)));
        Expect(held.Pen.Holder == HandSide.Right && held.Events.Count == 0, "Old grip held through transfer must not steal the pen back.");
    }

    [TestMethod]
    public void RawOriginOffsetMakesPickupOrientationDependent()
    {
        const float Radius = .08f;
        var rawControllerOrigin = new Vector3(0, 0, -.097f); // Measured grip-minus-raw offset is about +9.7 cm along this local axis.
        var dock = new BoardPose(Vector3.Zero, Quaternion.Identity);
        var boardFarAway = new BoardPose(new Vector3(5, 0, 0), Quaternion.Identity);

        var upright = new GrabScene(boardFarAway, dock, new GrabSettings(Radius));
        Expect(upright.Update(Frame(Hand(rawControllerOrigin, grip: true))).Pen.Holder is null,
            "With a raw-device origin, an upright pen is 9.7 cm away and misses an 8 cm pickup radius.");

        var rotatedDock = new BoardPose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        var rotated = new GrabScene(boardFarAway, rotatedDock, new GrabSettings(Radius));
        Expect(rotated.Update(Frame(Hand(rawControllerOrigin, grip: true))).Pen.Holder == HandSide.Left,
            "Rotating the same pen puts its segment near the raw origin, reproducing the orientation-dependent apparent success.");

        var palm = Vector3.Zero;
        var uprightPalm = new GrabScene(boardFarAway, dock, new GrabSettings(Radius));
        var rotatedPalm = new GrabScene(boardFarAway, rotatedDock, new GrabSettings(Radius));
        Expect(uprightPalm.Update(Frame(Hand(palm, grip: true))).Pen.Holder == HandSide.Left &&
            rotatedPalm.Update(Frame(Hand(palm, grip: true))).Pen.Holder == HandSide.Left,
            "Using the actual palm/grip point accepts both orientations at the same 8 cm pickup radius.");
    }

    [TestMethod]
    public void SimultaneousFreshGripsAreDeterministic()
    {
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), new BoardPose(Vector3.Zero, Quaternion.Identity));
        var result = scene.Update(Frame(Hand(Vector3.Zero, grip: true), Hand(Vector3.Zero, grip: true)));
        Expect(result.Pen.Holder == HandSide.Left && result.BoardHolder == HandSide.Right, "Left is processed first; right takes the remaining board instead of transferring the newly grabbed pen.");
        Expect(result.Events.Count == 2 && result.Events.All(e => e.Kind == GrabEventKind.Grabbed), "Simultaneous grips must produce one grab per hand without a handover.");
    }

    [TestMethod]
    public void BoardTransferPreservesWorldPose()
    {
        var board = new BoardPose(new Vector3(.2f, -.1f, .3f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .35f));
        var scene = new GrabScene(board, new BoardPose(new Vector3(5, 0, 0), Quaternion.Identity));
        var left = new HandInput(new BoardPose(board.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitX, .2f)), true, true, false, true);
        scene.Update(Frame(left, Hand(board.Position)));
        var beforeTransfer = scene.BoardPose;
        var right = new HandInput(new BoardPose(beforeTransfer.Position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f)), true, true, false, true);
        var transfer = scene.Update(Frame(left, right));
        Expect(transfer.BoardHolder == HandSide.Right, "Fresh opposite grip should receive the board.");
        SamePose(transfer.BoardPose, beforeTransfer, "board transfer must not snap world pose");
        Expect(transfer.Events.SequenceEqual([
            new GrabEvent(GrabEventKind.Released, HandSide.Left, Grabbable.Board),
            new GrabEvent(GrabEventKind.Grabbed, HandSide.Right, Grabbable.Board)]), "Board transfer events must be ordered.");
    }

    [TestMethod]
    public void GrabCandidatesMatchSelection()
    {
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), new BoardPose(Vector3.Zero, Quaternion.Identity));
        var hover = scene.Update(Frame(Hand(Vector3.Zero), Hand(Vector3.Zero)));
        Expect(hover.LeftCandidate == Grabbable.Pen && hover.RightCandidate == Grabbable.Pen, "Both free valid hands should highlight the pen when pen and board overlap.");
        var held = scene.Update(Frame(Hand(Vector3.Zero, grip: true), Hand(Vector3.Zero)));
        Expect(held.LeftCandidate is null && held.RightCandidate == Grabbable.Pen, "A holding hand has no candidate; the free opposite hand still prioritizes its held pen for transfer.");
        var invalid = scene.Update(Frame(Hand(Vector3.Zero, tracked: false), Hand(Vector3.Zero, buttonsValid: false)));
        Expect(invalid.LeftCandidate is null && invalid.RightCandidate is null, "Invalid hands must not be highlighted.");
        var invisible = scene.Update(Frame(visible: false));
        Expect(invisible.LeftCandidate is null && invisible.RightCandidate is null, "Hidden scenes must not expose candidates.");
    }

    [TestMethod]
    public void PenTransferInterruptsInk()
    {
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), new BoardPose(Vector3.Zero, Quaternion.Identity));
        var engine = NewEngine();
        var left = Hand(Vector3.Zero, grip: true, trigger: true);
        var first = scene.Update(Frame(left, Hand(Vector3.Zero)));
        engine.UpdateProjectedTip(new TipSample(first.Pen.Tip, first.Pen.InputAvailable), first.Pen.TriggerDown, .15f, 0);
        Expect(engine.IsWriting, "Setup must have an active stroke.");

        var transfer = scene.Update(Frame(left, Hand(first.Pen.Pose.Position, grip: true, trigger: true)));
        if (transfer.Events.Any(e => e.Object == Grabbable.Pen))
            engine.RequireTriggerRelease();
        var interrupted = engine.UpdateProjectedTip(new TipSample(transfer.Pen.Tip, transfer.Pen.InputAvailable), transfer.Pen.TriggerDown, .15f, .1);
        Expect(interrupted.StrokeEnded && !engine.IsWriting, "Pen ownership events must interrupt an active stroke.");
    }
}
