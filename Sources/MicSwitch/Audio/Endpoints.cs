namespace MicSwitch.Audio;

internal enum AudioFlow
{
    Capture,
    Render
}

internal sealed record EndpointInfo(string Id, string Name);

/// <summary>An open audio device. Any member may throw once the device has gone away.</summary>
internal interface IAudioEndpoint : IDisposable
{
    EndpointInfo Info { get; }

    bool Mute { get; set; }

    /// <summary>0..1</summary>
    float Volume { get; set; }

    /// <summary>Mute or volume changed, by us or by anyone else. Raised on an arbitrary thread.</summary>
    event Action? Changed;
}

/// <summary>Access to the system's audio devices. Calls may throw while the audio service is unavailable.</summary>
internal interface IEndpointSource
{
    IReadOnlyList<EndpointInfo> GetActiveEndpoints(AudioFlow flow);

    /// <summary>IDs of the device(s) Windows treats as default for <paramref name="flow"/>.</summary>
    IReadOnlyList<string> GetDefaultEndpointIds(AudioFlow flow);

    IAudioEndpoint? Open(string id);

    /// <summary>Drops and recreates the connection to the audio service.</summary>
    void Reset();

    /// <summary>A device was added, removed, changed state, or the default changed. Raised on an arbitrary thread.</summary>
    event Action? DevicesChanged;
}

/// <summary>What a <see cref="DeviceController"/> currently controls.</summary>
internal sealed class DeviceState : IEquatable<DeviceState>
{
    public static DeviceState Empty { get; } = new(DeviceSelection.Default, [], [], null, null);

    public DeviceState(
        DeviceSelection selection,
        IReadOnlyList<EndpointInfo> available,
        IReadOnlyList<EndpointInfo> bound,
        bool? mute,
        float? volume,
        string? defaultName = null)
    {
        Selection = selection;
        Available = available;
        Bound = bound;
        Mute = mute;
        Volume = volume;
        DefaultName = defaultName;
    }

    public DeviceSelection Selection { get; }

    /// <summary>All active devices, for pickers.</summary>
    public IReadOnlyList<EndpointInfo> Available { get; }

    /// <summary>The device Windows currently uses by default, whatever the selection is. Null when there is none.</summary>
    public string? DefaultName { get; }

    /// <summary>The devices the selection resolved to. Empty means disconnected.</summary>
    public IReadOnlyList<EndpointInfo> Bound { get; }

    public bool IsConnected => Bound.Count > 0;

    /// <summary>True when every bound device is muted. Null while disconnected.</summary>
    public bool? Mute { get; }

    /// <summary>Average volume of the bound devices, 0..1. Null while disconnected.</summary>
    public float? Volume { get; }

    public bool Equals(DeviceState? other) =>
        other is not null &&
        Selection == other.Selection &&
        Mute == other.Mute &&
        Volume == other.Volume &&
        DefaultName == other.DefaultName &&
        Available.SequenceEqual(other.Available) &&
        Bound.SequenceEqual(other.Bound);

    public override bool Equals(object? obj) => Equals(obj as DeviceState);

    public override int GetHashCode() => HashCode.Combine(Selection, Mute, Volume, Available.Count, Bound.Count);
}
