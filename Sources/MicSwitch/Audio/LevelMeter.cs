using System.Runtime.InteropServices;
using MicSwitch.Infrastructure;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicSwitch.Audio;

/// <summary>
/// Measures the input level of one microphone by recording from it and throwing the audio away.
/// Only runs while a target is set, because an open recording lights up the Windows "microphone in use" indicator.
/// </summary>
internal sealed class LevelMeter : IDisposable
{
    private readonly IWorkQueue queue;
    private readonly Func<string, MMDevice?> openDevice;

    private string? targetId;
    private MMDevice? device;
    private WasapiCapture? capture;
    private float peak;

    public LevelMeter(IWorkQueue queue, Func<string, MMDevice?> openDevice)
    {
        this.queue = queue;
        this.openDevice = openDevice;
    }

    /// <summary>The loudest sample since the last call, 0..1.</summary>
    public float ReadPeak() => Interlocked.Exchange(ref peak, 0f);

    /// <summary>Starts metering the device with this ID. Null stops metering.</summary>
    public void SetTarget(string? deviceId) => queue.Post(() =>
    {
        if (string.Equals(deviceId, targetId, StringComparison.OrdinalIgnoreCase) && (capture != null || deviceId == null))
        {
            return;
        }

        Stop();
        targetId = deviceId;
        if (deviceId != null)
        {
            Start(deviceId);
        }
    });

    public void Dispose() => queue.Post(() =>
    {
        targetId = null;
        Stop();
    });

    private void Start(string deviceId)
    {
        try
        {
            device = openDevice(deviceId);
            if (device == null)
            {
                return;
            }

            var started = new WasapiCapture(device);
            started.DataAvailable += OnDataAvailable;
            started.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null)
                {
                    Log.Debug(e.Exception, "Level meter stopped");
                    queue.Post(() =>
                    {
                        if (ReferenceEquals(capture, started))
                        {
                            Stop();
                        }
                    });
                }
            };
            capture = started;
            started.StartRecording();
        }
        catch (Exception ex)
        {
            // E.g. microphone access is switched off in Windows privacy settings.
            Log.Information(ex, "Level meter is unavailable for {Id}", deviceId);
            Stop();
        }
    }

    private void Stop()
    {
        var oldCapture = capture;
        var oldDevice = device;
        capture = null;
        device = null;
        Interlocked.Exchange(ref peak, 0f);
        try
        {
            if (oldCapture != null)
            {
                oldCapture.DataAvailable -= OnDataAvailable;
                oldCapture.Dispose();
            }

            oldDevice?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error while stopping the level meter");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiCapture source || e.BytesRecorded == 0)
        {
            return;
        }

        var format = source.WaveFormat;
        var bytes = e.Buffer.AsSpan(0, e.BytesRecorded);
        var max = 0f;
        if (format.BitsPerSample == 32 && IsFloat(format))
        {
            foreach (var sample in MemoryMarshal.Cast<byte, float>(bytes))
            {
                max = Math.Max(max, Math.Abs(sample));
            }
        }
        else if (format.BitsPerSample == 16)
        {
            foreach (var sample in MemoryMarshal.Cast<byte, short>(bytes))
            {
                max = Math.Max(max, Math.Abs(sample / 32768f));
            }
        }
        else if (format.BitsPerSample == 32)
        {
            foreach (var sample in MemoryMarshal.Cast<byte, int>(bytes))
            {
                max = Math.Max(max, Math.Abs(sample / 2147483648f));
            }
        }
        else
        {
            return;
        }

        max = Math.Min(max, 1f);
        float current;
        do
        {
            current = Volatile.Read(ref peak);
        }
        while (max > current && Interlocked.CompareExchange(ref peak, max, current) != current);
    }

    private static bool IsFloat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat ||
        format is WaveFormatExtensible { SubFormat: var subFormat } && subFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
}
