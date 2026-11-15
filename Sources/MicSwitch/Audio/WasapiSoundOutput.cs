using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicSwitch.Audio;

/// <summary>
/// A shared-mode WASAPI stream fed by a mixer. The stream plays silence between sounds, so starting
/// a sound is just swapping the mixer's input: no device open, no waiting.
/// </summary>
internal sealed class WasapiSoundOutput : ISoundOutput
{
    private const int LatencyMilliseconds = 60;

    private readonly MMDevice device;
    private readonly WasapiOut player;
    private readonly MixingSampleProvider mixer;

    public WasapiSoundOutput(MMDevice device)
    {
        this.device = device;
        try
        {
            mixer = new MixingSampleProvider(SoundLibrary.Format) { ReadFully = true };
            player = new WasapiOut(device, AudioClientShareMode.Shared, true, LatencyMilliseconds);
            player.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null)
                {
                    Log.Warning(e.Exception, "Sound output stopped unexpectedly");
                    Faulted?.Invoke();
                }
            };
            player.Init(mixer);
            player.Play();
        }
        catch
        {
            player?.Dispose();
            device.Dispose();
            throw;
        }
    }

    public event Action? Faulted;

    public void Play(CachedSound sound, float volume)
    {
        mixer.RemoveAllMixerInputs();
        mixer.AddMixerInput(new CachedSoundProvider(sound, volume));
    }

    public void Dispose()
    {
        try
        {
            player.Dispose();
        }
        finally
        {
            device.Dispose();
        }
    }

    /// <summary>Opens the device a selection points at, falling back to the Windows default output.</summary>
    public static ISoundOutput? Open(CoreAudioEndpointSource source, DeviceSelection selection)
    {
        MMDevice? device = null;
        if (selection.Kind == DeviceSelectionKind.Specific)
        {
            if (selection.Id != null)
            {
                device = source.OpenDevice(selection.Id);
            }

            if (device == null && DeviceNameMatcher.FindMatch(selection.Name, source.GetActiveEndpoints(AudioFlow.Render)) is { } match)
            {
                device = source.OpenDevice(match.Id);
            }
        }

        device ??= source.OpenDefaultDevice(AudioFlow.Render);
        return device == null ? null : new WasapiSoundOutput(device);
    }

    private sealed class CachedSoundProvider(CachedSound sound, float volume) : ISampleProvider
    {
        private int position;

        public WaveFormat WaveFormat => SoundLibrary.Format;

        public int Read(float[] buffer, int offset, int count)
        {
            var samples = sound.Samples;
            var available = Math.Min(count, samples.Length - position);
            for (var i = 0; i < available; i++)
            {
                buffer[offset + i] = samples[position + i] * volume;
            }

            position += available;
            return available;
        }
    }
}
