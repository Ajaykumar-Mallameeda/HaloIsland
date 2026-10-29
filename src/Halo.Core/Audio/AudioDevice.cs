using Halo.Core.Island;

namespace Halo.Core.Audio;

/// <summary>
/// The default output device as far as the island needs it (J-F04): whether it is Bluetooth and its display name.
/// The name is shown, never logged. Battery level is deliberately absent: Windows documents no API for the battery
/// of classic Bluetooth audio devices, and Halo never infers one.
/// </summary>
/// <param name="IsBluetooth">The endpoint's device enumerator is a Bluetooth bus (BTHENUM, BTHLEDEVICE, BTHHFENUM).</param>
/// <param name="Name">Friendly name from the endpoint (for example "Headphones (WH-1000XM4)"), cleaned and capped.</param>
public readonly record struct AudioDeviceInfo(bool IsBluetooth, string Name)
{
    /// <summary>Classifies an endpoint by its PKEY_Device_EnumeratorName value.</summary>
    public static bool IsBluetoothEnumerator(string? enumerator) =>
        enumerator is not null && (enumerator.Equals("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || enumerator.Equals("BTHLEDEVICE", StringComparison.OrdinalIgnoreCase)
            || enumerator.Equals("BTHHFENUM", StringComparison.OrdinalIgnoreCase));

    public static AudioDeviceInfo From(string? enumerator, string? friendlyName)
    {
        var name = Media.MediaText.Clean(friendlyName);
        return new AudioDeviceInfo(IsBluetoothEnumerator(enumerator), name.Length > 64 ? name[..64] : name);
    }
}

/// <summary>
/// Bluetooth audio connection pills (J-F04): when the default output switches to a Bluetooth device, "Connected";
/// when it switches away from one (disconnected or replaced), "Disconnected". The first reading after start or after
/// the feature is turned on only records the state. Wired-to-wired switches show nothing.
/// </summary>
public sealed class AudioDeviceMonitor
{
    public const string Key = "audio.device";
    private const string Headphones = "";
    private bool _known;

    public AudioDeviceInfo? Current { get; private set; }

    /// <summary>Applies a new default device (null = no output device). Returns the pill to show, if any.</summary>
    public IslandAlert? Update(AudioDeviceInfo? next)
    {
        var previous = Current;
        Current = next;
        if (!_known)
        {
            _known = true;
            return null;
        }

        if (next is { IsBluetooth: true } connected && previous != next)
        {
            return Alert(connected.Name.Length > 0 ? connected.Name : "Bluetooth audio", "Connected", connected.Name.Length > 0 ? $"{connected.Name} connected" : "Bluetooth audio connected");
        }

        if (previous is { IsBluetooth: true } gone && next != previous)
        {
            return Alert(gone.Name.Length > 0 ? gone.Name : "Bluetooth audio", "Disconnected", gone.Name.Length > 0 ? $"{gone.Name} disconnected" : "Bluetooth audio disconnected");
        }

        return null;
    }

    /// <summary>Forgets the state (feature turned off); the next reading is a new first reading.</summary>
    public void Reset()
    {
        _known = false;
        Current = null;
    }

    private static IslandAlert Alert(string name, string state, string spoken) => new(
        Key,
        AlertKind.Volume,
        AlertPriority.Normal,
        new AlertContent(Headphones, $"{state} · {name}") { Accessible = spoken, Tone = Theming.Tone.Bluetooth },
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5));
}
