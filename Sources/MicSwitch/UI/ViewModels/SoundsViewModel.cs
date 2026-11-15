using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MicSwitch.Audio;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

internal sealed partial class SoundsViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly AudioEngine engine;
    private readonly SettingsStore settings;
    private bool syncing;

    [ObservableProperty]
    private Option<string>? muteSound;

    [ObservableProperty]
    private Option<string>? unmuteSound;

    [ObservableProperty]
    private double volume;

    [ObservableProperty]
    private DeviceOption? selectedOutput;

    public SoundsViewModel(AppState state, AudioEngine engine, SettingsStore settings)
    {
        this.state = state;
        this.engine = engine;
        this.settings = settings;
        state.SettingsChanged += Sync;
        state.SpeakersChanged += Sync;
        Sync();
    }

    public ObservableCollection<Option<string>> Sounds { get; } = [];

    public ObservableCollection<DeviceOption> Outputs { get; } = [];

    private void Sync()
    {
        var current = state.Settings.Sounds;
        syncing = true;
        try
        {
            var options = new List<Option<string>> { new(string.Empty, "None") };
            options.AddRange(engine.Sounds.List().Select(x => new Option<string>(x.Id, x.Id)));
            foreach (var id in new[] { current.MuteSound, current.UnmuteSound })
            {
                if (Find(options, id) == null)
                {
                    options.Add(new Option<string>(id, $"{id} (missing)"));
                }
            }

            if (!Sounds.SequenceEqual(options))
            {
                Sounds.Clear();
                options.ForEach(Sounds.Add);
            }

            MuteSound = Find(Sounds, current.MuteSound);
            UnmuteSound = Find(Sounds, current.UnmuteSound);
            Volume = Math.Round(current.Volume * 100);
            SelectedOutput = DeviceOptions.Sync(Outputs, state.Speakers.Available, current.OutputDevice, null);
        }
        finally
        {
            syncing = false;
        }
    }

    private static Option<string>? Find(IEnumerable<Option<string>> options, string? id) =>
        options.FirstOrDefault(x => string.Equals(x.Value, id ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void PlayMuteSound() => engine.Player.Play(MuteSound?.Value, (float)(Volume / 100));

    [RelayCommand]
    private void PlayUnmuteSound() => engine.Player.Play(UnmuteSound?.Value, (float)(Volume / 100));

    [RelayCommand]
    private void AddSound()
    {
        var dialog = new OpenFileDialog { Title = "Add sound", Filter = SoundLibrary.FileFilter };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            engine.Sounds.AddFromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not add sound {Path}", dialog.FileName);
        }

        Sync();
    }

    partial void OnMuteSoundChanged(Option<string>? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => s.Sounds.MuteSound = value.Value);
        }
    }

    partial void OnUnmuteSoundChanged(Option<string>? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => s.Sounds.UnmuteSound = value.Value);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Sounds.Volume = (float)(value / 100));
        }
    }

    partial void OnSelectedOutputChanged(DeviceOption? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => s.Sounds.OutputDevice = value.Selection);
        }
    }
}
