using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Composition;

namespace Halo.App.Interop;

internal enum TextAlign
{
    Leading = 0,
    Trailing = 1,
    Center = 2,
}

/// <summary>A DirectWrite text format (font family, size in pixels, weight, alignment). Owned COM pointer.</summary>
internal sealed unsafe class TextFormat : IDisposable
{
    internal TextFormat(nint pointer) => Pointer = pointer;

    internal nint Pointer { get; private set; }

    public void Dispose()
    {
        if (Pointer != 0)
        {
            _ = Marshal.Release(Pointer);
            Pointer = 0;
        }
    }
}

/// <summary>
/// Draws text with Direct2D/DirectWrite into <see cref="CompositionDrawingSurface"/>s (ADR-001 §4.5, option C).
/// COM objects are called through their vtables (slot numbers from the Windows SDK headers, as in the S1 Desktop
/// Duplication code), so no extra package is needed. Surfaces are drawn only when their text changes.
/// </summary>
internal sealed unsafe partial class TextRenderer : IDisposable
{
    private const int D3DDriverTypeUnknown = 0;
    private const int D3DDriverTypeHardware = 1;
    private const int D3DDriverTypeWarp = 5;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const int DWriteFactoryTypeShared = 0;
    private const int D2DTextAntialiasGrayscale = 2;

