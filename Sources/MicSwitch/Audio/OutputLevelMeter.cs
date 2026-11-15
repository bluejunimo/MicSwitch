using MicSwitch.Infrastructure;
using NAudio.CoreAudioApi;

namespace MicSwitch.Audio;

/// <summary>Something that reports how loud a device is right now.</summary>
internal interface ILevelMeter
{
    /// <summary>The loudest moment since the last call, 0..1.</summary>
    float ReadPeak();

    /// <summary>Starts metering the device with this ID. Null stops metering.</summary>
    void SetTarget(string? deviceId);
}

/// <summary>
/// Measures what is playing on one output device. Windows keeps a peak meter for every output, so this
/// only reads it; nothing is recorded.
/// </summary>
internal sealed class OutputLevelMeter : ILevelMeter, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(30);

    private readonly IWorkQueue queue;
    private readonly Func<string, MMDevice?> openDevice;

    private string? targetId;
    private MMDevice? device;
    private AudioMeterInformation? meter;
    private IDisposable? timer;
    private float peak;

    public OutputLevelMeter(IWorkQueue queue, Func<string, MMDevice?> openDevice)
    {
        this.queue = queue;
        this.openDevice = openDevice;
    }

    public float ReadPeak() => Interlocked.Exchange(ref peak, 0f);

    public void SetTarget(string? deviceId) => queue.Post(() =>
    {
        if (string.Equals(deviceId, targetId, StringComparison.OrdinalIgnoreCase) && (meter != null || deviceId == null))
        {
            return;
        }

        Stop();
        targetId = deviceId;
        if (deviceId == null)
        {
            return;
        }

        try
        {
            device = openDevice(deviceId);
            meter = device?.AudioMeterInformation;
            if (meter != null)
            {
                Poll();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Output level is unavailable for {Id}", deviceId);
            Stop();
        }
    });

    public void Dispose() => queue.Post(() =>
    {
        targetId = null;
        Stop();
    });

    private void Poll()
    {
        if (meter == null)
        {
            return;
        }

        try
        {
            var value = Math.Clamp(meter.MasterPeakValue, 0f, 1f);
            if (value > Volatile.Read(ref peak))
            {
                Volatile.Write(ref peak, value);
            }

            timer = queue.Schedule(Interval, Poll);
        }
        catch (Exception ex)
        {
            // The device went away; the owner sets a new target when it learns of that.
            Log.Debug(ex, "Output level meter stopped");
            Stop();
        }
    }

    private void Stop()
    {
        timer?.Dispose();
        timer = null;
        var oldDevice = device;
        device = null;
        meter = null;
        Interlocked.Exchange(ref peak, 0f);
        try
        {
            oldDevice?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error while stopping the output level meter");
        }
    }
}
