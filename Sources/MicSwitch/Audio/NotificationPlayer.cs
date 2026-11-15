using MicSwitch.Infrastructure;

namespace MicSwitch.Audio;

/// <summary>An open playback stream. Playing a sound replaces whatever is playing.</summary>
internal interface ISoundOutput : IDisposable
{
    void Play(CachedSound sound, float volume);

    /// <summary>The stream died (device unplugged, format change). Raised on an arbitrary thread.</summary>
    event Action? Faulted;
}

/// <summary>
/// Plays mute/unmute sounds. The newest sound always wins: it cuts off the one before it, so a burst
/// of key presses never queues up. The output stream stays open for a few seconds after a sound so
/// the next one starts instantly, then closes so it doesn't keep the audio device awake.
/// </summary>
internal sealed class NotificationPlayer : IDisposable
{
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);

    private readonly IWorkQueue queue;
    private readonly Func<string?, CachedSound?> loadSound;
    private readonly Func<DeviceSelection, ISoundOutput?> openOutput;

    private DeviceSelection device = DeviceSelection.Default;
    private ISoundOutput? output;
    private IDisposable? idleTimer;
    private bool disposed;

    public NotificationPlayer(
        IWorkQueue queue,
        Func<string?, CachedSound?> loadSound,
        Func<DeviceSelection, ISoundOutput?> openOutput)
    {
        this.queue = queue;
        this.loadSound = loadSound;
        this.openOutput = openOutput;
    }

    public void SetDevice(DeviceSelection newDevice) => queue.Post(() =>
    {
        if (newDevice != device)
        {
            device = newDevice;
            CloseOutput();
        }
    });

    public void Play(string? soundId, float volume) => queue.Post(() => PlayCore(soundId, volume));

    /// <summary>Drops the open stream so the next sound picks its device afresh (devices changed).</summary>
    public void InvalidateOutput() => queue.Post(CloseOutput);

    public void Dispose() => queue.Post(() =>
    {
        disposed = true;
        CloseOutput();
    });

    private void PlayCore(string? soundId, float volume)
    {
        if (disposed || volume <= 0 || loadSound(soundId) is not { } sound)
        {
            return;
        }

        // A second attempt covers a stream that went stale without telling us.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                output ??= Open();
                if (output == null)
                {
                    return;
                }

                output.Play(sound, Math.Min(volume, 1f));
                Log.Debug("Playing {Sound}", soundId);
                idleTimer?.Dispose();
                idleTimer = queue.Schedule(sound.Duration + IdleTimeout, CloseOutput);
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not play {Sound} (attempt {Attempt})", soundId, attempt + 1);
                CloseOutput();
            }
        }
    }

    private ISoundOutput? Open()
    {
        var opened = openOutput(device);
        if (opened != null)
        {
            Log.Debug("Sound output opened");
            opened.Faulted += () => queue.Post(() =>
            {
                if (ReferenceEquals(output, opened))
                {
                    CloseOutput();
                }
            });
        }

        return opened;
    }

    private void CloseOutput()
    {
        idleTimer?.Dispose();
        idleTimer = null;
        var old = output;
        output = null;
        if (old == null)
        {
            return;
        }

        try
        {
            old.Dispose();
            Log.Debug("Sound output closed");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error while closing the sound output");
        }
    }
}
