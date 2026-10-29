using System.Runtime.InteropServices;
using Halo.App.Interop;
using Halo.Core.Audio;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// System volume through Core Audio (FR-SYS-01, M3-F03). Follows the default console render endpoint, the device the
/// volume keys and the taskbar flyout control. Event-driven only:
/// <list type="bullet">
/// <item><c>IAudioEndpointVolumeCallback</c> reports every change (keys, flyout, mixer, other apps, Halo). It runs on
/// an audio thread, stores the reading and posts <see cref="ChangedMessage"/> at most once until the UI thread has
/// taken it, so a burst of changes becomes one update.</item>
/// <item><c>IMMNotificationClient</c> reports a new default device or a removed one; the UI thread re-binds after
/// <see cref="DeviceMessage"/> (debounced by the island).</item>
/// </list>
/// Changes Halo makes carry <see cref="HaloContext"/>, so they update the slider but do not raise the pill.
/// Device IDs are never logged or shown.
/// </summary>
internal sealed class VolumeWatcher : IDisposable
{
    public const uint ChangedMessage = WmApp + 6;
    public const uint DeviceMessage = WmApp + 7;

    /// <summary>Event context of Halo's own changes.</summary>
    public static readonly Guid HaloContext = new("6F1C3A2E-9B4D-4E7A-8C21-5D0B7E3F9A41");

    private readonly nint _hwnd;
    private readonly bool _noDevice;
    private readonly Lock _gate = new();
    private readonly VolumeCallback _callback;
    private readonly DeviceClient _client;
    private IMMDeviceEnumerator? _enumerator;
    private bool _clientRegistered;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _volume;
    private string? _deviceId;
    private (VolumeReading Reading, bool FromHalo)? _pending;
    private int _posted;
    private volatile bool _disposed;

    /// <param name="hwnd">Window that receives <see cref="ChangedMessage"/> and <see cref="DeviceMessage"/>.</param>
    /// <param name="noDevice">Test fault: behave as if Windows reports no output device.</param>
    public VolumeWatcher(nint hwnd, bool noDevice = false)
    {
        _hwnd = hwnd;
        _noDevice = noDevice;
        _callback = new VolumeCallback(this);
        _client = new DeviceClient(this);
    }

    /// <summary>The last reading taken on the UI thread, or null without a device.</summary>
    public VolumeReading? Current { get; private set; }

    /// <summary>HRESULT of the last failed bind (0 when bound), for diagnostics.</summary>
    public int LastHResult { get; private set; }

    public bool HasDevice => _volume is not null;

    /// <summary>The bound device's bus and name (J-F04), or null without a device. The name is never logged.</summary>
    public Halo.Core.Audio.AudioDeviceInfo? Device { get; private set; }

    /// <summary>The bound endpoint's bus as a diagnostics token ("hdaudio", "bthenum", "usb"…; "unknown" if unreadable).</summary>
    public string BusToken { get; private set; } = "none";

    /// <summary>Creates the device enumerator and binds the default device. False if Core Audio is unavailable.</summary>
    public bool Start()
    {
        try
        {
            _enumerator = CoreAudio.CreateEnumerator();
            _clientRegistered = _enumerator.RegisterEndpointNotificationCallback(_client) >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            LastHResult = ex.HResult;
            return false;
        }

        Bind();
        return true;
    }

    /// <summary>Drops the current device and binds the default one again (after a device change or a failure).</summary>
    public void Rebind()
    {
        Unbind();
        Bind();
    }

    /// <summary>Takes the newest change posted by the audio thread (UI thread, after <see cref="ChangedMessage"/>).</summary>
    public (VolumeReading Reading, bool FromHalo)? TakeChange()
    {
        _ = Interlocked.Exchange(ref _posted, 0);
        lock (_gate)
        {
            var change = _pending;
            _pending = null;
            if (change is { } c && _volume is not null)
            {
                Current = c.Reading;
            }

            return _volume is null ? null : change;
        }
    }

    /// <summary>Sets the master volume (slider). Moving the slider while muted unmutes, as Windows' own slider does.</summary>
    public bool SetLevel(float level)
    {
        if (_volume is not { } volume)
        {
            return false;
        }

        var context = HaloContext;
        var hr = volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context);
        if (hr >= 0 && Current is { Muted: true })
        {
            hr = volume.SetMute(false, ref context);
        }

