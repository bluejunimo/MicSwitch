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
        SpeakerPlayer = new NotificationPlayer(Queue, Sounds.Load, selection => WasapiSoundOutput.Open(source, selection));
        Meter = new LevelMeter(Queue, source.OpenDevice);
        OutputMeter = new OutputLevelMeter(Queue, source.OpenDevice);
        source.DevicesChanged += InvalidateOutputs;
    }

    public WorkerThread Queue { get; }

    public SoundLibrary Sounds { get; }

    public DeviceController Microphone { get; }

    public DeviceController Speakers { get; }

    /// <summary>Plays the microphone's mute and unmute sounds.</summary>
    public NotificationPlayer Player { get; }

    /// <summary>Plays the speakers' mute and unmute sounds; they have their own output device and volume.</summary>
    public NotificationPlayer SpeakerPlayer { get; }

    public LevelMeter Meter { get; }

    public OutputLevelMeter OutputMeter { get; }

    public DeviceController Controller(AudioFlow flow) => flow == AudioFlow.Capture ? Microphone : Speakers;

    public NotificationPlayer PlayerFor(AudioFlow flow) => flow == AudioFlow.Capture ? Player : SpeakerPlayer;

    public ILevelMeter MeterFor(AudioFlow flow) => flow == AudioFlow.Capture ? Meter : OutputMeter;

    /// <summary>Rescans devices and reopens streams, e.g. after the computer wakes up.</summary>
    public void Refresh()
    {
        Microphone.RequestRefresh();
        Speakers.RequestRefresh();
        InvalidateOutputs();
    }

    private void InvalidateOutputs()
    {
        Player.InvalidateOutput();
        SpeakerPlayer.InvalidateOutput();
    }

    public void Dispose()
    {
        source.DevicesChanged -= InvalidateOutputs;
        Meter.Dispose();
        OutputMeter.Dispose();
        Player.Dispose();
        SpeakerPlayer.Dispose();
        Microphone.Dispose();
        Speakers.Dispose();
        Queue.InvokeAsync(source.Dispose).Wait(TimeSpan.FromSeconds(2));
        Queue.Dispose();
    }
}
