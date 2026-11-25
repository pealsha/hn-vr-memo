using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class VrStabilityTests
{
    [TestMethod]
    public void Run()
    {
        CheckHover();
        CheckNavigation();
        Console.WriteLine("PASS: hover debounce, unchanged 10cm pickup, page poses and held tools across VR/PC navigation");
    }

    private static void CheckHover()
    {
        foreach (var side in new[] { HandSide.Left, HandSide.Right })
        {
            var scene = new GrabScene(new(new(5), Quaternion.Identity), new(Vector3.Zero, Quaternion.Identity));
            var highlights = new GrabHighlights();
            GrabFrameResult Step(float distance, double time, bool grip = false, bool tracked = true, bool visible = true, bool buttons = true)
            {
                var hand = new HandInput(new(new(distance, 0, 0), Quaternion.Identity), tracked, grip, false, buttons);
                var far = new HandInput(new(new(10), Quaternion.Identity), true, false, false, true);
                var input = side == HandSide.Left ? new GrabFrame(hand, far, visible, false) : new GrabFrame(far, hand, visible, false);
                var result = scene.Update(input);
                highlights.Update(input, result, time);
                return result;
            }
            Step(.10f, 0);
            Check(highlights.Contains(Grabbable.Pen), "Entering range highlights immediately");
            for (int i = 1; i <= 80; i++)
            {
                Step(i % 2 == 0 ? .099f : .101f, i * .01);
                Check(highlights.Contains(Grabbable.Pen), "Boundary jitter cannot blink");
            }
            var miss = Step(.101f, .81, grip: true);
            Check(miss.Pen.Holder is null && highlights.Contains(Grabbable.Pen), "Visual grace does not enlarge pickup distance");
            Step(.101f, .82);
            Step(.101f, .95);
            Check(!highlights.Contains(Grabbable.Pen), "Sustained departure clears feedback");
            Step(.1f, 1);
            Check(Step(.1f, 1.01, grip: true).Pen.Holder == side && !highlights.Contains(Grabbable.Pen), "10cm pickup and immediate held feedback reset");
            Step(.1f, 1.02);
            Step(.099f, 1.03, tracked: false);
            Check(!highlights.Contains(Grabbable.Pen), "Tracking loss clears feedback immediately");
            Step(.099f, 1.04);
            Step(.099f, 1.05);
            Step(.099f, 1.06, buttons: false);
            Check(!highlights.Contains(Grabbable.Pen), "Invalid buttons clear feedback immediately");
            Step(.099f, 1.07);
            Step(.099f, 1.08);
            Step(.099f, 1.09, visible: false);
            Check(!highlights.Contains(Grabbable.Pen), "Hide clears feedback immediately");
            highlights.Reset();
        }
        // If the old object is gone, alternation between two OTHER candidates must not retain it forever.
        var filter = new GrabHighlights();
        var valid = new HandInput(new(Vector3.Zero, Quaternion.Identity), true, false, false, true);
        var inputFrame = new GrabFrame(valid, valid, true, false);
        GrabFrameResult Candidate(Grabbable? candidate) => default(GrabFrameResult) with
        {
            LeftCandidate = candidate
        };
        filter.Update(inputFrame, Candidate(Grabbable.Pen), 0);
        for (int i = 1; i <= 14; i++)
            filter.Update(inputFrame, Candidate(i % 2 == 0 ? Grabbable.Board : null), i * .01);
        Check(!filter.Contains(Grabbable.Pen), "Other candidate jitter cannot retain an old highlight indefinitely");
        filter.Reset();
        filter.Update(inputFrame, Candidate(Grabbable.Board), 1);
        filter.Update(inputFrame, Candidate(Grabbable.ResizeHandle), 1.01);
        Check(filter.Contains(Grabbable.ResizeHandle) && !filter.Contains(Grabbable.Board),
            "Board to resize feedback matches the next grip immediately");
        filter.Update(inputFrame, Candidate(Grabbable.Board), 1.02);
        Check(filter.Contains(Grabbable.Board) && !filter.Contains(Grabbable.ResizeHandle),
            "Returning to paper never previews resize while a grip would move");
    }

    private static void CheckNavigation()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-022", "pages-" + Guid.NewGuid().ToString("N")));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, root, DateTimeOffset.UnixEpoch, _ =>
        {
        });
        using var browser = new VrPageBrowser(page);
        var store = new MemoStore(root);
        for (int i = 0; i < 3; i++)
        {
            var date = DateTimeOffset.UnixEpoch.AddDays(i);
            var document = new MemoDocument(2, Guid.NewGuid(), date, date,
                i == 1 ? BoardResize.Maximum : BoardSize.Default, []);
            store.Save(document, stream => MemoRenderer.WritePng(document, stream));
        }
        var entries = page.Catalog.List();
        double seconds = 0;
        foreach (var held in new[] { Grabbable.Pen, Grabbable.Eraser, Grabbable.Board })
        {
            var scene = new GrabScene(new(new(.7f, 1, -.6f), Quaternion.CreateFromYawPitchRoll(.4f, -.3f, .2f)),
                new(new(2, 1, 0), Quaternion.Identity));
            VrOverlayHost.SyncPageGeometry(scene, engine);
            var handPose = held switch
            {
                Grabbable.Pen => scene.PenPose,
                Grabbable.Eraser => scene.EraserPose,
                _ => scene.BoardPose
            };
            var far = new HandInput(new(new(10), Quaternion.Identity), true, false, false, true);
            GrabFrame Hands(bool grip) => new(new(handPose, true, grip, false, true), far, true, false);
            scene.Update(Hands(false));
            scene.Update(Hands(true));
            var topLeft = Anchor();
            var pen = scene.PenPose;
            var eraser = scene.EraserPose;
            var rotation = scene.BoardPose.Rotation;
            void Verify(string action)
            {
                VrOverlayHost.SyncPageGeometry(scene, engine);
                var result = scene.Update(Hands(true));
                Check(Vector3.Distance(Anchor(), topLeft) < 1e-5f && scene.BoardPose.Rotation == rotation, action + " preserves rotated top-left");
                Check(scene.PenPose == pen && scene.EraserPose == eraser, action + " preserves tool poses");
                Check(result.Events.Count == 0 && !scene.NeedsGripRelease(HandSide.Left), action + " does not release holder");
                Check((held switch
                {
                    Grabbable.Pen => result.Pen.Holder,
                    Grabbable.Board => result.BoardHolder,
                    _ => result.Eraser.Holder
                }) == HandSide.Left, action + " retains grip");
            }
            Vector3 Anchor() => scene.BoardPose.LocalToWorld(new(-scene.BoardSize.WidthMetres / 2, scene.BoardSize.HeightMetres / 2, 0));
            void Command(BoardCommand command, Vector2? uv = null) => browser.Handle(new(ContactState.None, uv, [], [command], false), seconds += 1);
            Check(page.Open(entries[0], seconds += 1), "PC page open");
            Verify("PC open");
            Command(BoardCommand.NextPage);
            Check(page.Id == entries[1].Document.Id, "Next opens different size");
            Verify("Next");
            Command(BoardCommand.PreviousPage);
            Check(page.Id == entries[0].Document.Id, "Previous opens original");
            Verify("Previous");
            Command(BoardCommand.PageList);
            var card = BoardControls.CardBounds(1);
            Command(BoardCommand.OpenPage, new((card.Left + card.Width / 2) * BoardSize.Default.WidthMetres / page.BoardSize.WidthMetres,
                (card.Top + card.Height / 2) * BoardSize.Default.HeightMetres / page.BoardSize.HeightMetres));
            Check(page.Id == entries[1].Document.Id, "List card opens expanded page");
            Verify("List card");
            Command(BoardCommand.NewPage);
            Check(page.BoardSize == BoardSize.Default, "New is standard size");
            Verify("New");
        }
    }
}
