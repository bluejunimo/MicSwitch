using MicSwitch.Infrastructure;

namespace MicSwitch.Audio;

/// <summary>Owns the audio thread and everything that lives on it.</summary>
internal sealed class AudioEngine : IDisposable
{
    private readonly CoreAudioEndpointSource source = new();

    public AudioEngine()
    {
        Queue = new WorkerThread("Audio");
        Sounds = new SoundLibrary(AppInfo.BuiltInSoundsDirectory, AppInfo.UserSoundsDirectory);
        Microphone = new DeviceController(source, Queue, AudioFlow.Capture);
        Speakers = new DeviceController(source, Queue, AudioFlow.Render);
        Player = new NotificationPlayer(Queue, Sounds.Load, selection => WasapiSoundOutput.Open(source, selection));
        Meter = new LevelMeter(Queue, source.OpenDevice);
        source.DevicesChanged += Player.InvalidateOutput;
    }

    public WorkerThread Queue { get; }

    public SoundLibrary Sounds { get; }

    public DeviceController Microphone { get; }

    public DeviceController Speakers { get; }

    public NotificationPlayer Player { get; }

    public LevelMeter Meter { get; }

    /// <summary>Rescans devices and reopens streams, e.g. after the computer wakes up.</summary>
    public void Refresh()
    {
        Microphone.RequestRefresh();
        Speakers.RequestRefresh();
        Player.InvalidateOutput();
    }

    public void Dispose()
    {
        source.DevicesChanged -= Player.InvalidateOutput;
        Meter.Dispose();
        Player.Dispose();
        Microphone.Dispose();
        Speakers.Dispose();
        Queue.InvokeAsync(source.Dispose).Wait(TimeSpan.FromSeconds(2));
        Queue.Dispose();
    }
}
