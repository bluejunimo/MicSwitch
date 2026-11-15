using MicSwitch.Audio;
using MicSwitch.Infrastructure;

namespace MicSwitch.Tests;

/// <summary>
/// A work queue driven by the test. Posted actions run in order, never nested, like on the real audio
/// thread; scheduled actions run when the test advances the clock.
/// </summary>
internal sealed class TestQueue : IWorkQueue
{
    private readonly Queue<Action> pending = new();
    private readonly List<Timer> timers = [];
    private TimeSpan now;
    private bool draining;

    public void Post(Action action)
    {
        pending.Enqueue(action);
        if (draining)
        {
            return;
        }

        draining = true;
        try
        {
            while (pending.Count > 0)
            {
                pending.Dequeue()();
            }
        }
        finally
        {
            draining = false;
        }
    }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new Timer(this, now + delay, action);
        timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan time)
    {
        var target = now + time;
        while (timers.Where(x => x.Due <= target).OrderBy(x => x.Due).FirstOrDefault() is { } next)
        {
            timers.Remove(next);
            now = next.Due;
            Post(next.Action);
        }

        now = target;
    }

    private sealed class Timer(TestQueue owner, TimeSpan due, Action action) : IDisposable
    {
        public TimeSpan Due { get; } = due;

        public Action Action { get; } = action;

        public void Dispose() => owner.timers.Remove(this);
    }
}

internal sealed class FakeEndpoint(string id, string name) : IAudioEndpoint
{
    private bool mute;
    private float volume = 0.5f;

    public EndpointInfo Info { get; } = new(id, name);

    public bool Gone { get; set; }

    public bool Disposed { get; private set; }

    public event Action? Changed;

    public bool Mute
    {
        get => Gone ? throw new InvalidOperationException("device gone") : mute;
        set
        {
            if (Gone)
            {
                throw new InvalidOperationException("device gone");
            }

            mute = value;
        }
    }

    public float Volume
    {
        get => Gone ? throw new InvalidOperationException("device gone") : volume;
        set
        {
            if (Gone)
            {
                throw new InvalidOperationException("device gone");
            }

            volume = value;
        }
    }

    /// <summary>Simulates another program changing the device.</summary>
    public void ChangeExternally(bool newMute)
    {
        mute = newMute;
        Changed?.Invoke();
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeEndpointSource : IEndpointSource
{
    private readonly List<FakeEndpoint> plugged = [];

    public event Action? DevicesChanged;

    public List<string> DefaultIds { get; } = [];

    public bool Broken { get; set; }

    public int Resets { get; private set; }

    /// <summary>Every endpoint handed out by <see cref="Open"/>, newest last.</summary>
    public List<FakeEndpoint> Opened { get; } = [];

    public FakeEndpoint Plug(string id, string name, bool muted = false)
    {
        var endpoint = new FakeEndpoint(id, name) { Mute = muted };
        plugged.Add(endpoint);
        DevicesChanged?.Invoke();
        return endpoint;
    }

    public void Unplug(string id)
    {
        plugged.RemoveAll(x => x.Info.Id == id);
        DevicesChanged?.Invoke();
    }

    public IReadOnlyList<EndpointInfo> GetActiveEndpoints(AudioFlow flow) =>
        Broken ? throw new InvalidOperationException("audio service unavailable") : plugged.Select(x => x.Info).ToList();

    public IReadOnlyList<string> GetDefaultEndpointIds(AudioFlow flow) => DefaultIds;

    public IAudioEndpoint? Open(string id)
    {
        var endpoint = plugged.FirstOrDefault(x => x.Info.Id == id);
        if (endpoint != null)
        {
            Opened.Add(endpoint);
        }

        return endpoint;
    }

    public void Reset() => Resets++;
}

internal sealed class FakeSoundOutput : ISoundOutput
{
    public List<(CachedSound Sound, float Volume)> Played { get; } = [];

    public bool Disposed { get; private set; }

    public bool FailNextPlay { get; set; }

    public event Action? Faulted;

    public void Play(CachedSound sound, float volume)
    {
        if (FailNextPlay)
        {
            FailNextPlay = false;
            throw new InvalidOperationException("stream is stale");
        }

        Played.Add((sound, volume));
    }

    public void Fault() => Faulted?.Invoke();

    public void Dispose() => Disposed = true;
}
