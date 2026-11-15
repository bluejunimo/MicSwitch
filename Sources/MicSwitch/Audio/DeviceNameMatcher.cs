using System.Text.RegularExpressions;

namespace MicSwitch.Audio;

/// <summary>
/// Finds a device again after Windows gave it a new endpoint ID (new USB port, driver or Windows update).
/// Windows also tends to renumber the name, e.g. "Microphone (4- HyperX SoloCast)" becomes
/// "Microphone (5- HyperX SoloCast)" or loses the number, so names are compared without it.
/// </summary>
internal static partial class DeviceNameMatcher
{
    // "(4- " as in "Microphone (4- HyperX SoloCast)". The space matters: "(3-in-1 Dock)" is a real name.
    [GeneratedRegex(@"\(\s*\d+-\s+")]
    private static partial Regex InstanceNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var withoutNumber = InstanceNumber().Replace(name, "(");
        return Whitespace().Replace(withoutNumber, " ").Trim();
    }

    /// <summary>
    /// Returns the one device whose name matches <paramref name="name"/>: an exact match first,
    /// otherwise a match ignoring the instance number. Returns null when nothing matches or when
    /// several devices match equally well (picking one would be a guess).
    /// </summary>
    public static EndpointInfo? FindMatch(string? name, IReadOnlyList<EndpointInfo> candidates)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var exact = candidates.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0)
        {
            return exact.Count == 1 ? exact[0] : null;
        }

        var normalized = Normalize(name);
        var loose = candidates.Where(x => string.Equals(Normalize(x.Name), normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        return loose.Count == 1 ? loose[0] : null;
    }
}
