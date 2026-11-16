using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser(), EventWaitHandleRights.FullControl, AccessControlType.Allow));
        showSignal = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, Name + ".Show", out _, security);
        registration = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, false);
    }

    /// <summary>Another launch happened. Raised on a thread-pool thread.</summary>
    public event Action? ShowRequested;

    /// <param name="wait">How long to wait for a running copy to exit (it is handing over to this one).</param>
    public static SingleInstance? TryAcquire(TimeSpan wait = default)
    {
        Mutex mutex;
        bool owned;
        try
        {
            // Named explicitly for this user: by default an ordinary copy could not even see
            // one that runs as administrator, and would start beside it.
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(CurrentUser(), MutexRights.FullControl, AccessControlType.Allow));
            mutex = MutexAcl.Create(true, Name, out owned, security);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (!owned && wait > TimeSpan.Zero)
        {
            try
            {
                owned = mutex.WaitOne(wait);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
        }

        if (owned)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static void SignalExisting()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(Name + ".Show", out var signal))
            {
                using (signal)
                {
                    signal.Set();
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        registration.Unregister(null);
        showSignal.Dispose();
        mutex.Dispose();
    }

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
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
