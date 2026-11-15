using System.Reflection;

namespace MicSwitch;

internal static class AppInfo
{
    public const string Name = "MicSwitch";

    /// <summary>
    /// GitHub repository whose releases this copy updates from, e.g. "https://github.com/you/MicSwitch".
    /// The release workflow stamps it into the build (-p:UpdateRepositoryUrl=...), so a release always
    /// updates from the repository that built it. Empty in local builds, which switches update checks off.
    /// </summary>
    public static string RepositoryUrl { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "UpdateRepositoryUrl")?.Value ?? string.Empty;

    public const string UpstreamUrl = "https://github.com/iXab3r/MicSwitch";

    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    private static readonly string RoamingRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>Settings, logs and user sounds. MICSWITCH_DATA_DIR overrides it (used for testing).</summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("MICSWITCH_DATA_DIR") is { Length: > 0 } custom ? custom : RoamingRoot;

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public static string UserSoundsDirectory => Path.Combine(DataDirectory, "Sounds");
    public static string IconsDirectory => Path.Combine(DataDirectory, "Icons");
    public static string BuiltInSoundsDirectory => Path.Combine(AppContext.BaseDirectory, "Resources", "Sounds");

    // Files written by the original (PoeEye-based) MicSwitch 1.x.
    public static string LegacyConfigPath => Path.Combine(RoamingRoot, "release", "config.cfg");
    public static string LegacySoundsDirectory => Path.Combine(RoamingRoot, "Resources", "Notifications");
}
