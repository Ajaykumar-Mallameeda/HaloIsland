using System.Runtime.InteropServices;

namespace Halo.Spikes.Windows;

/// <summary>Raw value of one counter instance.</summary>
public readonly record struct RawCounterValue(string Instance, long Value);

/// <summary>
/// Minimal PDH reader for raw counter arrays (built into Windows, no elevation). Used for PB-02
/// (<c>\Thread(*)\Context Switches/sec</c> raw counts) and PB-12 (<c>\GPU Engine(*)\Running Time</c>).
/// Instance names are only parsed for process/thread IDs and engine types; they are never logged.
/// </summary>
public sealed partial class PdhQuery : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhNoData = 0x800007D5;
    private readonly Dictionary<string, nint> _counters = new(StringComparer.Ordinal);
    private nint _query;

    public PdhQuery(params ReadOnlySpan<string> englishPaths)
    {
        Check(PdhOpenQueryW(null, 0, out _query), "PdhOpenQuery");
        foreach (var path in englishPaths)
        {
            Check(PdhAddEnglishCounterW(_query, path, 0, out var counter), "PdhAddEnglishCounter");
            _counters[path] = counter;
        }
    }

    public void Collect() => Check(PdhCollectQueryData(_query), "PdhCollectQueryData");

    /// <summary>All instances of a counter from the last <see cref="Collect"/>.</summary>
    public IReadOnlyList<RawCounterValue> Read(string englishPath)
    {
        var counter = _counters[englishPath];
        uint size = 0;
        var status = PdhGetRawCounterArrayW(counter, ref size, out _, 0);
        if (status == PdhNoData || size == 0)
        {
            return [];
        }

        if (status != PdhMoreData)
        {
            Check(status, "PdhGetRawCounterArray(size)");
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Check(PdhGetRawCounterArrayW(counter, ref size, out var count, buffer), "PdhGetRawCounterArray");
            var result = new List<RawCounterValue>((int)count);
            for (var i = 0; i < count; i++)
            {
                // PDH_RAW_COUNTER_ITEM_W on x64: szName (8), PDH_RAW_COUNTER { CStatus 4, FILETIME 8, FirstValue @16 ... } = 48 bytes.
                var item = buffer + (i * 48);
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? string.Empty;
                var cstatus = (uint)Marshal.ReadInt32(item + 8);
                if (cstatus is 0 or 1)
                {
                    result.Add(new RawCounterValue(name, Marshal.ReadInt64(item + 8 + 16)));
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            _ = PdhCloseQuery(_query);
            _query = 0;
        }
    }

    private static void Check(uint status, string what)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{what} failed: 0x{status:X8}");
        }
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounterW(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetRawCounterArrayW(nint counter, ref uint bufferSize, out uint itemCount, nint itemBuffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);
}

/// <summary>Per-process counter readings needed by PB-02 and PB-12.</summary>
public sealed class ProcessCounters : IDisposable
{
    private const string ContextSwitches = @"\Thread(*)\Context Switches/sec";
    private const string ThreadId = @"\Thread(*)\ID Thread";
    private const string ThreadProcessId = @"\Thread(*)\ID Process";
    private const string GpuRunningTime = @"\GPU Engine(*)\Running Time";
    private readonly PdhQuery _query = new(ContextSwitches, ThreadId, ThreadProcessId, GpuRunningTime);

    public sealed record Sample(IReadOnlyDictionary<int, long> ContextSwitchesByThread, long Gpu3dRunningTime100ns, long GpuAllRunningTime100ns, bool HasGpuInstances, long Dwm3dRunningTime100ns);

    public Sample Read(int processId, int dwmProcessId)
    {
        _query.Collect();
        // Thread instance names are not unique (e.g. several "svchost/0"), so the three arrays from the same
        // collection are aligned by position; the names are compared pairwise as a consistency check.
        var tids = _query.Read(ThreadId);
        var pids = _query.Read(ThreadProcessId);
        var css = _query.Read(ContextSwitches);
        if (tids.Count != pids.Count || tids.Count != css.Count)
        {
            throw new InvalidOperationException("Thread counter arrays are not aligned.");
        }

        var switches = new Dictionary<int, long>();
        for (var i = 0; i < css.Count; i++)
        {
            if (!string.Equals(css[i].Instance, tids[i].Instance, StringComparison.Ordinal) || !string.Equals(css[i].Instance, pids[i].Instance, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Thread counter arrays are not aligned.");
            }

            if (pids[i].Value == processId)
            {
                switches[(int)tids[i].Value] = css[i].Value;
            }
        }

        long gpu3d = 0, gpuAll = 0, dwm3d = 0;
        var any = false;
        var prefix = $"pid_{processId}_";
        var dwmPrefix = $"pid_{dwmProcessId}_";
        foreach (var g in _query.Read(GpuRunningTime))
        {
            var is3d = g.Instance.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase);
            if (g.Instance.StartsWith(prefix, StringComparison.Ordinal))
            {
                any = true;
                gpuAll += g.Value;
                gpu3d += is3d ? g.Value : 0;
            }
            else if (is3d && g.Instance.StartsWith(dwmPrefix, StringComparison.Ordinal))
            {
                dwm3d += g.Value;
            }
        }

        return new Sample(switches, gpu3d, gpuAll, any, dwm3d);
    }

    public void Dispose() => _query.Dispose();
}