        return Checked(hr);
    }

    public bool SetMuted(bool muted)
    {
        if (_volume is not { } volume)
        {
            return false;
        }

        var context = HaloContext;
        return Checked(volume.SetMute(muted, ref context));
    }

    public void Dispose()
    {
        _disposed = true;
        Unbind();
        if (_enumerator is { } enumerator)
        {
            if (_clientRegistered)
            {
                _ = enumerator.UnregisterEndpointNotificationCallback(_client);
                _clientRegistered = false;
            }

            CoreAudio.Release(enumerator);
            _enumerator = null;
        }
    }

    private void Bind()
    {
        if (_disposed || _enumerator is null)
        {
            return;
        }

        if (_noDevice)
        {
            LastHResult = CoreAudio.ENotFound;
            return;
        }

        var (device, volume, hr) = CoreAudio.DefaultRender(_enumerator);
        LastHResult = hr;
        if (device is null || volume is null)
        {
            return;
        }

        if (volume.GetMasterVolumeLevelScalar(out var level) < 0 || volume.GetMute(out var muted) < 0 || volume.RegisterControlChangeNotify(_callback) < 0)
        {
            CoreAudio.Release(volume);
            CoreAudio.Release(device);
            LastHResult = CoreAudio.ENotFound;
            return;
        }

        _ = device.GetId(out var id);
        var (enumerator, name) = CoreAudio.DeviceNames(device);
        Device = Halo.Core.Audio.AudioDeviceInfo.From(enumerator, name);
        BusToken = enumerator is { Length: > 0 and <= 16 } e && e.All(char.IsAsciiLetterOrDigit) ? e.ToLowerInvariant() : enumerator is null ? "unknown" : "other";
        lock (_gate)
        {
            _device = device;
            _volume = volume;
            _deviceId = id;
            _pending = null;
            Current = new VolumeReading(level, muted);
        }
    }

    private void Unbind()
    {
        IAudioEndpointVolume? volume;
        IMMDevice? device;
        lock (_gate)
        {
            (volume, device) = (_volume, _device);
            (_volume, _device, _deviceId, _pending) = (null, null, null, null);
            Current = null;
            Device = null;
            BusToken = "none";
        }

        if (volume is not null)
        {
            _ = volume.UnregisterControlChangeNotify(_callback);
            CoreAudio.Release(volume);
        }

        CoreAudio.Release(device);
    }

    /// <summary>A failed call usually means the device just went away: ask for a re-bind.</summary>
    private bool Checked(int hr)
    {
        if (hr < 0)
        {
            LastHResult = hr;
            _ = PostMessageW(_hwnd, DeviceMessage, 0, 0);
        }

        return hr >= 0;
    }

    /// <summary>Audio thread: keep the newest reading and post once until the UI thread takes it.</summary>
    private unsafe void OnNotify(nint data)
    {
        if (_disposed || data == 0)
        {
            return;
        }

        // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext; BOOL bMuted; float fMasterVolume; ...
        var context = *(Guid*)data;
        var muted = *(int*)(data + 16) != 0;
        var level = *(float*)(data + 20);
        lock (_gate)
        {
            _pending = (new VolumeReading(level, muted), context == HaloContext);
        }

        if (Interlocked.Exchange(ref _posted, 1) == 0)
        {
            _ = PostMessageW(_hwnd, ChangedMessage, 0, 0);
        }
    }

    private void OnDeviceEvent(string? deviceId, bool defaultChanged)
    {
        if (_disposed)
        {
            return;
        }

        // A new default always matters; other device events only when they concern the bound device or none is bound.
        bool relevant;
        lock (_gate)
        {
            relevant = defaultChanged || _deviceId is null || string.Equals(deviceId, _deviceId, StringComparison.OrdinalIgnoreCase);
        }

        if (relevant)
        {
            _ = PostMessageW(_hwnd, DeviceMessage, 0, 0);
        }
    }

    /// <summary>COM callback object for volume changes (called on Core Audio's threads; must not block).</summary>
    private sealed class VolumeCallback(VolumeWatcher owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notify)
        {
            owner.OnNotify(notify);
            return 0;
        }
    }

    /// <summary>COM callback object for device changes (called on Core Audio's threads; must not call back into it).</summary>
    private sealed class DeviceClient(VolumeWatcher owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string? deviceId, uint newState)
        {
            owner.OnDeviceEvent(deviceId, defaultChanged: false);
            return 0;
        }

        public int OnDeviceAdded(string? deviceId)
        {
            owner.OnDeviceEvent(deviceId, defaultChanged: false);
            return 0;
        }

        public int OnDeviceRemoved(string? deviceId)
        {
            owner.OnDeviceEvent(deviceId, defaultChanged: false);
            return 0;
        }

        public int OnDefaultDeviceChanged(DataFlow flow, DeviceRole role, string? defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == DeviceRole.Console)
            {
                owner.OnDeviceEvent(defaultDeviceId, defaultChanged: true);
            }

            return 0;
        }

        public int OnPropertyValueChanged(string? deviceId, PropertyKey key) => 0;
    }
}
