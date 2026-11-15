using Microsoft.Win32;

namespace MicSwitch.Shell;

/// <summary>Starts the app when the user signs in, through the per-user "Run" registry key.</summary>
internal sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppInfo.Name;

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read the startup setting");
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not change the startup setting");
            return false;
        }
    }
}
