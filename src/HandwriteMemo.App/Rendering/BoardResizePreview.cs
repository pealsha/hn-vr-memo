using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Ink stays fixed relative to the anchor while the two opposite paper edges move.
internal sealed class BoardResizePreview : IDisposable
{
    private readonly TextureBrush _paper;
    public HandSide Handedness { get; }
    public BoardSize Size { get; }
    public Bitmap Image { get; }

    public BoardResizePreview(HandwriteEngine engine, HandSide handedness)
    {
        Handedness = handedness;
        var current = engine.BoardSize;
        var maximum = BoardResize.Maximum;
        Size = new(Math.Max(maximum.WidthMetres, current.WidthMetres), Math.Max(maximum.HeightMetres, current.HeightMetres));
        var pixels = MemoRenderer.PixelSize(Size);
        using var paper = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        float xScale = pixels.Width / Size.WidthMetres;
        float yScale = pixels.Height / Size.HeightMetres;
        bool anchorRight = handedness == HandSide.Left;
        float paperLeft = anchorRight ? pixels.Width - current.WidthMetres * xScale : 0;
        using (var g = Graphics.FromImage(paper))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            BoardPainter.DrawPaperGrid(g, pixels, anchorRight);
            foreach (var stroke in engine.Strokes)
            {
                var points = stroke.Points.Select(p => new PointF(
                    paperLeft + p.Uv.X * current.WidthMetres * xScale,
                    p.Uv.Y * current.HeightMetres * yScale)).ToArray();
                MemoRenderer.DrawStroke(g, points, stroke.Color, stroke.WidthMetres * xScale);
            }
        }
        _paper = new TextureBrush(paper, WrapMode.Clamp);
        try
        {
            Image = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        }
        catch
        {
            _paper.Dispose();
            throw;
        }
    }

    public BoardPose PoseFor(BoardPose board, BoardSize paper) => new(board.LocalToWorld(new(
        BoardResize.Direction(Handedness) * (Size.WidthMetres - paper.WidthMetres) / 2,
        (paper.HeightMetres - Size.HeightMetres) / 2, 0)), board.Rotation);

    public void Draw(BoardSize paper)
    {
        float width = paper.WidthMetres / Size.WidthMetres * Image.Width;
        float height = paper.HeightMetres / Size.HeightMetres * Image.Height;
        float left = Handedness == HandSide.Left ? Image.Width - width : 0;
        using var shape = UiTheme.Rounded(new RectangleF(left, 0, width, height), UiDesign.PaperRadius);
        using var g = Graphics.FromImage(Image);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(_paper, shape);
    }

    public void Dispose()
    {
        Image.Dispose();
        _paper.Dispose();
    }
}
