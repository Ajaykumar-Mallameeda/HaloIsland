using System.Diagnostics;
using System.Runtime.InteropServices;
using Halo.Spikes.Measurement;

namespace Halo.Spikes.Windows;

/// <summary>One desktop update reported by DXGI Desktop Duplication (metadata only; pixels are never read).</summary>
public readonly record struct DesktopFrame(long Qpc, uint AccumulatedFrames, bool Attributed, bool RectsCoalesced);

/// <summary>
/// Frame attribution method (PROTOTYPE_S1_S2_REPORT §8.5). Records the QPC present time of every desktop update
/// and whether any dirty or move rectangle intersects a region. Uses raw COM vtables (DXGI 1.2), no package.
/// The desktop image is never mapped, copied or stored.
/// </summary>
public sealed unsafe class DesktopDuplication : IDisposable
{
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);
    private const int DxgiErrorAccessLost = unchecked((int)0x887A0026);
    private const int VtFactoryEnumAdapters1 = 12;
    private const int VtAdapterEnumOutputs = 7;
    private const int VtOutput1DuplicateOutput = 22;
    private const int VtDuplAcquireNextFrame = 8;
    private const int VtDuplGetFrameDirtyRects = 9;
    private const int VtDuplGetFrameMoveRects = 10;
    private const int VtDuplReleaseFrame = 14;

    private readonly Lock _gate = new();
    private readonly List<DesktopFrame> _frames = new(capacity: 200_000);
    private readonly byte[] _metadata = new byte[64 * 1024];
    private nint _device;
    private nint _context;
    private nint _duplication;
    private Thread? _thread;
    private volatile bool _stop;
    private PxRect _region;

    public int ErrorHResult { get; private set; }

    public bool AccessLost { get; private set; }

    public long PointerOnlyUpdates { get; private set; }

    /// <summary>Creates the duplication on the adapter/output that contains the primary monitor's origin.</summary>
    public static DesktopDuplication Create(PxRect region)
    {
        var d = new DesktopDuplication { _region = region };
        d.Initialize();
        return d;
    }

    public void SetRegion(PxRect region)
    {
        lock (_gate)
        {
            _region = region;
        }
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "desktop-duplication" };
        _thread.Start();
    }

    /// <summary>Snapshot of all frames recorded so far.</summary>
    public IReadOnlyList<DesktopFrame> Frames()
    {
        lock (_gate)
        {
            return _frames.ToArray();
        }
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        Stop();
        Release(ref _duplication);
        Release(ref _context);
        Release(ref _device);
    }

    private void Initialize()
    {
        var iidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Check(CreateDXGIFactory1(&iidFactory1, out var factory), "CreateDXGIFactory1");
        try
        {
            for (uint a = 0; ; a++)
            {
                nint adapter;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vt(factory, VtFactoryEnumAdapters1))(factory, a, &adapter) != 0)
                {
                    break;
                }

                try
                {
                    for (uint o = 0; ; o++)
                    {
                        nint output;
                        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vt(adapter, VtAdapterEnumOutputs))(adapter, o, &output) != 0)
                        {
                            break;
                        }

                        try
                        {
                            if (TryDuplicate(adapter, output))
                            {
                                return;
                            }
                        }
                        finally
                        {
                            Marshal.Release(output);
                        }
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        throw new InvalidOperationException("No output could be duplicated.");
    }

    private bool TryDuplicate(nint adapter, nint output)
    {
        // Only the output that contains the primary monitor (desktop origin) is used.
        var desc = stackalloc byte[96];
        if (((delegate* unmanaged[Stdcall]<nint, byte*, int>)Vt(output, 7))(output, desc) != 0)
        {
            return false;
        }

        var left = *(int*)(desc + 64);
        var top = *(int*)(desc + 68);
        var right = *(int*)(desc + 72);
        var bottom = *(int*)(desc + 76);
        if (!(left <= 0 && top <= 0 && right > 0 && bottom > 0))
        {
            return false;
        }

        Check(D3D11CreateDevice(adapter, 0, 0, 0x20, null, 0, 7, out _device, out _, out _context), "D3D11CreateDevice");
        var iidOutput1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
        Check(Marshal.QueryInterface(output, in iidOutput1, out var output1), "QueryInterface(IDXGIOutput1)");
        try
        {
            nint dupl;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Vt(output1, VtOutput1DuplicateOutput))(output1, _device, &dupl), "DuplicateOutput");
            _duplication = dupl;
            return true;
        }
        finally
        {
            Marshal.Release(output1);
        }
    }

    private void Loop()
    {
        fixed (byte* buffer = _metadata)
        {
            while (!_stop)
            {
                FrameInfo info;
                nint resource;
                var hr = ((delegate* unmanaged[Stdcall]<nint, uint, FrameInfo*, nint*, int>)Vt(_duplication, VtDuplAcquireNextFrame))(_duplication, 100, &info, &resource);
                if (hr == DxgiErrorWaitTimeout)
                {
                    continue;
                }

                if (hr != 0)
                {
                    ErrorHResult = hr;
                    AccessLost = hr == DxgiErrorAccessLost;
                    return;
                }

                try
                {
                    if (info.LastPresentTime == 0)
                    {
                        PointerOnlyUpdates++;
                        continue;
                    }

                    PxRect region;
                    lock (_gate)
                    {
                        region = _region;
                    }

                    var attributed = AnyRectIntersects(buffer, info.TotalMetadataBufferSize, region);
                    lock (_gate)
                    {
                        _frames.Add(new DesktopFrame(info.LastPresentTime, info.AccumulatedFrames, attributed, info.RectsCoalesced != 0));
                    }
                }
                finally
                {
                    if (resource != 0)
                    {
                        Marshal.Release(resource);
                    }

                    ((delegate* unmanaged[Stdcall]<nint, int>)Vt(_duplication, VtDuplReleaseFrame))(_duplication);
                }
            }
        }
    }

    private bool AnyRectIntersects(byte* buffer, uint totalMetadata, PxRect region)
    {
        if (totalMetadata == 0)
        {
            return false;
        }

        uint required;
        // Move rects: DXGI_OUTDUPL_MOVE_RECT = POINT source (8 bytes) + RECT destination (16 bytes).
        if (((delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, int>)Vt(_duplication, VtDuplGetFrameMoveRects))(_duplication, (uint)_metadata.Length, buffer, &required) == 0)
        {
            for (var p = 0; p + 24 <= required; p += 24)
            {
                if (Intersects((int*)(buffer + p + 8), region))
                {
                    return true;
                }
            }
        }
        else
        {
            return true; // Unknown metadata: count conservatively as attributed.
        }

        if (((delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, int>)Vt(_duplication, VtDuplGetFrameDirtyRects))(_duplication, (uint)_metadata.Length, buffer, &required) != 0)
        {
            return true;
        }

        for (var p = 0; p + 16 <= required; p += 16)
        {
            if (Intersects((int*)(buffer + p), region))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Intersects(int* r, PxRect region) =>
        PxRect.FromEdges(r[0], r[1], r[2], r[3]).Intersects(region);

    private static nint Vt(nint obj, int slot) => (*(nint**)obj)[slot];

    private static void Release(ref nint p)
    {
        if (p != 0)
        {
            Marshal.Release(p);
            p = 0;
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr != 0)
        {
            throw new InvalidOperationException($"{what} failed: 0x{hr:X8}") { HResult = hr };
        }
    }

    public static long QpcFrequency => Stopwatch.Frequency;

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(Guid* riid, out nint factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint levels, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameInfo
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public int PointerX;
        public int PointerY;
        public int PointerVisible;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }
}
