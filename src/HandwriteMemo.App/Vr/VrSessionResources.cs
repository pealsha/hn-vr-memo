using HandwriteMemo.Core;
using Valve.VR;

namespace HandwriteMemo.App;

// One SteamVR connection owns these handles, textures and the source images for fixed textures.
// The caller keeps the original scopes for input, device and painter.
internal sealed class VrSessionResources
{
    internal ulong Board { get; set; }
    internal ulong ToolsDock { get; set; }
    internal ulong ResizeHandle { get; set; }
    internal ulong Dashboard { get; set; }
    internal ulong Thumbnail { get; set; }
    internal ulong PenMarker { get; set; }
    internal ulong EraserMarker { get; set; }
    internal ulong WristCue { get; set; }
    internal ulong WristCalibration { get; set; }
    internal D3DTexture WristCalibrationTexture { get; private set; } = null!;
    internal List<ulong> Handles { get; } = [];
    internal ulong[] PenFaces { get; } = new ulong[PenPainter.FaceCount];
    internal ulong[] EraserFaces { get; } = new ulong[EraserPainter.FaceCount];
    internal Dictionary<ulong, OverlayMotion> Attachments { get; } = [];
    internal D3DTexture ToolsTexture { get; private set; } = null!;
    internal D3DTexture? BoardTexture { get; private set; }
    private BoardResizePreview? _resizePreview;
    internal Bitmap DashboardImage { get; private set; } = null!;
    internal D3DTexture DashboardTexture { get; private set; } = null!;
    internal Bitmap TabImage { get; private set; } = null!;
    internal D3DTexture TabTexture { get; private set; } = null!;
    internal Bitmap TipImage { get; private set; } = null!;
    internal D3DTexture TipTexture { get; private set; } = null!;
    internal List<D3DTexture> EraserTextures { get; } = [];
    internal List<D3DTexture> PenTextures { get; } = [];
    internal List<D3DTexture> ResizeTextures { get; } = [];
    internal List<D3DTexture> WristCueTextures { get; } = [];

    internal void CreateFixedTextures(D3DDevice device, BoardPainter painter, AppOptions options, bool visible, string? settingsNotice)
    {
        try
        {
            ToolsTexture = new D3DTexture(device, painter.ToolsImage.Width, painter.ToolsImage.Height);
            WristCalibrationTexture = new D3DTexture(device, 800, 160);
            DashboardImage = VrOverlaySprites.MakeDashboard(0, options.GestureMode, visible, options.Handedness, options.BoardBrightness, settingsNotice, options.WristButtonActivationMode);
            DashboardTexture = new D3DTexture(device, DashboardImage.Width, DashboardImage.Height);
            TabImage = VrOverlaySprites.MakeDashboardTab();
            TabTexture = new D3DTexture(device, TabImage.Width, TabImage.Height);
            TipImage = VrOverlaySprites.MakeMarker(Color.OrangeRed);
            TipTexture = new D3DTexture(device, 64, 64);
        }
        catch
        {
            DisposeFixedTextures();
            throw;
        }
    }

    internal void CreateToolTextures(D3DDevice device)
    {
        for (int state = 0; state < 2; state++)
        {
            using var image = VrOverlaySprites.MakeWristCue(state == 1);
            var texture = new D3DTexture(device, image.Width, image.Height);
            WristCueTextures.Add(texture);
            texture.Upload(image);
        }
        for (int hand = 0; hand < 2; hand++)
            for (int state = 0; state < 4; state++)
            {
                using var image = VrOverlaySprites.MakeResizeHandle(state is 1 or 3, state != 2, state == 3);
                if (hand == 1)
                    image.RotateFlip(RotateFlipType.RotateNoneFlipX);
                var texture = new D3DTexture(device, image.Width, image.Height);
                ResizeTextures.Add(texture);
                texture.Upload(image);
            }
        for (int selected = 0; selected < 2; selected++)
            for (int face = 0; face < PenPainter.FaceCount; face++)
            {
                using var image = PenPainter.MakeFace(face, selected == 1);
                var texture = new D3DTexture(device, image.Width, image.Height);
                PenTextures.Add(texture);
                texture.Upload(image);
            }
        for (int selected = 0; selected < 2; selected++)
            for (int face = 0; face < EraserPainter.FaceCount; face++)
            {
                using var image = EraserPainter.MakeFace(face, selected == 1);
                var texture = new D3DTexture(device, image.Width, image.Height);
                EraserTextures.Add(texture);
                texture.Upload(image);
            }
    }

    internal BoardResizePreview GetResizePreview(HandwriteEngine engine, HandSide handedness)
    {
        if (_resizePreview?.Handedness != handedness)
        {
            EndResizePreview();
            _resizePreview = new(engine, handedness);
        }
        return _resizePreview;
    }

    internal void EndResizePreview()
    {
        _resizePreview?.Dispose();
        _resizePreview = null;
    }

    internal void EnsureBoardTexture(IOpenVrOverlay overlay, D3DDevice device, Bitmap image)
    {
        if (BoardTexture is null || BoardTexture.Width != image.Width || BoardTexture.Height != image.Height)
        {
            var error = overlay.ClearOverlayTexture(Board);
            if (error != EVROverlayError.None)
                throw new InvalidOperationException($"clear resized board texture: {error}");
            BoardTexture?.Dispose();
            BoardTexture = new D3DTexture(device, image.Width, image.Height);
        }
    }

    internal void DestroyOverlaysAndDynamicTextures(IOpenVrOverlay overlay)
    {
        foreach (var handle in Handles.AsEnumerable().Reverse())
        {
            overlay.HideOverlay(handle);
            overlay.ClearOverlayTexture(handle);
            overlay.DestroyOverlay(handle);
        }
        Handles.Clear();
        Attachments.Clear();
        EndResizePreview();
        BoardTexture?.Dispose();
        foreach (var texture in EraserTextures)
            texture.Dispose();
        foreach (var texture in PenTextures)
            texture.Dispose();
        foreach (var texture in ResizeTextures)
            texture.Dispose();
        foreach (var texture in WristCueTextures)
            texture.Dispose();
    }

    internal void DisposeFixedTextures()
    {
        // Release in reverse creation order, even when a Dispose throws.
        // Nullable values also cover a failure partway through CreateFixedTextures.
        using var toolsTexture = ToolsTexture;
        using var calibrationTexture = WristCalibrationTexture;
        using var dashboardImage = DashboardImage;
        using var dashboardTexture = DashboardTexture;
        using var tabImage = TabImage;
        using var tabTexture = TabTexture;
        using var tipImage = TipImage;
        using var tipTexture = TipTexture;
    }
}
