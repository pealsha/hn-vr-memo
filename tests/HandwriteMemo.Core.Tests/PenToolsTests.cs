using System.Numerics;
using HandwriteMemo.Core;

[TestClass]
public sealed class PenToolsTests
{
    private sealed class Page
    {
        public HandwriteEngine Engine { get; } = new(new BoardPose(Vector3.Zero, Quaternion.Identity));
        public double Time;
        public FrameResult At(float x, float y, bool down = false, DrawingTool tool = DrawingTool.Pen) =>
            Engine.UpdatePagePoint(new Vector2(x, y), down, Time += .01, tool);
        public void Draw(params Vector2[] points)
        {
            At(points[0].X, points[0].Y);
            foreach (var p in points)
                At(p.X, p.Y, true);
            At(points[^1].X, points[^1].Y);
        }
        public void Erase(float x, float y)
        {
            At(x, y, false, DrawingTool.Eraser);
            At(x, y, true, DrawingTool.Eraser);
            At(x, y, false, DrawingTool.Eraser);
        }
    }
    private static void Ok(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    private static void Near(double a, double b) => Ok(Math.Abs(a - b) < 1e-5, $"Expected {b}, got {a}");
    [TestMethod]
    public void StyleIsCaptured()
    {
        var p = new Page();
        p.At(.2f, .3f, true);
        p.Engine.SetPenStyle(ToolPalette.Colors[1], ToolPalette.Widths[2]);
        p.At(.7f, .3f, true);
        p.At(.7f, .3f);
        p.Draw(new(.2f, .6f), new(.7f, .6f));
        var before = p.Engine.Strokes;
        Ok(before[0].Color == MemoStroke.DefaultColor && before[0].WidthMetres == .0015f, "default or active stroke changed");
        Ok(before[1].Color == ToolPalette.Colors[1] && before[1].WidthMetres == .003f, "next stroke missing selection");
        var doc = MemoDocument.Capture(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, BoardSize.Default, before);
        Ok(doc.Strokes[1].Color == before[1].Color && doc.Strokes[1].WidthMetres == before[1].WidthMetres, "capture lost style");
    }
    [TestMethod]
    public void PaletteDoesNotDraw()
    {
        foreach (var tool in new[] { DrawingTool.Pen, DrawingTool.Eraser })
        {
            var p = new Page();
            for (int i = 0; i < ToolPalette.ButtonCount; i++)
            {
                var center = ToolPalette.Center(i);
                float x = center.X;
                p.At(x, center.Y, false, tool);
                var selected = p.At(x, center.Y, true, tool);
                Ok(p.Engine.IsPaletteSelected(i) && !selected.DocumentChanged && selected.PaletteSelection == i, "palette selection missing or changed page");
                p.At(.4f, .5f, true, tool);
                var held = p.At(ToolPalette.Center((i + 1) % ToolPalette.ButtonCount).X, center.Y, true, tool);
                Ok(held.PaletteSelection is null, "held press must not report a new palette choice");
                Ok(p.Engine.IsPaletteSelected(i) && p.Engine.Strokes.Count == 0, "held palette press drew or changed selection");
            }
            p.At(.2f, .4f, false, tool);
            p.At(.2f, .4f, true, tool);
            var dragged = p.At(ToolPalette.Center(5).X, ToolPalette.Center(5).Y, true, tool);
            Ok(dragged.StrokeEnded && p.Engine.PenWidthMetres == .003f && dragged.PaletteSelection is null, "ink/erase drag selected palette");
        }
    }
    [TestMethod]
    public void WholeStrokes()
    {
        var p = new Page();
        p.Draw(new(.1f, .4f), new(.9f, .4f));
        p.Draw(new(.1f, .7f), new(.9f, .7f));
        p.Erase(.5f, .4f);
        Ok(p.Engine.Strokes.Count == 1 && p.Engine.Strokes[0].Points[0].Uv.Y == .7f, "sparse middle was not hit or other stroke changed");
        p.Engine.SetPenStyle(ToolPalette.Colors[3], .01f);
        p.Draw(new Vector2(.3f, .3f));
        p.Erase(.333f, .3f); // 15.84mm from a 10mm-wide dot: within 12mm + half width.
        Ok(p.Engine.Strokes.Count == 1, "dot footprint ignored stroke width");
    }
    [TestMethod]
    public void PartialSplits()
    {
        var p = new Page();
        p.Engine.SetPenStyle(ToolPalette.Colors[2], .003f);
        p.Draw(new(.1f, .5f), new(.9f, .5f));
        var original = p.Engine.Strokes.Single();
        p.Engine.SetEraserMode(EraserMode.Partial);
        p.Erase(.5f, .5f);
        Ok(p.Engine.Strokes.Count == 2, "middle should split into two strokes");
        double reachUv = (.012 + .003 / 2) / .48;
        var left = p.Engine.Strokes[0];
        var right = p.Engine.Strokes[1];
        Near(left.Points[^1].Uv.X, .5 - reachUv);
        Near(right.Points[0].Uv.X, .5 + reachUv);
        Near(left.Points[^1].Seconds, original.Points[0].Seconds + (original.Points[1].Seconds - original.Points[0].Seconds) * ((.5 - reachUv - .1) / .8));
        Ok(p.Engine.Strokes.All(s => s.Color == original.Color && s.WidthMetres == original.WidthMetres), "split restyled fragments");
        Ok(p.Engine.Undo() && p.Engine.Strokes.Single().Points.SequenceEqual(original.Points), "undo did not restore exact original");
    }
    [TestMethod]
    public void PartialEndsAndDots()
    {
        foreach (float end in new[] { .2f, .8f })
        {
            var p = new Page();
            p.Engine.SetEraserMode(EraserMode.Partial);
            p.Draw(new(.2f, .5f), new(.8f, .5f));
            p.Erase(end, .5f);
            Ok(p.Engine.Strokes.Count == 1 && p.Engine.Strokes[0].Points.Count == 2, "end trim left spurious fragments");
            var points = p.Engine.Strokes[0].Points;
            Ok(end == .2f ? points[0].Uv.X > .2f && points[^1].Uv.X == .8f : points[0].Uv.X == .2f && points[^1].Uv.X < .8f, "wrong end trimmed");
        }
        var dot = new Page();
        dot.Engine.SetEraserMode(EraserMode.Partial);
        dot.Draw(new Vector2(.5f, .5f));
        dot.Erase(.5f, .5f);
        Ok(dot.Engine.Strokes.Count == 0 && dot.Engine.Undo() && dot.Engine.Strokes.Count == 1, "partial dot erase/undo failed");
    }
    [TestMethod]
    public void CrossingsAndRepeats()
    {
        var p = new Page();
        p.Engine.SetEraserMode(EraserMode.Partial);
        p.Draw(new(.1f, .5f), new(.9f, .5f));
        p.Engine.SetPenStyle(ToolPalette.Colors[1], .003f);
        p.Draw(new(.5f, .2f), new(.5f, .8f));
        p.Erase(.5f, .5f);
        Ok(p.Engine.Strokes.Count == 4, "crossing should leave four arms");
        var first = p.Engine.Strokes.SelectMany(s => s.Points).ToArray();
        p.Erase(.5f, .5f);
        Ok(p.Engine.Strokes.Count == 4, "repeat erase fragmented unchanged arms");
        var second = p.Engine.Strokes.SelectMany(s => s.Points).ToArray();
        Ok(first.Length == second.Length, "repeat erase changed point count");
        for (int i = 0; i < first.Length; i++)
            Near(Vector2.Distance(first[i].Uv, second[i].Uv), 0);
    }
    [TestMethod]
    public void PartialVerticesAndEdges()
    {
        var p = new Page();
        p.Engine.SetEraserMode(EraserMode.Partial);
        p.Draw(new(0, .4f), new(.5f, .4f), new(1, .4f));
        var original = p.Engine.Strokes.Single();
        p.Erase(.5f, .4f);
        p.Erase(0, .4f);
        p.Erase(1, .4f);
        Ok(p.Engine.Strokes.Count == 2, "vertex or edge erasure left extra fragments");
        var left = p.Engine.Strokes[0].Points;
        var right = p.Engine.Strokes[1].Points;
        Ok(left[0].Uv.X > 0 && left[^1].Uv.X < .5f && right[0].Uv.X > .5f && right[^1].Uv.X < 1, "wrong boundary survived");
        Ok(p.Engine.Undo() && p.Engine.Undo() && p.Engine.Undo() && p.Engine.Strokes.Single() == original,
            "separate passes did not each undo once");
    }
    [TestMethod]
    public void SweepAndUndo()
    {
        foreach (var mode in new[] { EraserMode.Partial, EraserMode.Stroke })
        {
            var p = new Page();
            p.Engine.SetEraserMode(mode);
            p.Draw(new(.1f, .5f), new(.9f, .5f));
            p.Draw(new(.1f, .65f), new(.9f, .65f));
            var original = p.Engine.Strokes.ToArray();
            p.At(.3f, .2f, false, DrawingTool.Eraser);
            p.At(.3f, .2f, true, DrawingTool.Eraser); // initially no ink
            p.At(.7f, .8f, true, DrawingTool.Eraser); // crosses both lines between samples
            p.At(.8f, .8f, true, DrawingTool.Eraser);
            p.At(.8f, .8f, false, DrawingTool.Eraser);
            Ok(p.Engine.Strokes.Count == (mode == EraserMode.Partial ? 4 : 0), "sweep missed lines between samples");
            Ok(p.Engine.Undo() && p.Engine.Strokes.SequenceEqual(original), "one undo must restore entire sweep");
            Ok(p.Engine.Undo() && p.Engine.Strokes.Count == 1, "erase created more than one undo entry");
        }
    }
    [TestMethod]
    public void UndoOrder()
    {
        var p = new Page();
        p.Draw(new(.2f, .5f), new(.8f, .5f));
        var original = p.Engine.Strokes.Single();
        p.Erase(.5f, .5f);
        p.Draw(new Vector2(.5f, .7f));
        Ok(p.Engine.Undo() && p.Engine.Strokes.Count == 0, "first undo should remove newest drawing");
        Ok(p.Engine.Undo() && p.Engine.Strokes.Single() == original, "second undo should restore erased ink");
        p.Erase(.1f, .2f);
        Ok(p.Engine.Undo() && p.Engine.Strokes.Count == 0, "empty erase must not create undo history");
        p.Engine.StartNewPage(p.Time += .1);
        Ok(!p.Engine.Undo(), "new page must clear history");
    }
    [TestMethod]
    public void RedoHistory()
    {
        foreach (var mode in new[] { EraserMode.Stroke, EraserMode.Partial })
        {
            var p = new Page();
            Ok(!p.Engine.CanUndo && !p.Engine.CanRedo && !p.Engine.Redo(), "empty history must be unavailable");
            p.Engine.SetPenStyle(ToolPalette.Colors[2], ToolPalette.Widths[2]);
            p.Draw(new(.2f, .5f), new(.8f, .5f));
            var original = p.Engine.Strokes.Single();
            p.Engine.SetEraserMode(mode);
            p.Erase(.5f, .5f);
            var erased = p.Engine.Strokes.ToArray();
            Ok(p.Engine.Undo() && p.Engine.Undo() && !p.Engine.CanUndo && p.Engine.CanRedo, "undo both gestures");
            Ok(!p.Engine.Undo() && p.Engine.Redo() && p.Engine.Strokes.Single() == original, "excess undo retains redo");
            Ok(p.Engine.Redo() && p.Engine.Strokes.SequenceEqual(erased) && !p.Engine.CanRedo, "redo exact erasure");
            Ok(p.Engine.Undo(), "undo erasure again");
            p.Engine.SetPenStyle(ToolPalette.Colors[1], ToolPalette.Widths[0]);
            p.Erase(.1f, .8f);
            Ok(p.Engine.CanRedo, "tool changes and empty erasure retain redo");
            p.Draw(new(.2f, .7f), new(.7f, .7f));
            Ok(!p.Engine.CanRedo && !p.Engine.Redo(), "new ink discards redo branch");
            p.Engine.Undo();
            p.Erase(.5f, .5f);
            Ok(!p.Engine.CanRedo, "effective erasure discards redo branch");
            p.Engine.Undo();
            p.Engine.StartNewPage(p.Time += .1);
            Ok(!p.Engine.CanUndo && !p.Engine.CanRedo, "new page clears both histories");
        }
        var resized = new Page();
        resized.Engine.ResizeBoard(new(.8f, .6f));
        resized.Draw(new(.8f, .7f), new(.9f, .8f));
        resized.Engine.Undo();
        resized.Engine.ResizeBoard(BoardSize.Default);
        Ok(resized.Engine.BoardSize.WidthMetres > .72f && resized.Engine.BoardSize.HeightMetres > .48f, "redo ink protects shrink boundary");
        Ok(resized.Engine.Redo(), "redo survives resize");
        Near(resized.Engine.Strokes.Single().Points[^1].Uv.X * resized.Engine.BoardSize.WidthMetres, .72);
        Near(resized.Engine.Strokes.Single().Points[^1].Uv.Y * resized.Engine.BoardSize.HeightMetres, .48);
        var document = MemoDocument.Capture(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            resized.Engine.BoardSize, resized.Engine.Strokes);
        resized.Engine.Undo();
        resized.Engine.LoadPage(document, resized.Time += .1);
        Ok(!resized.Engine.CanUndo && !resized.Engine.CanRedo, "load clears both histories");
        var bounded = new Page();
        for (int i = 0; i <= HandwriteEngine.HistoryLimit; i++)
            bounded.Draw(new(.2f, .4f), new(.3f, .4f));
        int count = 0;
        while (bounded.Engine.Undo())
            count++;
        Ok(count == HandwriteEngine.HistoryLimit && bounded.Engine.Strokes.Count == 1, "history limit drops oldest operations without dropping current ink");
        while (bounded.Engine.Redo())
            count--;
        Ok(count == 0 && bounded.Engine.Strokes.Count == HandwriteEngine.HistoryLimit + 1, "bounded history retains redo order");
    }

    [TestMethod]
    public void Interruptions()
    {
        var p = new Page();
        p.Draw(new(.1f, .5f), new(.9f, .5f));
        var pose = new BoardPose(new Vector3(1, 2, 3), Quaternion.CreateFromYawPitchRoll(.4f, -.3f, .2f));
        p.Engine.SetBoardPose(pose);
        TipSample Tip(float x, float z, bool tracked = true) => new(pose.LocalToWorld(new Vector3((x - .5f) * .48f, 0, z)), tracked);
        p.Engine.UpdateProjectedTip(Tip(.2f, .05f), false, .15f, p.Time += .1, DrawingTool.Eraser);
        var erased = p.Engine.UpdateProjectedTip(Tip(.2f, .05f), true, .15f, p.Time += .1, DrawingTool.Eraser);
        Ok(erased.DocumentChanged && p.Engine.IsErasing, "rotated eraser projection failed");
        var lost = p.Engine.UpdateProjectedTip(Tip(.3f, .05f, false), true, .15f, p.Time += .1, DrawingTool.Eraser);
        Ok(lost.StrokeEnded && !p.Engine.IsErasing, "lost tracking must end erase");
        p.Engine.UpdateProjectedTip(Tip(.3f, .05f), true, .15f, p.Time += .1, DrawingTool.Eraser);
        Ok(!p.Engine.IsErasing, "restored held trigger resumed erase");
        p.Engine.UpdateProjectedTip(Tip(.3f, -.01f), false, .15f, p.Time += .1, DrawingTool.Eraser);
        p.Engine.UpdateProjectedTip(Tip(.3f, .05f), true, .15f, p.Time += .1, DrawingTool.Eraser);
        Ok(p.Engine.IsErasing, "release off-board must rearm erase");
        var far = p.Engine.UpdateProjectedTip(Tip(.3f, .16f), true, .15f, p.Time += .1, DrawingTool.Eraser);
        Ok(far.StrokeEnded && far.CursorUv is null, "eraser ignored distance limit");
    }
    private static HandInput Hand(BoardPose pose, bool grip = false, bool tracked = true, bool valid = true) => new(pose, tracked, grip, true, valid);
    private static GrabFrame Frame(HandInput left, HandInput right = default, bool visible = true) => new(left, right, visible, false);
    [TestMethod]
    public void EraserGrabAndTransfer()
    {
        var pen = new BoardPose(new Vector3(2, 3, 4), Quaternion.CreateFromYawPitchRoll(.7f, -.4f, .3f));
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), pen);
        var dock = scene.EraserPose;
        var hand = new BoardPose(dock.LocalToWorld(new Vector3(.02f, -.01f, .01f)), Quaternion.Identity);
        var grab = scene.Update(Frame(Hand(hand, true), Hand(dock)));
        Ok(grab.Eraser.Holder == HandSide.Left && grab.Eraser.InputAvailable && grab.Eraser.TriggerDown, "eraser did not grab");
        SamePose(scene.EraserPose, dock);
        var moved = hand with
        {
            Position = hand.Position + new Vector3(.4f, .3f, .2f)
        };
        scene.Update(Frame(Hand(moved, true), Hand(dock)));
        var expected = dock with
        {
            Position = dock.Position + new Vector3(.4f, .3f, .2f)
        };
        SamePose(scene.EraserPose, expected);
        var transfer = scene.Update(Frame(Hand(moved, true), Hand(expected, true)));
        Ok(transfer.Eraser.Holder == HandSide.Right && transfer.Events.SequenceEqual(new[]
        { new GrabEvent(GrabEventKind.Released, HandSide.Left, Grabbable.Eraser), new GrabEvent(GrabEventKind.Grabbed, HandSide.Right, Grabbable.Eraser) }), "transfer event order wrong");
        SamePose(scene.EraserPose, expected);
        scene.Update(Frame(Hand(moved, true), Hand(expected)));
        Ok(scene.EraserHolder is null, "old held grip stole transferred eraser back");
    }
    [TestMethod]
    public void EraserResetAndInterrupt()
    {
        foreach (string interruption in new[] { "release", "tracking", "buttons", "hide", "reset" })
        {
            var pen = new BoardPose(new Vector3(2, 0, 0), Quaternion.Identity);
            var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), pen);
            var dock = scene.EraserPose;
            var candidate = scene.Update(Frame(Hand(dock)));
            Ok(candidate.LeftCandidate == Grabbable.Eraser, "eraser not highlighted");
            scene.Update(Frame(Hand(dock, true)));
            if (interruption == "reset")
                scene.Reset(new BoardPose(new Vector3(0, 1, 0), Quaternion.Identity), pen);
            else
                scene.Update(Frame(Hand(dock, interruption != "release", interruption != "tracking", interruption != "buttons"), visible: interruption != "hide"));
            Ok(scene.EraserHolder is null, interruption + " retained eraser");
            if (interruption != "release")
            {
                scene.Update(Frame(Hand(dock, true)));
                Ok(scene.EraserHolder is null, interruption + " accepted held grip");
            }
            scene.Update(Frame(Hand(dock)));
            Ok(scene.Update(Frame(Hand(dock, true))).Eraser.Holder == HandSide.Left, "released grip did not rearm");
            scene.Reset(new BoardPose(Vector3.Zero, Quaternion.Identity), pen);
            SamePose(scene.EraserPose, GrabScene.EraserDock(pen));
        }
    }
    [TestMethod]
    public void SimultaneousEraserGrab()
    {
        var scene = new GrabScene(new BoardPose(Vector3.Zero, Quaternion.Identity), new BoardPose(new Vector3(3, 0, 0), Quaternion.Identity));
        var hand = Hand(scene.EraserPose, true);
        var result = scene.Update(Frame(hand, hand));
        Ok(result.Eraser.Holder == HandSide.Left && result.Events.Count == 1, "simultaneous eraser grab transferred twice");
    }
    private static void SamePose(BoardPose a, BoardPose b)
    {
        Near(Vector3.Distance(a.Position, b.Position), 0);
        Near(Math.Abs(Quaternion.Dot(a.Rotation, b.Rotation)), 1);
    }
}
