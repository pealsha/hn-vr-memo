using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BoardToolbarInputTests
{
    [TestMethod]
    public void Controls()
    {
        foreach (var size in new[] { BoardSize.Default, new BoardSize(.64f, .44f), BoardResize.Maximum })
            foreach (var tool in Enum.GetValues<DrawingTool>())
            {
                var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity), size);
                double time = 0;
                FrameResult At(Vector2 uv, bool down, bool list = false) => engine.UpdatePagePoint(uv, down, time += .01, tool, list);
                foreach (bool list in new[] { false, true })
                {
                    var buttons = list ? BoardControls.ListButtons : BoardControls.EditorButtons;
                    for (int i = 0; i < buttons.Count; i++)
                    {
                        var r = BoardControls.ButtonBounds(i, list);
                        var uv = new Vector2((r.Left + r.Width / 2) * .48f / size.WidthMetres, (r.Top + r.Height / 2) * .36f / size.HeightMetres);
                        Check(uv.Y < 0, "Every toolbar button must be outside paper");
                        At(uv, false, list);
                        var pressed = At(uv, true, list);
                        Check(pressed.Commands.SequenceEqual([buttons[i]]) && pressed.AddedPoints.Count == 0, "Moved button emits original command");
                        Check(At(uv, true, list).Commands.Count == 0, "Held button does not repeat");
                        At(uv, false, list);
                    }
                }
                if (tool == DrawingTool.Pen)
                {
                    foreach (var uv in new[] { new Vector2(.03f, 0), new(.5f, .01f), new(.95f, .1f) })
                    {
                        At(uv, false);
                        var ink = At(uv, true);
                        Check(ink.AddedPoints.Count == 1 && ink.Commands.Count == 0, "Old toolbar/status area now writes at unchanged page coordinates");
                        At(uv, false);
                    }
                    var gap = new Vector2(.5f * .48f / size.WidthMetres, -4 / MemoRenderer.PixelsPerMetre / size.HeightMetres);
                    At(gap, false);
                    var ignored = At(gap, true);
                    Check(ignored.Commands.Count == 0 && ignored.AddedPoints.Count == 0, "Gap between dock and paper is inert");
                }
                var board = new BoardPose(new(.4f, 1.2f, -.5f), Quaternion.CreateFromYawPitchRoll(.4f, -.2f, .3f));
                var pose = BoardPainter.ToolsPose(board, size);
                var corner = pose.LocalToWorld(new(-.24f, -ToolPalette.DockHeightPixels / 2f / MemoRenderer.PixelsPerMetre, 0));
                var expected = board.LocalToWorld(new(-size.WidthMetres / 2, size.HeightMetres / 2 + ToolPalette.DockGapPixels / MemoRenderer.PixelsPerMetre, 0));
                Check(Vector3.Distance(corner, expected) < .000001f, "Dock follows resized rotated paper");
            }
        Console.WriteLine("PASS: external toolbar hit regions in editor/list with either tool, top-edge ink, inert gaps and rotated dock attachment.");
    }

    [TestMethod]
    public void ResizeIntegration()
    {
        foreach (var handedness in Enum.GetValues<HandSide>())
            foreach (bool eraser in new[] { false, true })
            {
                using var rig = new Rig(handedness, eraser);
                var at = rig.Scene.ResizeHandlePose.Position;
                rig.Step(at, false);
                Check(rig.Highlights.Contains(Grabbable.ResizeHandle), "Held tool previews yellow handle");
                rig.Step(at, true);
                Check(rig.Page.IsResizing && rig.Last.ResizeHolder == handedness, "Trigger starts shared-session resize");
                int count = rig.Engine.Strokes.Count;
                rig.Step(at + new Vector3(BoardResize.Direction(handedness) * .12f, -.08f, 0), true);
                Check(Math.Abs(rig.Engine.BoardSize.WidthMetres - .60f) < .0001f && Math.Abs(rig.Engine.BoardSize.HeightMetres - .44f) < .0001f,
                    "Shared page and scene grow together");
                rig.Step(rig.Scene.BoardPose.Position + Vector3.UnitZ * .03f, false);
                Check(!rig.Page.IsResizing && rig.Engine.Strokes.Count == count && !rig.Engine.IsWriting && !rig.Engine.IsErasing,
                    "Release over paper finalizes without phantom ink or erasure");
                rig.Step(rig.Scene.BoardPose.Position + Vector3.UnitZ * .03f, false);
                rig.Step(rig.Scene.BoardPose.Position + Vector3.UnitZ * .03f, true);
                Check(eraser ? rig.Engine.IsErasing : rig.Engine.IsWriting, "Fresh paper trigger writes or erases after resizing");
                var before = rig.Engine.BoardSize;
                rig.Step(rig.Scene.ResizeHandlePose.Position, true);
                Check(!rig.Page.IsResizing && rig.Engine.BoardSize == before, "Drawing into handle never changes to resizing");
                rig.Step(rig.Scene.ResizeHandlePose.Position, true);
                Check(!rig.Page.IsResizing, "Held trigger stays blocked after the stroke leaves paper");
                rig.Step(rig.Scene.ResizeHandlePose.Position, false);
            }
        foreach (int seed in new[] { 39, 39037, 39103 })
        {
            var random = new Random(seed);
            using var rig = new Rig(HandSide.Right, false);
            for (int gesture = 0; gesture < 30; gesture++)
            {
                var paper = rig.Scene.BoardPose.Position + new Vector3(0, 0, .03f);
                rig.Step(paper, false);
                rig.Step(paper, true);
                Check(rig.Engine.IsWriting, "Random sequence starts on paper");
                var size = rig.Engine.BoardSize;
                for (int i = 0; i < 20; i++)
                {
                    var tip = i % 3 == 0 ? rig.Scene.ResizeHandlePose.Position : paper + new Vector3((float)random.NextDouble() * .6f - .3f,
                        (float)random.NextDouble() * .6f - .3f, 0);
                    rig.Step(tip, true);
                    Check(!rig.Page.IsResizing && rig.Engine.BoardSize == size, "Random held drawing sequence cannot enter resize");
                }
                rig.Step(paper, false);
            }
        }
        Console.WriteLine("PASS: real page/scene resize integration, both tools/hands, yellow hover, trigger release, no phantom ink; random 3 seeds x 30 x 20 held inputs.");
    }

    private sealed class Rig : IDisposable
    {
        internal HandwriteEngine Engine { get; } = new(new(Vector3.Zero, Quaternion.Identity));
        public void Dispose() => _browser.Dispose();
        private readonly VrPageBrowser _browser;
        internal MemoSession Page { get; }
        internal GrabScene Scene { get; }
        internal GrabHighlights Highlights { get; } = new();
        internal GrabFrameResult Last { get; private set; }
        private readonly HandSide _side;
        private readonly bool _eraser;
        private double _time;

        internal Rig(HandSide side, bool eraser)
        {
            _side = side;
            _eraser = eraser;
            Page = new(Engine, Path.GetFullPath("artifacts/task-039/input-" + Guid.NewGuid().ToString("N")), DateTimeOffset.UnixEpoch, _ => { });
            _browser = new VrPageBrowser(Page);
            Scene = new(new(Vector3.Zero, Quaternion.Identity), new(new(2), Quaternion.Identity)) { Handedness = side };
            var pose = eraser ? Scene.EraserPose : Scene.PenPose;
            var hand = new HandInput(pose, true, false, false, true);
            Scene.Update(Hands(hand));
            Scene.Update(Hands(hand with { GripDown = true }));
        }

        private GrabFrame Hands(HandInput hand)
        {
            var far = new HandInput(new(new(10), Quaternion.Identity), true, false, false, true);
            return _side == HandSide.Left ? new(hand, far, true, false) : new(far, hand, true, false);
        }

        internal void Step(Vector3 tip, bool down)
        {
            _time += .02;
            var localTip = _eraser ? GrabScene.EraserTipLocal : GrabScene.PenTipLocal;
            var input = Hands(new(new(tip - localTip, Quaternion.Identity), true, true, down, true));
            Last = VrOverlayHost.UpdatePageGrab(Scene, Engine, Page, input, _time, _side, _browser.IsOpen);
            Highlights.Update(input, Last, _time);
            Engine.SetBoardPose(Last.BoardPose);
            _browser.RefreshIfNeeded();
            var frame = Page.VrPoint(new(_eraser ? Last.Eraser.Tip : Last.Pen.Tip, _eraser ? Last.Eraser.InputAvailable : Last.Pen.InputAvailable),
                _eraser ? Last.Eraser.TriggerDown : Last.Pen.TriggerDown, .15f, _time, _eraser ? DrawingTool.Eraser : DrawingTool.Pen, _browser.IsOpen);
            _browser.Handle(frame, _time);
        }
    }
}
