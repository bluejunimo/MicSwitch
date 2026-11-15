using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicSwitch.Audio;

internal sealed record SoundInfo(string Id, string Path, bool IsBuiltIn);

/// <summary>A fully decoded sound: interleaved float samples in <see cref="SoundLibrary.Format"/>.</summary>
internal sealed class CachedSound(float[] samples)
{
    public float[] Samples { get; } = samples;

    public TimeSpan Duration { get; } =
        TimeSpan.FromSeconds((double)samples.Length / (SoundLibrary.Format.SampleRate * SoundLibrary.Format.Channels));
}

/// <summary>
/// Notification sounds: the built-in ones next to the executable plus files the user added.
/// A sound's ID is its file name without extension.
/// </summary>
internal sealed class SoundLibrary
{
    public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private static readonly string[] Extensions = [".wav", ".mp3", ".flac", ".m4a", ".wma", ".aiff"];
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);

    private readonly string builtInDirectory;
    private readonly string userDirectory;
    private readonly ConcurrentDictionary<string, CachedSound?> cache = new(StringComparer.OrdinalIgnoreCase);

    public SoundLibrary(string builtInDirectory, string userDirectory)
    {
        this.builtInDirectory = builtInDirectory;
        this.userDirectory = userDirectory;
    }

    public static string FileFilter { get; } =
        "Sound files|" + string.Join(";", Extensions.Select(x => "*" + x)) + "|All files|*.*";

    public IReadOnlyList<SoundInfo> List()
    {
        var result = new Dictionary<string, SoundInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (directory, builtIn) in new[] { (builtInDirectory, true), (userDirectory, false) })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    // A user file replaces a built-in sound of the same name.
                    result[Path.GetFileNameWithoutExtension(file)] = new SoundInfo(Path.GetFileNameWithoutExtension(file), file, builtIn);
                }
            }
        }

        return result.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Returns the decoded sound, or null when there is no such sound or it can't be read.</summary>
    public CachedSound? Load(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return cache.GetOrAdd(id, key =>
        {
            var info = List().FirstOrDefault(x => string.Equals(x.Id, key, StringComparison.OrdinalIgnoreCase));
            if (info == null)
            {
                Log.Warning("Sound {Id} not found", key);
                return null;
            }

            try
            {
                return Decode(info.Path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read sound {Path}", info.Path);
                return null;
            }
        });
    }

    /// <summary>Copies a file into the user's sounds and returns its ID.</summary>
    public string AddFromFile(string sourcePath)
    {
        Directory.CreateDirectory(userDirectory);
        var id = Path.GetFileNameWithoutExtension(sourcePath);
        var target = Path.Combine(userDirectory, Path.GetFileName(sourcePath));
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, target, overwrite: true);
        }

        cache.TryRemove(id, out _);
        return id;
    }

    private static CachedSound Decode(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        source = source.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(source),
            2 => source,
            _ => new MultiplexingSampleProvider([source], 2)
        };

        if (source.WaveFormat.SampleRate != Format.SampleRate)
        {
            source = new WdlResamplingSampleProvider(source, Format.SampleRate);
        }

        var maxSamples = (int)(MaxDuration.TotalSeconds * Format.SampleRate * Format.Channels);
        var samples = new List<float>();
        var buffer = new float[Format.SampleRate * Format.Channels / 4];
        int read;
        while (samples.Count < maxSamples && (read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return new CachedSound(samples.ToArray());
    }
}
