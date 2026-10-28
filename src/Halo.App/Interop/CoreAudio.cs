using System.Runtime.InteropServices;

namespace Halo.App.Interop;

// Core Audio endpoint interfaces (mmdeviceapi.h, endpointvolume.h). Built-in COM interop creates the wrappers;
// every method keeps its HRESULT (PreserveSig) so a missing or removed device is a value, not an exception.
#pragma warning disable SA1600, CA1711

internal enum DataFlow
{
    Render = 0,
    Capture = 1,
}

internal enum DeviceRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out nint devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(DataFlow dataFlow, DeviceRole role, out IMMDevice? endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    [PreserveSig]
    int OpenPropertyStore(uint access, out nint properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string? deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(DataFlow flow, DeviceRole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string? deviceId, PropertyKey key);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int GetChannelCount(out uint count);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDb);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    /// <summary>Receives an AUDIO_VOLUME_NOTIFICATION_DATA pointer: GUID context, BOOL muted, float master, ...</summary>
    [PreserveSig]
    int OnNotify(nint notify);
}

#pragma warning restore SA1600, CA1711

/// <summary>Constants and helpers shared by the island and its tests.</summary>
internal static class CoreAudio
{
    public const uint ClsctxAll = 0x17;
    public const uint DeviceStateActive = 0x1;

    /// <summary>E_NOTFOUND: there is no default endpoint (no output device).</summary>
    public const int ENotFound = unchecked((int)0x80070490);

    public static readonly Guid AudioEndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;

    public static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    /// <summary>Activates the endpoint volume of the default console render device; null with the HRESULT if none.</summary>
    public static (IMMDevice? Device, IAudioEndpointVolume? Volume, int HResult) DefaultRender(IMMDeviceEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        var hr = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, DeviceRole.Console, out var device);
        if (hr < 0 || device is null)
        {
            return (null, null, hr < 0 ? hr : ENotFound);
        }

        var iid = AudioEndpointVolumeIid;
        hr = device.Activate(ref iid, ClsctxAll, 0, out var instance);
        if (hr < 0 || instance is not IAudioEndpointVolume volume)
        {
            Release(device);
            return (null, null, hr < 0 ? hr : ENotFound);
        }

        return (device, volume, 0);
    }

    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            _ = Marshal.FinalReleaseComObject(com);
        }
    }
}
