namespace MicSwitch.Audio;

internal enum DeviceSelectionKind
{
    /// <summary>Follows whatever Windows uses as its default device.</summary>
    WindowsDefault,

    /// <summary>Every active device at once.</summary>
    All,

    /// <summary>One device, remembered by ID and name.</summary>
    Specific
}

/// <summary>Which device(s) the user picked. Saved in settings.</summary>
internal sealed record DeviceSelection(DeviceSelectionKind Kind, string? Id = null, string? Name = null)
{
    public static DeviceSelection Default { get; } = new(DeviceSelectionKind.WindowsDefault);

    public static DeviceSelection All { get; } = new(DeviceSelectionKind.All);

    public static DeviceSelection Specific(string id, string name) => new(DeviceSelectionKind.Specific, id, name);
}
