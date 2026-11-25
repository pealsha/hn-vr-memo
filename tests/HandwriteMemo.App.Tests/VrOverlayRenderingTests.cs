using System.Numerics;
using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class VrOverlayRenderingTests
{
    [TestMethod]
    public void IdleFramesKeepMovingWithoutRedrawingOrUploading()
    {
        using var rig = new RenderRig();
        rig.View.Show();
        rig.ExpectUpload("first visible frame");

        int before = rig.PoseUpdates;
        for (int i = 0; i < 10; i++)
        {
            var pose = new BoardPose(new Vector3(i * .01f, 0, 0), Quaternion.Identity);
            rig.Scene.Reset(pose, BoardResize.PenDock(pose, rig.Engine.BoardSize, HandSide.Right));
            rig.ExpectIdle();
        }
        Assert.AreEqual(20, rig.PoseUpdates - before, "The board and dock transforms still update every frame.");

        rig.View.Hide();
        rig.View.Show();
        rig.ExpectUpload("reopening the same page");
        rig.ExpectIdle();
    }

    [TestMethod]
    public void ContentChangesUploadOnceThenSettle()
    {
        using var rig = new RenderRig();
        rig.ExpectUpload("initial frame");
        rig.Frame = rig.Frame with { CursorUv = new(.5f, .5f), Contact = ContactState.Proximity };
        rig.ExpectUpload("cursor appears");
        rig.Frame = rig.Frame with { CursorUv = new(.6f, .5f) };
        rig.ExpectUpload("cursor moves");
        rig.Frame = rig.Frame with { Contact = ContactState.Contact };
        rig.ExpectUpload("contact changes");
        rig.Frame = rig.Frame with { Tool = DrawingTool.Eraser };
        rig.ExpectUpload("tool changes");
        rig.Engine.SetPenStyle(ToolPalette.Colors[1], rig.Engine.PenWidthMetres);
        rig.ExpectUpload("color changes");
        rig.Engine.SetPenStyle(rig.Engine.PenColor, ToolPalette.Widths[2]);
        rig.ExpectUpload("width changes");
        rig.Engine.SetEraserMode(EraserMode.Partial);
        rig.ExpectUpload("eraser mode changes");

        rig.Engine.UpdateProjectedTip(new(new(.01f, .01f, .01f), true), true, .15f, 1);
        rig.ExpectUpload("stroke starts with a fixed rendered cursor");
        rig.Engine.UpdateProjectedTip(new(new(.02f, .02f, .01f), true), true, .15f, 2);
        rig.ExpectUpload("live ink is appended");
        rig.Engine.UpdateProjectedTip(new(new(.02f, .02f, .01f), true), false, .15f, 3);
        rig.ExpectUpload("stroke ends");

        rig.Page.Undo(4);
        rig.ExpectUpload("undo and redo change");
        Assert.IsTrue(rig.Page.SetVrResizing(true, 4.5));
        Assert.IsTrue(rig.Page.Resize(new(.56f, .42f)));
        Assert.IsTrue(rig.Page.SetVrResizing(false, 5.5));
        rig.ExpectUpload("board is resized");
        Assert.IsTrue(rig.Page.NewPage(6));
        rig.ExpectUpload("page changes");
        Assert.IsTrue(rig.Page.SetPinned(true, 7));
        rig.ExpectUpload("pin changes");

        rig.Pages.Handle(rig.Frame with { Commands = [BoardCommand.PageList] }, 8);
        Assert.IsTrue(rig.Pages.IsOpen);
        rig.ExpectUpload("page list opens");
        Assert.IsTrue(rig.Pages.Refresh());
        rig.ExpectUpload("catalog revision changes");
        rig.Pages.ReturnToPage();
        rig.ExpectUpload("page list closes");
    }

    [TestMethod]
    [DataRow(HandSide.Right, false)]
    [DataRow(HandSide.Left, false)]
    [DataRow(HandSide.Left, true)]
    public void DragKeepsOneFixedCanvasAndSavesTheFinalSize(HandSide handedness, bool oversized)
    {
        var initialSize = oversized ? new BoardSize(.88f, .64f) : BoardSize.Default;
        var canvasSize = oversized ? initialSize : BoardResize.Maximum;
        using var rig = new RenderRig(handedness, initialSize);
        var pose = new BoardPose(new(.3f, 1, -.4f), Quaternion.CreateFromYawPitchRoll(.5f, -.4f, .2f));
        rig.Scene.SetPageSize(initialSize, BoardSize.Default);
        rig.Scene.Reset(pose, BoardResize.PenDock(pose, initialSize, handedness));
        rig.Page.Observe(rig.Engine.UpdatePagePoint(new(.5f, .5f), true, 0), 0);
        rig.Page.Observe(rig.Engine.UpdatePagePoint(null, false, .1), .1);
        long savedBefore = rig.Page.SavedRevision;

        var grip = rig.Scene.ResizeHandlePose.Position;
        rig.RenderGrip(grip, down: false, seconds: 1);
        rig.RenderGrip(grip, down: true, seconds: 2);
        Assert.IsTrue(rig.Page.IsResizing);
        CollectionAssert.AreEqual(oversized ? Array.Empty<float>() : new[] { canvasSize.WidthMetres }, rig.BoardWidths);
        Assert.AreEqual(oversized ? 0 : 1, rig.BoardTextureClears);
        Assert.AreEqual(MemoRenderer.PixelSize(canvasSize), rig.BoardTextureSize);
        var canvasPose = rig.BoardPose;
        float direction = BoardResize.Direction(handedness);
        var anchor = pose.LocalToWorld(new(-direction * initialSize.WidthMetres / 2, initialSize.HeightMetres / 2, 0));
        Near(anchor, canvasPose.LocalToWorld(new(-direction * canvasSize.WidthMetres / 2, canvasSize.HeightMetres / 2, 0)));

        Vector2[] drags = oversized
            ? [new(-.11f, -.05f), new(-.111f, -.051f), new(-.112f, -.052f), new(-.023f, -.005f)]
            : [new(.023f, .016f), new(.024f, .017f), new(.025f, .018f), new(.026f, .019f)];
        for (int i = 0; i < drags.Length; i++)
        {
            Vector3 moved = grip + Vector3.Transform(new Vector3(direction * drags[i].X, -drags[i].Y, 0), pose.Rotation);
            rig.RenderGrip(moved, down: true, seconds: 3 + i * 2);
            Assert.AreEqual(0, rig.BoardTextureClears);
            Assert.AreEqual(0, rig.BoardWidths.Count);
            Assert.AreEqual(MemoRenderer.PixelSize(canvasSize), rig.BoardTextureSize);
            Assert.AreEqual(MemoRenderer.PixelSize(initialSize), rig.NormalImageSize);
            Assert.AreEqual(1, rig.BoardSubmissions);
            Assert.AreEqual(0, rig.DockSubmissions);
            Near(canvasPose.Position, rig.BoardPose.Position);
            Assert.IsTrue(MathF.Abs(Quaternion.Dot(canvasPose.Rotation, rig.BoardPose.Rotation)) > .99999f);
            Assert.AreEqual(0, rig.RelativeHandlePoses);
            Near(VrOverlaySprites.ResizeVisualPose(rig.Scene.ResizeHandlePose).Position, rig.HandlePose.Position);
            Assert.AreEqual(savedBefore, rig.Page.SavedRevision, "Drag frames do not save, even seconds apart.");
        }

        Assert.IsFalse(rig.Page.BeginDesktop(10)); // Changes only the dock guidance while the grip stays held.
        rig.RenderGrip(rig.GripPosition, down: true, seconds: 10);
        Assert.AreEqual(1, rig.DockSubmissions);
        Assert.AreEqual(0, rig.BoardSubmissions);
        rig.RenderGrip(rig.GripPosition, down: true, seconds: 11);
        Assert.AreEqual(0, rig.DockSubmissions);
        Assert.AreEqual(savedBefore, rig.Page.SavedRevision);
        Assert.AreEqual(initialSize, rig.Page.Catalog.List().Single().Document.BoardSize);

        var finalSize = rig.Engine.BoardSize;
        Assert.AreNotEqual(initialSize, finalSize);
        if (oversized)
            Assert.AreEqual(BoardResize.Maximum, finalSize);
        rig.RenderGrip(rig.GripPosition, down: false, seconds: 12);
        Assert.IsFalse(rig.Page.IsResizing);
        CollectionAssert.AreEqual(new[] { finalSize.WidthMetres }, rig.BoardWidths);
        Assert.AreEqual(1, rig.BoardTextureClears);
        Assert.AreEqual(MemoRenderer.PixelSize(finalSize), rig.BoardTextureSize);
        Assert.AreEqual(rig.NormalImageSize, rig.BoardTextureSize);
        Near(rig.Scene.BoardPose.Position, rig.BoardPose.Position);
        Assert.AreEqual(1, rig.BoardSubmissions);
        Assert.AreEqual(savedBefore + 1, rig.Page.SavedRevision);
        var saved = rig.Page.Catalog.List().Single();
        Assert.AreEqual(finalSize, saved.Document.BoardSize);
        using var png = new Bitmap(Path.ChangeExtension(saved.JsonPath, ".png"));
        Assert.AreEqual(MemoRenderer.PixelSize(finalSize), png.Size);
        rig.RenderGrip(rig.GripPosition, down: false, seconds: 13);
        Assert.AreEqual(0, rig.BoardSubmissions);
    }

    [TestMethod]
    [DataRow(HandSide.Right)]
    [DataRow(HandSide.Left)]
    public void PreviewKeepsInkInPlaceAndClearsShrinkingEdges(HandSide handedness)
    {
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        engine.SetPenStyle("#C53939", .003f);
        engine.UpdatePagePoint(new(.25f, .40f), true, 0);
        engine.UpdatePagePoint(null, false, .1);
        using var preview = new BoardResizePreview(engine, handedness);
        var image = preview.Image;
        bool anchorRight = handedness == HandSide.Left;
        float xScale = image.Width / preview.Size.WidthMetres;
        int inkX = (int)((anchorRight ? .8f - .48f * .75f : .48f * .25f) * xScale);
        int inkY = (int)(.36f * .40f / preview.Size.HeightMetres * image.Height);
        int extendingX = (int)((anchorRight ? .15f : .65f) * xScale);
        int extendingY = (int)(.45f / preview.Size.HeightMetres * image.Height);

        engine.ResizeBoard(new(.72f, .52f), anchorRight);
        preview.Draw(engine.BoardSize);
        Assert.AreEqual(ColorTranslator.FromHtml("#C53939").ToArgb(), image.GetPixel(inkX, inkY).ToArgb());
        Assert.AreEqual(255, (int)image.GetPixel(extendingX, extendingY).A);

        engine.ResizeBoard(new(.52f, .39f), anchorRight);
        preview.Draw(engine.BoardSize);
        Assert.AreEqual(ColorTranslator.FromHtml("#C53939").ToArgb(), image.GetPixel(inkX, inkY).ToArgb());
        Assert.AreEqual(0, (int)image.GetPixel(extendingX, extendingY).A);
        Assert.AreEqual(0, (int)image.GetPixel(anchorRight ? image.Width - 1 : 0, 0).A, "The anchor corner is rounded.");
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < 1e-5f, $"Expected world position {expected}, got {actual}.");

    private sealed class RenderRig : IDisposable
    {
        private const ulong Board = 1, Dock = 2, ResizeHandle = 3;
        private const uint Left = 1, Right = 2;
        private readonly D3DDevice _device = new(0);
        private readonly BoardPainter _painter = new();
        private readonly VrSessionResources _resources = new() { Board = Board, ToolsDock = Dock, ResizeHandle = ResizeHandle };
        private readonly GrabHighlights _highlights = new();
        private readonly RunLog _log;
        private readonly FakeVrOverlay _overlay;
        private readonly List<ulong> _submissions = [];
        private double _seconds;
        public HandwriteEngine Engine { get; }
        public MemoSession Page { get; }
        public VrPageBrowser Pages { get; }
        public GrabScene Scene { get; }
        public VrOverlayView View { get; }
        public FrameResult Frame { get; set; } = new(ContactState.None, null, [], [], false);
        public int PoseUpdates { get; private set; }
        public BoardPose BoardPose { get; private set; }
        public BoardPose HandlePose { get; private set; }
        public List<float> BoardWidths { get; } = [];
        public int BoardTextureClears { get; private set; }
        public int RelativeHandlePoses { get; private set; }
        public int BoardSubmissions => _submissions.Count(handle => handle == Board);
        public int DockSubmissions => _submissions.Count(handle => handle == Dock);
        public Size BoardTextureSize => new(_resources.BoardTexture!.Width, _resources.BoardTexture.Height);
        public Size NormalImageSize => _painter.Image.Size;
        public Vector3 GripPosition { get; private set; }

        public RenderRig(HandSide handedness = HandSide.Right, BoardSize? initialSize = null)
        {
            string folder = Path.Combine("artifacts", "vr-dirty-" + Guid.NewGuid().ToString("N"));
            var options = new AppOptions { SaveFolder = Path.Combine(folder, "pages"), Handedness = handedness };
            _log = new RunLog(Path.Combine(folder, "logs"));
            var pose = new BoardPose(Vector3.Zero, Quaternion.Identity);
            Engine = new(pose, initialSize ?? BoardSize.Default);
            Scene = new(pose, BoardResize.PenDock(pose, BoardSize.Default, handedness)) { Handedness = handedness };
            Page = new(Engine, options.SaveFolder, DateTimeOffset.UnixEpoch, _log.Event);
            Pages = new(Page);
            _overlay = new FakeVrOverlay
            {
                TextureSubmitted = handle =>
                {
                    if (handle is Board or Dock)
                        _submissions.Add(handle);
                },
                TextureCleared = handle =>
                {
                    if (handle == Board)
                        BoardTextureClears++;
                },
                WidthChanged = (handle, width) =>
                {
                    if (handle == Board)
                        BoardWidths.Add(width);
                },
                PoseUpdated = (handle, transform) =>
                {
                    if (handle is Board or Dock)
                        PoseUpdates++;
                    if (handle == Board)
                        BoardPose = VrGeometry.FromOpenVr(transform);
                    if (handle == ResizeHandle)
                        HandlePose = VrGeometry.FromOpenVr(transform);
                },
                RelativePoseUpdated = handle =>
                {
                    if (handle == ResizeHandle)
                        RelativeHandlePoses++;
                },
            };
            _resources.CreateFixedTextures(_device, _painter, options, visible: true, settingsNotice: null);
            _resources.CreateToolTextures(_device);
            var tracked = new TrackedDevicePose_t { mDeviceToAbsoluteTracking = VrGeometry.ToOpenVr(pose) };
            View = new(_overlay, _resources, new(options, _log.Event), Page, Pages, Engine, Scene, [tracked, tracked, tracked],
                index => index <= Right, _log,
                () => _seconds += .001);
        }

        public void RenderGrip(Vector3 position, bool down, double seconds)
        {
            GripPosition = position;
            _submissions.Clear();
            BoardWidths.Clear();
            BoardTextureClears = 0;
            RelativeHandlePoses = 0;
            var hand = new HandInput(new(position, Quaternion.Identity), true, down, false, true);
            var result = VrOverlayHost.UpdatePageGrab(Scene, Engine, Page, new(hand, default, true, false), seconds, Scene.Handedness);
            VrOverlayHost.SyncPageGeometry(Scene, Engine);
            View.RenderFrame(_device, _painter, new(Left, Right, false, false, hand, default, null, null), result, Frame, _highlights);
        }

        public void ExpectUpload(string reason)
        {
            _submissions.Clear();
            var timings = View.RenderFrame(_device, _painter, default, default, Frame, _highlights);
            CollectionAssert.AreEqual(new[] { Board, Dock }, _submissions, reason);
            Assert.IsTrue(timings.RenderMs > 0, reason);
            Assert.IsTrue(timings.UploadMs > 0, reason);
            ExpectIdle();
        }

        public void ExpectIdle()
        {
            _submissions.Clear();
            var timings = View.RenderFrame(_device, _painter, default, default, Frame, _highlights);
            Assert.AreEqual(0, _submissions.Count);
            Assert.AreEqual((0d, 0d), timings);
        }

        public void Dispose()
        {
            _resources.DestroyOverlaysAndDynamicTextures(_overlay);
            _resources.DisposeFixedTextures();
            Pages.Dispose();
            _painter.Dispose();
            _device.Dispose();
            _log.Dispose();
        }
    }
}
