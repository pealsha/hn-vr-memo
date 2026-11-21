using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace HandwriteMemo.App;

// Vortice owns COM interop. The app only uploads BGRA pixels to a reusable D3D11 texture.
internal sealed class D3DDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }

    public D3DDevice(int adapterIndex)
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1((uint)adapterIndex, out var selectedAdapter).CheckError();
        using var adapter = selectedAdapter;
        D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;
        Console.WriteLine($"D3D11 adapter {adapterIndex}: {adapter.Description1.Description}");
    }

    public void Dispose()
    {
        Context.Dispose();
        Device.Dispose();
    }
}

internal sealed class D3DTexture : IDisposable
{
    private readonly D3DDevice _device;
    public ID3D11Texture2D Texture { get; }
    public int Width { get; }
    public int Height { get; }

    public D3DTexture(D3DDevice device, int width, int height)
    {
        this._device = device;
        Width = width;
        Height = height;
        Texture = device.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.Shared
        });
    }

    public void Upload(Bitmap bitmap)
    {
        if (bitmap.Width != Width || bitmap.Height != Height)
            throw new ArgumentException("Bitmap and texture size must match.");
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride != Width * 4)
                throw new InvalidOperationException("Unexpected bitmap stride.");
            _device.Context.UpdateSubresource(Texture, 0, null, data.Scan0, (uint)data.Stride, 0);
            _device.Context.Flush();
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public void Dispose() => Texture.Dispose();
}
