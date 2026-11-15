using System.Collections.ObjectModel;
using MicSwitch.Audio;
using MicSwitch.UI.ViewModels;

namespace MicSwitch.Tests;

public sealed class SoundLibraryAndOptionsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MicSwitch-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string builtIn;
    private readonly string user;

    public SoundLibraryAndOptionsTests()
    {
        builtIn = Directory.CreateDirectory(Path.Combine(root, "builtin")).FullName;
        user = Directory.CreateDirectory(Path.Combine(root, "user")).FullName;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private SoundLibrary Library() => new(builtIn, user, File.Delete);

    [Fact]
    public void Only_sounds_the_user_added_can_be_removed()
    {
        File.WriteAllBytes(Path.Combine(builtIn, "Mute.wav"), []);
        File.WriteAllBytes(Path.Combine(user, "Airhorn.mp3"), []);
        var library = Library();

        Assert.False(library.Remove("Mute"));
        Assert.True(library.Remove("airhorn"));
        Assert.False(library.Remove("airhorn"));

        Assert.Equal(["Mute"], library.List().Select(x => x.Id));
        Assert.True(File.Exists(Path.Combine(builtIn, "Mute.wav")));
    }

    [Fact]
    public void Removing_a_user_sound_brings_back_the_built_in_one_it_replaced()
    {
        File.WriteAllBytes(Path.Combine(builtIn, "Mute.wav"), []);
        File.WriteAllBytes(Path.Combine(user, "mute.wav"), []);
        var library = Library();
        Assert.False(Assert.Single(library.List()).IsBuiltIn);

        Assert.True(library.Remove("mute"));

        Assert.True(Assert.Single(library.List()).IsBuiltIn);
    }

    [Fact]
    public void Windows_default_entry_names_the_device_it_stands_for()
    {
        var options = new ObservableCollection<DeviceOption>();
        EndpointInfo[] devices = [new("solo", "Microphone (HyperX SoloCast)"), new("cam", "Microphone (HD Webcam)")];

        var selected = DeviceOptions.Sync(options, devices, DeviceSelection.Default, "All microphones", "Microphone (HyperX SoloCast)");
        Assert.Equal("Windows default (Microphone (HyperX SoloCast))", selected!.Label);
        Assert.Equal(4, options.Count);

        // The default moved to another device: the entry is renamed and stays selected.
        selected = DeviceOptions.Sync(options, devices, DeviceSelection.Default, "All microphones", "Microphone (HD Webcam)");
        Assert.Equal("Windows default (Microphone (HD Webcam))", selected!.Label);
        Assert.Same(options[0], selected);

        selected = DeviceOptions.Sync(options, devices, DeviceSelection.Default, "All microphones", null);
        Assert.Equal("Windows default", selected!.Label);
    }

    [Fact]
    public void Exact_copies_of_built_in_sounds_are_dropped_in_favour_of_the_built_in_ones()
    {
        File.WriteAllBytes(Path.Combine(builtIn, "Discord Mute.wav"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(builtIn, "Discord Unmute.wav"), [5, 6, 7, 8]);
        File.WriteAllBytes(Path.Combine(user, "mute.wav"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(user, "unmute.wav"), [5, 6, 7, 9]); // same size, different sound
        File.WriteAllBytes(Path.Combine(user, "Airhorn.mp3"), [1, 2, 3]);
        var library = Library();

        var replaced = library.ReplaceCopiesOfBuiltIns();

        Assert.Equal("Discord Mute", Assert.Single(replaced).Value);
        Assert.Equal("Discord Mute", replaced["MUTE"]);
        Assert.Equal(["Airhorn", "Discord Mute", "Discord Unmute", "unmute"], library.List().Select(x => x.Id));
        Assert.All(library.List().Where(x => x.Id.StartsWith("Discord")), x => Assert.True(x.IsBuiltIn));
        Assert.Empty(library.ReplaceCopiesOfBuiltIns());
    }
}
