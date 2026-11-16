using System.Collections.Concurrent;
using Microsoft.VisualBasic.FileIO;
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
    private readonly Action<string> deleteFile;
    private readonly ConcurrentDictionary<string, CachedSound?> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="deleteFile">How a removed sound is deleted; by default it goes to the Recycle Bin.</param>
    public SoundLibrary(string builtInDirectory, string userDirectory, Action<string>? deleteFile = null)
    {
        this.builtInDirectory = builtInDirectory;
        this.userDirectory = userDirectory;
        this.deleteFile = deleteFile ??
                          (path => FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
    }

    /// <summary>A sound was added or removed. Raised on the thread that did it.</summary>
    public event Action? Changed;

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
                if (IsSound(file))
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
        Changed?.Invoke();
        return id;
    }

    /// <summary>
    /// Deletes a sound the user added. A built-in sound of the same name, if any, takes its place again. Returns false when there is no such user sound.
    /// </summary>
    public bool Remove(string id)
    {
        var info = List().FirstOrDefault(x => !x.IsBuiltIn && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (info == null)
        {
            return false;
        }

        deleteFile(info.Path);
        cache.TryRemove(id, out _);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Deletes the user's files that are exact copies of a built-in sound, and returns which built-in sound
    /// stands in for each (old ID to built-in ID). MicSwitch 1.x kept its stock sounds in the user's
    /// folder, so after an upgrade they would show up twice, once of them removable.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReplaceCopiesOfBuiltIns()
    {
        var replaced = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(builtInDirectory) || !Directory.Exists(userDirectory))
        {
            return replaced;
        }

        var builtIns = Directory.EnumerateFiles(builtInDirectory)
            .Where(IsSound)
            .Select(path => (Path: path, Length: new FileInfo(path).Length))
            .ToList();
        foreach (var file in Directory.EnumerateFiles(userDirectory).Where(IsSound).ToList())
        {
            try
            {
                var length = new FileInfo(file).Length;
                foreach (var builtIn in builtIns.Where(x => x.Length == length))
                {
                    if (File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(builtIn.Path)))
                    {
                        File.Delete(file);
                        replaced[Path.GetFileNameWithoutExtension(file)] = Path.GetFileNameWithoutExtension(builtIn.Path);
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not check sound {Path}", file);
            }
        }

        if (replaced.Count > 0)
        {
            cache.Clear();
            Changed?.Invoke();
        }

        return replaced;
    }

    private static bool IsSound(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

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
