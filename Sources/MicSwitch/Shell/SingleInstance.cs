using System.Security.Cryptography;
using System.Text;

namespace MicSwitch.Shell;

/// <summary>Allows one running copy. A second launch asks the first to show its window, then exits.</summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Name = BuildName();

    private readonly Mutex mutex;
    private readonly EventWaitHandle showSignal;
    private readonly RegisteredWaitHandle registration;

    private SingleInstance(Mutex mutex)
    {
        this.mutex = mutex;
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Show");
        registration = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, false);
    }

    /// <summary>Another launch happened. Raised on a thread-pool thread.</summary>
    public event Action? ShowRequested;

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, Name, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(Name + ".Show", out var signal))
        {
            using (signal)
            {
                signal.Set();
            }
        }
    }

    public void Dispose()
    {
        registration.Unregister(null);
        showSignal.Dispose();
        mutex.Dispose();
    }

    private static string BuildName()
    {
        // A copy pointed at its own data directory (testing) doesn't count as the same instance.
        var scope = Environment.GetEnvironmentVariable("MICSWITCH_DATA_DIR") is { Length: > 0 } custom
            ? "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(custom).ToUpperInvariant())), 0, 8)
            : string.Empty;
        return "MicSwitch.{4E0E0B6F-2C4B-4B7B-9E0C-6D3B1C4B8F21}" + scope;
    }
}
