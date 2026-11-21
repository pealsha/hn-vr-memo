using System.Numerics;
using HandwriteMemo.Core;

[TestClass]
public sealed class ToolResizeTests
{
    [TestMethod]
    public void PickupAndHandoff()
    {
        var board = new BoardPose(Vector3.Zero, Quaternion.Identity);
        var handle = BoardResize.HandleLocal(BoardSize.Default);
        var scene = new GrabScene(board, new(handle - GrabScene.PenTipLocal, Quaternion.Identity));
        var penHand = Hand(scene.PenPose.Position, grip: false);
        scene.Update(new(penHand, default, true, false));
        penHand = penHand with { GripDown = true, TriggerDown = true };
        var pickup = scene.Update(new(penHand, default, true, false));
        Check(pickup.Pen.Holder == HandSide.Left && pickup.ResizeHolder is null, "Pickup with trigger down cannot immediately resize");
        Check(scene.Update(new(penHand, default, true, false)).ResizeHolder is null, "Pickup trigger remains blocked while held");
        penHand = penHand with { TriggerDown = false };
        scene.Update(new(penHand, default, true, false));
        var eraserHand = Hand(scene.EraserPose.Position, grip: false);
        scene.Update(new(penHand, eraserHand, true, false));
        eraserHand = eraserHand with { GripDown = true };
        scene.Update(new(penHand, eraserHand, true, false));
        eraserHand = Hand(handle - GrabScene.EraserTipLocal);
        scene.Update(new(penHand, eraserHand, true, false));
        penHand = penHand with { TriggerDown = true };
        Check(scene.Update(new(penHand, eraserHand, true, false)).ResizeHolder == HandSide.Left, "Fresh press after pickup release begins resize");
        penHand = penHand with { TriggerDown = false };
        eraserHand = eraserHand with { TriggerDown = true };
        var handoff = scene.Update(new(penHand, eraserHand, true, false));
        Check(handoff.ResizeHolder == HandSide.Right && !handoff.Pen.InputAvailable && !handoff.Eraser.InputAvailable,
            "Simultaneous release and new capture suppress both tools");
    }
    private static HandInput Hand(Vector3 p, bool grip = true, bool trigger = false, bool tracked = true, bool buttons = true) =>
        new(new(p, Quaternion.Identity), tracked, grip, trigger, buttons);
    private static GrabFrame Frame(HandSide side, HandInput hand, HandInput other = default, bool visible = true, bool recenter = false) =>
        side == HandSide.Left ? new(hand, other, visible, recenter) : new(other, hand, visible, recenter);
    private static (GrabScene scene, Vector3 at) Setup(HandSide side, bool eraser = false)
    {
        var scene = new GrabScene(new(Vector3.Zero, Quaternion.Identity), new(new(2, 2, 2), Quaternion.Identity));
        var dock = eraser ? scene.EraserPose.Position : scene.PenPose.Position;
        scene.Update(Frame(side, Hand(dock, grip: false)));
        scene.Update(Frame(side, Hand(dock)));
        Check((eraser ? scene.EraserHolder : scene.PenHolder) == side, "Tool acquired");
        var tip = eraser ? scene.EraserTip : scene.PenTip;
        var at = dock + scene.ResizeHandlePose.Position - tip;
        return (scene, at);
    }
    [TestMethod]
    public void ToolResize()
    {
        foreach (var side in new[] { HandSide.Left, HandSide.Right })
            foreach (var eraser in new[] { false, true })
                foreach (var handedness in new[] { HandSide.Left, HandSide.Right })
                {
                    var (scene, _) = Setup(side, eraser);
                    scene.Handedness = handedness;
                    var dock = eraser ? scene.EraserPose.Position : scene.PenPose.Position;
                    var tip = eraser ? scene.EraserTip : scene.PenTip;
                    var at = dock + scene.ResizeHandlePose.Position - tip;
                    var hover = scene.Update(Frame(side, Hand(at)));
                    Check((side == HandSide.Left ? hover.LeftCandidate : hover.RightCandidate) == Grabbable.ResizeHandle, "Held tip highlights handle");
                    var start = scene.Update(Frame(side, Hand(at, trigger: true)));
                    Check(start.ResizeHolder == side && start.ToolResizeSuppressedHand == side &&
                        !(eraser ? start.Eraser.InputAvailable : start.Pen.InputAvailable), "Fresh trigger captures and suppresses tool input");
                    var sign = handedness == HandSide.Right ? 1 : -1;
                    var moved = scene.Update(Frame(side, Hand(at + new Vector3(.101f * sign, -.081f, 0), trigger: true)));
                    Check(Math.Abs(moved.BoardSize.WidthMetres - .58f) < .0001f && Math.Abs(moved.BoardSize.HeightMetres - .44f) < .0001f, "Quantized tool resize");
                    var released = scene.Update(Frame(side, Hand(at, trigger: false)));
                    Check(released.ResizeHolder is null && released.ToolResizeSuppressedHand == side &&
                        !(eraser ? released.Eraser.InputAvailable : released.Pen.InputAvailable), "Release frame remains suppressed");
                    Check((eraser ? released.Eraser.Holder : released.Pen.Holder) == side, "Tool remains held");
                    var rearmed = scene.Update(Frame(side, Hand(at)));
                    Check(rearmed.ToolResizeSuppressedHand is null && (eraser ? rearmed.Eraser.InputAvailable : rearmed.Pen.InputAvailable), "Tool input returns after release frame");
                }
    }
    [TestMethod]
    public void DrawingDoesNotSwitch()
    {
        var random = new Random(39037);
        foreach (var side in new[] { HandSide.Left, HandSide.Right })
            foreach (var eraser in new[] { false, true })
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    var (scene, at) = Setup(side, eraser);
                    var away = at + new Vector3(.3f, .2f, 0);
                    scene.Update(Frame(side, Hand(away)));
                    var first = scene.Update(Frame(side, Hand(away, trigger: true)));
                    Check(first.ResizeHolder is null && (eraser ? first.Eraser.TriggerDown : first.Pen.TriggerDown), "Paper or elsewhere starts normal tool input");
                    for (int i = 0; i < 20; i++)
                    {
                        var position = i % 2 == 0 ? at : at + new Vector3((float)random.NextDouble() * .5f, (float)random.NextDouble() * .5f, 0);
                        var r = scene.Update(Frame(side, Hand(position, trigger: true)));
                        Check(r.ResizeHolder is null && (eraser ? r.Eraser.TriggerDown : r.Pen.TriggerDown), "Held input never changes mode on entering handle");
                    }
                }
    }
    [TestMethod]
    public void Interruptions()
    {
        foreach (var kind in new[] { "tracking", "buttons", "hidden", "recenter", "reset", "disabled", "transfer" })
        {
            var (scene, at) = Setup(HandSide.Left);
            scene.Update(Frame(HandSide.Left, Hand(at), Hand(new Vector3(10), grip: false)));
            scene.Update(Frame(HandSide.Left, Hand(at, trigger: true), Hand(new Vector3(10), grip: false)));
            if (kind == "disabled") scene.ResizeEnabled = false;
            if (kind == "reset") scene.Reset(scene.BoardPose, scene.PenPose);
            var other = kind == "transfer" ? Hand(scene.PenPose.Position, trigger: true) : default;
            var interruptedHand = Hand(at, trigger: true, tracked: kind != "tracking", buttons: kind != "buttons");
            var r = scene.Update(Frame(HandSide.Left, interruptedHand, other,
                visible: kind != "hidden", recenter: kind == "recenter"));
            Check(r.ResizeHolder is null && r.ToolResizeSuppressedHand == HandSide.Left, "Interrupt suppresses release frame: " + kind);
            scene.ResizeEnabled = true;
            if (kind == "disabled")
            {
                Check(scene.Update(Frame(HandSide.Left, Hand(at, trigger: true))).ResizeHolder is null, "Disabled held trigger cannot restart");
                scene.Update(Frame(HandSide.Left, Hand(at)));
                Check(scene.Update(Frame(HandSide.Left, Hand(at, trigger: true))).ResizeHolder == HandSide.Left, "Release rearms after disabled");
            }
            if (kind == "transfer")
            {
                Check(r.Pen.Holder == HandSide.Right, "Tool transfers to other hand");
                Check(scene.Update(Frame(HandSide.Right, Hand(scene.PenPose.Position, trigger: true))).ResizeHolder is null, "Receiving held trigger cannot start resize");
            }
            if (kind is "tracking" or "buttons" or "hidden" or "recenter" or "reset")
            {
                scene.Update(Frame(HandSide.Left, Hand(at, grip: false, trigger: true)));
                var blocked = scene.Update(Frame(HandSide.Left, Hand(at, trigger: true)));
                Check(blocked.ResizeHolder is null, "Held trigger blocked after interruption: " + kind);
            }
        }
        var (disabledScene, handle) = Setup(HandSide.Left);
        disabledScene.ResizeEnabled = false;
        disabledScene.Update(Frame(HandSide.Left, Hand(handle, trigger: true)));
        disabledScene.ResizeEnabled = true;
        Check(disabledScene.Update(Frame(HandSide.Left, Hand(handle, trigger: true))).ResizeHolder is null, "Trigger started while disabled stays blocked");
    }
    [TestMethod]
    public void BothHands()
    {
        var (scene, at) = Setup(HandSide.Left);
        var eraserDock = scene.EraserPose.Position;
        scene.Update(Frame(HandSide.Left, Hand(at), Hand(eraserDock, grip: false)));
        scene.Update(Frame(HandSide.Left, Hand(at), Hand(eraserDock)));
        Check(scene.EraserHolder == HandSide.Right, "Other hand acquires eraser");
        var rightAt = eraserDock + scene.ResizeHandlePose.Position - scene.EraserTip;
        scene.Update(Frame(HandSide.Left, Hand(at), Hand(rightAt)));
        var result = scene.Update(Frame(HandSide.Left, Hand(at, trigger: true), Hand(rightAt, trigger: true)));
        Check(result.ResizeHolder == HandSide.Left && result.Events.Count(e => e.Object == Grabbable.ResizeHandle && e.Kind == GrabEventKind.Grabbed) == 1,
            "Only one hand captures the handle in a frame");
    }
}
