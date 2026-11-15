using System.Collections.ObjectModel;
using MicSwitch.Audio;

namespace MicSwitch.UI.ViewModels;

/// <summary>A choice in a drop-down.</summary>
internal sealed record Option<T>(T Value, string Label);

internal sealed record DeviceOption(DeviceSelection Selection, string Label);

internal static class DeviceOptions
{
    /// <summary>
    /// Fills a device drop-down: the Windows default, optionally "all", then every active device.
    /// A saved device that isn't there right now stays in the list, marked as disconnected.
    /// Returns the entry for the current selection.
    /// </summary>
    public static DeviceOption? Sync(
        ObservableCollection<DeviceOption> target,
        IReadOnlyList<EndpointInfo> available,
        DeviceSelection selection,
        string? allLabel)
    {
        var options = new List<DeviceOption> { new(DeviceSelection.Default, "Windows default") };
        if (allLabel != null)
        {
            options.Add(new DeviceOption(DeviceSelection.All, allLabel));
        }

        options.AddRange(available.Select(x => new DeviceOption(DeviceSelection.Specific(x.Id, x.Name), x.Name)));

        var selected = options.FirstOrDefault(x => Matches(x.Selection, selection));
        if (selected == null && selection.Kind == DeviceSelectionKind.Specific)
        {
            selected = new DeviceOption(selection, $"{selection.Name} (disconnected)");
            options.Add(selected);
        }

        if (!target.SequenceEqual(options))
        {
            target.Clear();
            foreach (var option in options)
            {
                target.Add(option);
            }
        }

        return target.FirstOrDefault(x => x == selected);
    }

    private static bool Matches(DeviceSelection option, DeviceSelection selection)
    {
        if (option.Kind != selection.Kind)
        {
            return false;
        }

        if (option.Kind != DeviceSelectionKind.Specific)
        {
            return true;
        }

        // A selection imported from MicSwitch 1.x may carry only a name.
        return selection.Id == null
            ? string.Equals(option.Name, selection.Name, StringComparison.OrdinalIgnoreCase)
            : string.Equals(option.Id, selection.Id, StringComparison.OrdinalIgnoreCase);
    }
}
