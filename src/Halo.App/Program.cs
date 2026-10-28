using System.Runtime.InteropServices;
using Halo.App.Island;
using static Halo.App.Interop.Native;

namespace Halo.App;

/// <summary>
/// Project Halo entry point. Arguments:
/// <c>--exit</c> closes a running island; <c>--diagnostics</c> writes privacy-safe JSON events to standard output.
/// A second launch while the island runs asks it to expand and exits (single instance, FR-APP-04).
/// </summary>
internal static class Program
{
    private const string InstanceMutexName = @"Local\ProjectHalo.Island";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--exit", StringComparer.Ordinal))
        {
            var running = FindWindowW(IslandWindow.ClassName, null);
            return running != 0 && PostMessageW(running, WmClose, 0, 0) ? 0 : 1;
        }

        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            var running = FindWindowW(IslandWindow.ClassName, null);
            if (running != 0)
            {
                _ = PostMessageW(running, IslandWindow.WmCommandFromInstance, 1, 0);
            }

            return 0;
        }

        try
        {
            var log = args.Contains("--diagnostics", StringComparer.Ordinal) ? new DiagnosticsLog(Console.Out) : null;
            using var window = new IslandWindow(log);
            return window.Run();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Type and HRESULT only: messages can contain paths.
            Console.Error.WriteLine($"Halo failed to start: {ex.GetType().Name} 0x{ex.HResult:X8}");
            return 1;
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
