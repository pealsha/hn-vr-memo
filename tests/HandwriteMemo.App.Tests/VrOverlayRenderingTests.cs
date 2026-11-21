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
        Assert.IsTrue(rig.Page.Resize(new(.56f, .42f), 5));
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

    private sealed class RenderRig : IDisposable
    {
        private const ulong Board = 1, Dock = 2, ResizeHandle = 3;
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

        public RenderRig()
        {
            string folder = Path.Combine("artifacts", "vr-dirty-" + Guid.NewGuid().ToString("N"));
            var options = new AppOptions { SaveFolder = Path.Combine(folder, "pages") };
            _log = new RunLog(Path.Combine(folder, "logs"));
            var pose = new BoardPose(Vector3.Zero, Quaternion.Identity);
            Engine = new(pose);
            Scene = new(pose, BoardResize.PenDock(pose, BoardSize.Default, HandSide.Right));
            Page = new(Engine, options.SaveFolder, DateTimeOffset.UnixEpoch, _log.Event);
            Pages = new(Page);
            _overlay = new FakeVrOverlay
            {
                TextureSubmitted = handle =>
                {
                    if (handle is Board or Dock)
                        _submissions.Add(handle);
                },
                PoseUpdated = handle =>
                {
                    if (handle is Board or Dock)
                        PoseUpdates++;
                },
            };
            _resources.CreateFixedTextures(_device, _painter, options, visible: true, settingsNotice: null);
            _resources.CreateToolTextures(_device);
            View = new(_overlay, _resources, new(options, _log.Event), Page, Pages, Engine, Scene, [], _ => false, _log,
                () => _seconds += .001);
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