    // Device-loss HRESULTs: DXGI_ERROR_DEVICE_REMOVED, DXGI_ERROR_DEVICE_RESET, D2DERR_RECREATE_TARGET.
    private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);
    private const int D2DErrRecreateTarget = unchecked((int)0x8899000C);

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IidDxgiFactory6 = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    private static readonly Guid IidDxgiAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private static readonly Guid IidD2DDeviceContext = new("e8f7fe7a-191c-466d-ad95-975678bda998");
    private static readonly Guid IidCompositorInterop = new("25297D5C-3AD4-4C9C-B5CF-E36A38512330");
    private static readonly Guid IidGraphicsDeviceInterop = new("A116FF71-F8BF-4C8A-9C98-70779A32A9C8");
    private static readonly Guid IidDrawingSurfaceInterop = new("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE");

    private nint _d2dDevice;
    private nint _dwriteFactory;

    public TextRenderer(Compositor compositor)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        fixed (Guid* iid = &IidDWriteFactory)
        {
            nint factory;
            Check(DWriteCreateFactory(DWriteFactoryTypeShared, iid, &factory), "DWriteCreateFactory");
            _dwriteFactory = factory;
        }

        _d2dDevice = CreateD2DDevice();

        var compositorUnknown = ((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr;
        Check(Marshal.QueryInterface(compositorUnknown, in IidCompositorInterop, out var interop), "QueryInterface(ICompositorInterop)");
        try
        {
            nint device;
            // ICompositorInterop::CreateGraphicsDevice(IUnknown* renderingDevice, ICompositionGraphicsDevice**) is slot 5.
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)interop)[5])(interop, _d2dDevice, &device), "CreateGraphicsDevice");
            GraphicsDevice = CompositionGraphicsDevice.FromAbi(device);
        }
        finally
        {
            _ = Marshal.Release(interop);
        }
    }

    public CompositionGraphicsDevice GraphicsDevice { get; }

    public TextFormat CreateFormat(string family, float sizePx, int weight, TextAlign align)
    {
        nint format;
        fixed (char* familyName = family)
        fixed (char* locale = "en-us")
        {
            // IDWriteFactory::CreateTextFormat(family, collection, weight, style, stretch, size, locale, format**) is slot 15.
            var create = (delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)(*(nint**)_dwriteFactory)[15];
            Check(create(_dwriteFactory, familyName, 0, weight, 0, 5, sizePx, locale, &format), "CreateTextFormat");
        }

        // IDWriteTextFormat: SetTextAlignment 3, SetParagraphAlignment 4 (centre = 2), SetWordWrapping 5 (none = 1).
        Check(Vtbl<int>(format, 3)(format, (int)align), "SetTextAlignment");
        Check(Vtbl<int>(format, 4)(format, 2), "SetParagraphAlignment");
        Check(Vtbl<int>(format, 5)(format, 1), "SetWordWrapping");
        return new TextFormat(format);
    }

    public CompositionDrawingSurface CreateSurface(int widthPx, int heightPx) =>
        GraphicsDevice.CreateDrawingSurface(
            new Size(Math.Max(1, widthPx), Math.Max(1, heightPx)),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

    /// <summary>
    /// Clears the surface and draws one line of text filling it; characters from <paramref name="smallFrom"/> on
    /// (if not negative) use <paramref name="smallSize"/> pixels. Returns false if the GPU device was lost.
    /// </summary>
    public bool TryDraw(CompositionDrawingSurface surface, string text, TextFormat format, Color color, int smallFrom = -1, float smallSize = 0)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(format);
        var surfaceUnknown = ((WinRT.IWinRTObject)surface).NativeObject.ThisPtr;
        Check(Marshal.QueryInterface(surfaceUnknown, in IidDrawingSurfaceInterop, out var interop), "QueryInterface(ICompositionDrawingSurfaceInterop)");
        try
        {
            nint context;
            Point offset;
            int hr;
            fixed (Guid* iid = &IidD2DDeviceContext)
            {
                // ICompositionDrawingSurfaceInterop::BeginDraw(RECT*, REFIID, void**, POINT*) is slot 3.
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, Point*, int>)(*(nint**)interop)[3])(interop, 0, iid, &context, &offset);
            }

            if (IsDeviceLost(hr))
            {
                return false;
            }

            Check(hr, "BeginDraw");
            try
            {
                DrawInto(context, offset, surface.Size, text, format, color, smallFrom, smallSize);
            }
            finally
            {
                // EndDraw is slot 4; the context returned by BeginDraw is released after it.
                hr = ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)interop)[4])(interop);
                _ = Marshal.Release(context);
            }

            if (IsDeviceLost(hr))
            {
                return false;
            }

            Check(hr, "EndDraw");
            return true;
        }
        finally
        {
            _ = Marshal.Release(interop);
        }
    }

    /// <summary>
    /// Replaces the lost Direct3D/Direct2D device on the existing composition graphics device; surfaces keep their
    /// identity but lose their content, so the caller redraws everything afterwards.
    /// </summary>
    public void RecoverDevice()
    {
        var replacement = CreateD2DDevice();
        var deviceUnknown = ((WinRT.IWinRTObject)GraphicsDevice).NativeObject.ThisPtr;
        Check(Marshal.QueryInterface(deviceUnknown, in IidGraphicsDeviceInterop, out var interop), "QueryInterface(ICompositionGraphicsDeviceInterop)");
        try
        {
            // ICompositionGraphicsDeviceInterop::SetRenderingDevice(IUnknown*) is slot 4.
            Check(Vtbl<nint>(interop, 4)(interop, replacement), "SetRenderingDevice");
        }
        finally
        {
            _ = Marshal.Release(interop);
        }

        _ = Marshal.Release(_d2dDevice);
        _d2dDevice = replacement;
    }

    public void Dispose()
    {
        GraphicsDevice.Dispose();
        if (_d2dDevice != 0)
        {
            _ = Marshal.Release(_d2dDevice);
            _d2dDevice = 0;
        }

        if (_dwriteFactory != 0)
        {
            _ = Marshal.Release(_dwriteFactory);
            _dwriteFactory = 0;
        }
    }

    private static bool IsDeviceLost(int hr) => hr is DxgiErrorDeviceRemoved or DxgiErrorDeviceReset or D2DErrRecreateTarget;

    private void DrawInto(nint context, Point offset, Size size, string text, TextFormat format, Color color, int smallFrom, float smallSize)
    {
        // ID2D1RenderTarget slots: CreateSolidColorBrush 8, DrawText 27, SetTransform 30, SetTextAntialiasMode 34,
        // Clear 47, SetDpi 51. The surface is drawn in pixels (96 DPI), at the offset BeginDraw returned.
        ((delegate* unmanaged[Stdcall]<nint, float, float, void>)(*(nint**)context)[51])(context, 96f, 96f);
        var transform = stackalloc float[] { 1, 0, 0, 1, offset.X, offset.Y };
        ((delegate* unmanaged[Stdcall]<nint, float*, void>)(*(nint**)context)[30])(context, transform);
        ((delegate* unmanaged[Stdcall]<nint, int, void>)(*(nint**)context)[34])(context, D2DTextAntialiasGrayscale);
        var clear = stackalloc float[] { 0, 0, 0, 0 };
        ((delegate* unmanaged[Stdcall]<nint, float*, void>)(*(nint**)context)[47])(context, clear);

        nint layout;
        fixed (char* chars = text)
        {
            // IDWriteFactory::CreateTextLayout(string, length, format, maxWidth, maxHeight, layout**) is slot 18.
            Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float, float, nint*, int>)(*(nint**)_dwriteFactory)[18])(
                _dwriteFactory, chars, (uint)text.Length, format.Pointer, (float)size.Width, (float)size.Height, &layout), "CreateTextLayout");
        }

        var rgba = stackalloc float[] { color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f };
        nint brush = 0;
        try
        {
            if (smallFrom >= 0 && smallFrom < text.Length && smallSize > 0)
            {
                // IDWriteTextLayout::SetFontSize(size, DWRITE_TEXT_RANGE) is slot 35; baselines stay aligned.
                Check(((delegate* unmanaged[Stdcall]<nint, float, TextRange, int>)(*(nint**)layout)[35])(
                    layout, smallSize, new TextRange((uint)smallFrom, (uint)(text.Length - smallFrom))), "SetFontSize");
            }

            Check(((delegate* unmanaged[Stdcall]<nint, float*, nint, nint*, int>)(*(nint**)context)[8])(context, rgba, 0, &brush), "CreateSolidColorBrush");

            // ID2D1RenderTarget::DrawTextLayout(origin, layout, brush, options = none) is slot 28.
            ((delegate* unmanaged[Stdcall]<nint, Point2F, nint, nint, int, void>)(*(nint**)context)[28])(context, default, layout, brush, 0);
        }
        finally
        {
            if (brush != 0)
            {
                _ = Marshal.Release(brush);
            }

            _ = Marshal.Release(layout);
        }
    }

    /// <summary>
    /// Direct3D device for text rasterisation, in order of preference: WARP (software), then the minimum-power GPU,
    /// then the default GPU. A few lines of text redrawn at most once a minute cost little CPU, while a hardware
    /// device loads the GPU's user-mode driver: measured on the dev machine, private memory was about 62 MB with
    /// the NVIDIA device, 68 MB with the Intel device and 15.5 MB with WARP (PROJECT_STATUS §3). Shape animation is
    /// unaffected: it is composed by DWM on the GPU.
    /// </summary>
    private static nint CreateD2DDevice()
    {
        nint d3d = 0;
        var hr = D3D11CreateDevice(0, D3DDriverTypeWarp, 0, D3D11CreateDeviceBgraSupport, null, 0, D3D11SdkVersion, &d3d, null, null);
        if (hr < 0)
        {
            var adapter = MinimumPowerAdapter();
            if (adapter != 0)
            {
                hr = D3D11CreateDevice(adapter, D3DDriverTypeUnknown, 0, D3D11CreateDeviceBgraSupport, null, 0, D3D11SdkVersion, &d3d, null, null);
                _ = Marshal.Release(adapter);
            }
        }

        if (hr < 0)
        {
            Check(D3D11CreateDevice(0, D3DDriverTypeHardware, 0, D3D11CreateDeviceBgraSupport, null, 0, D3D11SdkVersion, &d3d, null, null), "D3D11CreateDevice");
        }

        try
        {
            Check(Marshal.QueryInterface(d3d, in IidDxgiDevice, out var dxgi), "QueryInterface(IDXGIDevice)");
            try
            {
                nint d2d;
                Check(D2D1CreateDevice(dxgi, 0, &d2d), "D2D1CreateDevice");
                return d2d;
            }
            finally
            {
                _ = Marshal.Release(dxgi);
            }
        }
        finally
        {
            _ = Marshal.Release(d3d);
        }
    }

    /// <summary>
    /// The integrated GPU on hybrid laptops (DXGI_GPU_PREFERENCE_MINIMUM_POWER), so a fallback hardware device does
    /// not wake the discrete GPU. Returns 0 when unavailable.
    /// </summary>
    private static nint MinimumPowerAdapter()
    {
        nint factory;
        fixed (Guid* iid = &IidDxgiFactory1)
        {
            if (CreateDXGIFactory1(iid, &factory) < 0)
            {
                return 0;
            }
        }

        try
        {
            if (Marshal.QueryInterface(factory, in IidDxgiFactory6, out var factory6) < 0)
            {
                return 0;
            }

            try
            {
                nint adapter;
                fixed (Guid* iid = &IidDxgiAdapter1)
                {
                    // IDXGIFactory6::EnumAdapterByGpuPreference(index, preference = minimum power, iid, adapter) is slot 29.
                    var hr = ((delegate* unmanaged[Stdcall]<nint, uint, int, Guid*, nint*, int>)(*(nint**)factory6)[29])(factory6, 0, 1, iid, &adapter);
                    return hr < 0 ? 0 : adapter;
                }
            }
            finally
            {
                _ = Marshal.Release(factory6);
            }
        }
        finally
        {
            _ = Marshal.Release(factory);
        }
    }

    private static delegate* unmanaged[Stdcall]<nint, TArg, int> Vtbl<TArg>(nint self, int slot) =>
        (delegate* unmanaged[Stdcall]<nint, TArg, int>)(*(nint**)self)[slot];

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{what} failed: 0x{hr:X8}") { HResult = hr };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct TextRange(uint Start, uint Length);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point2F(float X, float Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint levels, uint sdkVersion, nint* device, int* level, nint* context);

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* iid, nint* factory);

    [LibraryImport("d2d1.dll")]
    private static partial int D2D1CreateDevice(nint dxgiDevice, nint properties, nint* device);

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int factoryType, Guid* iid, nint* factory);
}
