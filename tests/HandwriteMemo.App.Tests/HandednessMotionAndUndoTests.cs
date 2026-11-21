using System.Numerics;
using System.Text.Json;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class HandednessMotionAndUndoTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-023", "automated"));
        Directory.CreateDirectory(root);
        CheckSettings(root);
        CheckAttachment();
        CheckMirroredResize();
        CheckTriggerUndo(root);
        Console.WriteLine("PASS: Task 023 handedness/settings, mirrored resize, raw-device attachment and double-trigger undo gating");
    }

    private static void CheckSettings(string root)
    {
        string config = Path.Combine(root, "settings.json");
        var original = new AppOptions { SaveFolder = root, RightIndexTipOffset = new(.012f, -.07f, -.035f) };
        original.Save(config);
        Check(AppOptions.Load(config).Handedness == HandSide.Right, "right handed by default");
        (original with { Handedness = HandSide.Left }).Save(config);
        var options = AppOptions.Load(config);
        Check(options.Handedness == HandSide.Left, "handedness change persists");
        Check(AppOptions.Load(config).RightIndexTipOffset == original.RightIndexTipOffset, "save retains calibration");
        var origin = new BoardPose(Vector3.Zero, Quaternion.Identity);
        var right = HandLandmarks.FingerTip(origin, original);
        var left = HandLandmarks.FingerTip(origin, options);
        Check(Math.Abs(right.X + left.X) < 1e-6 && right.Y == left.Y && right.Z == left.Z, "finger offset mirrors on local X");
        File.WriteAllText(config, "{\"Handedness\":\"Unknown\"}");
        try
        {
            AppOptions.Load(config);
            throw new InvalidOperationException("Invalid handedness accepted");
        }
        catch (JsonException)
        {
        }
        options.Save(config);
    }

    private static void CheckAttachment()
    {
        var raw = new BoardPose(new(.2f, 1, -.3f), Quaternion.CreateFromYawPitchRoll(.4f, -.3f, .2f));
        var gripOffset = new BoardPose(new(0, .003f, .097f), Quaternion.CreateFromYawPitchRoll(.1f, .05f, 0));
        var grip = OverlayMotion.Compose(raw, gripOffset);
        var held = new BoardPose(new(.03f, .01f, -.02f), Quaternion.CreateFromYawPitchRoll(-.2f, .4f, .3f));
        var tool = OverlayMotion.Compose(grip, held);
        var motion = new OverlayMotion();
        var binding = motion.RelativePose(tool, HandSide.Left, 3, raw, grip);
        // No app updates occur below: SteamVR can evaluate the fixed local pose at every display frame.
        for (int i = 0; i < 120; i++)
        {
            var nextRaw = new BoardPose(raw.Position + new Vector3(i / 120f, .02f, 0), Quaternion.CreateFromYawPitchRoll(i * .01f, -.2f, .4f));
            var expected = OverlayMotion.Compose(OverlayMotion.Compose(nextRaw, gripOffset), held);
            var actual = OverlayMotion.Compose(nextRaw, binding);
            Check(Vector3.Distance(expected.Position, actual.Position) < 1e-5f && Math.Abs(Quaternion.Dot(expected.Rotation, actual.Rotation)) > .99999,
                "held tool follows raw device at 120Hz without reapplying sampled world pose");
        }
        motion.Release();
        var newRaw = raw with
        {
            Position = raw.Position + Vector3.UnitX
        };
        var transferred = motion.RelativePose(tool, HandSide.Right, 4, newRaw, grip);
        Check(Vector3.Distance(OverlayMotion.Compose(newRaw, transferred).Position, tool.Position) < 1e-5, "handover recalibrates without a jump");
        Check(Math.Abs(OverlayMotion.PredictionSeconds(90, .003f, .005f) - (1 / 90f - .003f + .005f)) < 1e-6,
            "display prediction follows Valve's next-vsync plus photons formula");
        File.WriteAllText(Path.Combine("artifacts", "task-023", "motion-estimate.md"), """
            # App-sampled motion delay estimate

            For 1 m/s constant motion and an absolute pose held between app updates:
            - 60 Hz: maximum sample-hold lag 16.67 ms / 16.67 mm; average 8.33 ms / mm.
            - 40 Hz: maximum 25 ms / 25 mm; average 12.5 ms / mm.
            Device-relative overlays remove this app sample-hold component: a 120-step synthetic
            rigid-transform test, with no app pose updates, stays within 0.01 mm of the expected pose.
            This is a geometric estimate, NOT measured tracking, compositor or motion-to-photon latency.
            Actual runtime frame rate and HMD flicker still require the owner's VR check.
            """);
    }

    private static void CheckMirroredResize()
    {
        foreach (var handedness in new[] { HandSide.Right, HandSide.Left })
            foreach (var side in new[] { HandSide.Left, HandSide.Right })
            {
                var pose = new BoardPose(new(.2f, 1, -.4f), Quaternion.CreateFromYawPitchRoll(.4f, -.3f, .2f));
                var scene = new GrabScene(pose, BoardResize.PenDock(pose, BoardSize.Default, handedness)) { Handedness = handedness };
                float direction = BoardResize.Direction(handedness);
                var local = BoardResize.HandleLocal(scene.BoardSize, handedness) + new Vector3(direction * .055f, .02f, .04f);
                var hand = new HandInput(new(pose.LocalToWorld(local), pose.Rotation), true, false, false, true);
                GrabFrame Input(HandInput input) => side == HandSide.Left ? new(input, default, true, false) : new(default, input, true, false);
                var hover = scene.Update(Input(hand));
                Check((side == HandSide.Left ? hover.LeftCandidate : hover.RightCandidate) == Grabbable.ResizeHandle, "rough off-centre approach previews resize");
                var anchor = pose.LocalToWorld(new(-direction * scene.BoardSize.WidthMetres / 2, scene.BoardSize.HeightMetres / 2, 0));
                scene.Update(Input(hand with
                {
                    GripDown = true
                }));
                hand = hand with
                {
                    GripDown = true,
                    Pose = hand.Pose with
                    {
                        Position = pose.LocalToWorld(local + new Vector3(direction * .12f, -.10f, 0))
                    }
                };
                var resized = scene.Update(Input(hand));
                Check(Vector3.Distance(anchor, resized.BoardPose.LocalToWorld(new(-direction * resized.BoardSize.WidthMetres / 2, resized.BoardSize.HeightMetres / 2, 0))) < 1e-5,
                    "opposite top corner remains fixed for either hand/layout");
            }
    }

    private static void CheckTriggerUndo(string root)
    {
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, Path.Combine(root, "pages-" + Guid.NewGuid().ToString("N")), DateTimeOffset.UnixEpoch, _ =>
        {
        });
        var gesture = new DoubleTriggerUndo();
        double seconds = 0;
        void Ink()
        {
            Check(page.BeginDesktop(seconds += .02), "start setup stroke");
            page.DesktopPoint(new(.3f, .5f), true, seconds += .02, DrawingTool.Pen);
            page.DesktopPoint(new(.6f, .6f), true, seconds += .02, DrawingTool.Pen);
            page.DesktopPoint(null, false, seconds += .02, DrawingTool.Pen);
        }
        bool Step(bool down, Vector2? uv = null, bool paused = false, HandSide owner = HandSide.Right, bool tracked = true)
        {
            seconds += .02;
            var tip = uv is { } p ? new Vector3((p.X - .5f) * .48f, (.5f - p.Y) * .36f, .05f) : new Vector3(0, 0, .3f);
            var frame = page.VrPoint(new(tip, tracked), down, .15f, seconds, DrawingTool.Pen);
            var result = default(GrabFrameResult) with
            {
                Pen = new(default, default, owner, down, tracked)
            };
            return VrOverlayHost.UpdateTriggerUndo(gesture, page, result, frame, false, paused, true, seconds);
        }
        void Ready()
        {
            Step(false);
            Step(false);
        }
        bool Double(Vector2? uv = null)
        {
            Step(true, uv);
            Step(false, uv);
            Step(false, uv);
            Step(false, uv);
            return Step(true, uv);
        }
        Ink();
        Ink();
        Ready();
        Step(true);
        Step(false);
        Step(false);
        Step(false);
        Check(engine.Strokes.Count == 2, "one pull does nothing");
        Check(Step(true) && engine.Strokes.Count == 1 && page.TriggerUndoNotice(seconds) is not null, "second outside pull undoes once with feedback");
        Check(!Step(true) && engine.Strokes.Count == 1, "held second pull never repeats");
        Ready();
        Step(true);
        Step(false);
        for (int i = 0; i < 30; i++)
            Step(false);
        Check(!Step(true) && engine.Strokes.Count == 1, "slow pair does nothing");
        Ready();
        var color = ToolPalette.Bounds(1);
        var tile = new Vector2(color.Left + color.Width / 2, color.Top + color.Height / 2);
        Check(!Double(tile) && engine.Strokes.Count == 1 && engine.PenColor == ToolPalette.Colors[1], "palette double pull selects color without undo");
        Ready();
        int before = engine.Strokes.Count;
        Check(!Double(new(.5f, .5f)) && engine.Strokes.Count >= before, "ink double pull writes without undo");
        Ready();
        Step(true);
        Step(false, paused: true);
        Step(false);
        Step(false);
        Check(!Step(true), "dashboard interruption cancels first pull");
        Ready();
        Step(true);
        Step(false, owner: HandSide.Left);
        Step(false, owner: HandSide.Left);
        Step(false, owner: HandSide.Left);
        Check(!Step(true, owner: HandSide.Left), "handover cancels first pull");
        Ready();
        Step(true);
        Step(false, tracked: false);
        Step(false);
        Step(false);
        Check(!Step(true), "tracking interruption cancels first pull");
        Ready();
        Step(true);
        Step(false);
        Ink(); // A whole desktop edit can occur between two VR update loops.
        int afterDesktopEdit = engine.Strokes.Count;
        Step(false);
        Step(false);
        Check(!Step(true) && engine.Strokes.Count == afterDesktopEdit, "PC edit between pulls cancels the pending undo");
    }
}
