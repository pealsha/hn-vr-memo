using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HandwriteMemo.App;

internal static class FoundationPixels
{
    internal static byte[] Read(Bitmap image)
    {
        var data = image.LockBits(new(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[image.Width * image.Height * 4];
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * image.Width * 4, image.Width * 4);
            return pixels;
        }
        finally { image.UnlockBits(data); }
    }

    internal static int Differences(Bitmap before, Bitmap after)
    {
        if (before.Size != after.Size) throw new InvalidOperationException("Image dimensions changed.");
        byte[] a = Read(before), b = Read(after);
        int differences = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (!a.AsSpan(i, 4).SequenceEqual(b.AsSpan(i, 4))) differences++;
        return differences;
    }
}
