using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicSwitch.Settings;

/// <summary>
/// Holds the current settings as a snapshot. <see cref="Update"/> swaps in a changed copy,
/// tells listeners, and saves to disk shortly afterwards.
/// </summary>
internal sealed class SettingsStore : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string path;
    private readonly object gate = new();
    private readonly Timer saveTimer;
    private AppSettings current;
    private string currentJson;
    private bool dirty;

    public SettingsStore(string path, AppSettings initial, bool saveNow = false)
    {
        this.path = path;
        current = initial;
        currentJson = Serialize(initial);
        saveTimer = new Timer(_ => Flush());
        if (saveNow)
        {
            dirty = true;
            Flush();
        }
    }

    /// <summary>The current snapshot. Treat it as read-only; change settings with <see cref="Update"/>.</summary>
    public AppSettings Current => Volatile.Read(ref current);

    /// <summary>Raised on the thread that called <see cref="Update"/>.</summary>
    public event Action<AppSettings>? Changed;

    public static AppSettings? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read settings from {Path}", path);
            return null;
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        AppSettings next;
        lock (gate)
        {
            next = Deserialize(currentJson);
            mutate(next);
            var nextJson = Serialize(next);
            if (nextJson == currentJson)
            {
                return;
            }

            current = next;
            currentJson = nextJson;
            dirty = true;
            saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke(next);
    }

    public void Flush()
    {
        lock (gate)
        {
            if (!dirty)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, currentJson);
                File.Move(temp, path, overwrite: true);
                dirty = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not save settings to {Path}", path);
            }
        }
    }

    public void Dispose()
    {
        saveTimer.Dispose();
        Flush();
    }

    internal static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    internal static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
}
