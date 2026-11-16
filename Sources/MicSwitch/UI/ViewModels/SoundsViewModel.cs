using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MicSwitch.Audio;
using MicSwitch.Settings;
using MicSwitch.Shell;
using Wpf.Ui.Controls;

namespace MicSwitch.UI.ViewModels;

/// <summary>The mute and unmute sounds of the microphone. <see cref="SpeakerSoundsViewModel"/> is the same for the speakers.</summary>
internal partial class SoundsViewModel : ObservableObject
{
    private static readonly SoundOption None = new(string.Empty, "None");

    // Sits at the end of each sound list; choosing it opens a file dialog instead of selecting it.
    private static readonly SoundOption AddAction = new("\0add", "Add sound file…", IsAddAction: true);

    private readonly DeviceProfile profile;
    private readonly AppState state;
    private readonly AudioEngine engine;
    private readonly SettingsStore settings;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool syncing;

    [ObservableProperty]
    private SoundOption? muteSound;

    [ObservableProperty]
    private SoundOption? unmuteSound;

    [ObservableProperty]
    private double volume;

    [ObservableProperty]
    private DeviceOption? selectedOutput;

    public SoundsViewModel(AppState state, AudioEngine engine, SettingsStore settings)
        : this(DeviceProfile.Microphone, state, engine, settings)
    {
    }

    protected SoundsViewModel(DeviceProfile profile, AppState state, AudioEngine engine, SettingsStore settings)
    {
        this.profile = profile;
        this.state = state;
        this.engine = engine;
        this.settings = settings;
        state.SettingsChanged += Sync;
        state.SpeakersChanged += Sync;

        // The microphone and the speakers share one set of sounds, each with its own page.
        engine.Sounds.Changed += () => dispatcher.BeginInvoke(Sync);
        Sync();
    }

    public ObservableCollection<SoundOption> Sounds { get; } = [];

    public ObservableCollection<DeviceOption> Outputs { get; } = [];

    public SymbolRegular MutedSymbol => profile.IsMicrophone ? SymbolRegular.MicOff24 : SymbolRegular.SpeakerMute24;

    public SymbolRegular LiveSymbol => profile.IsMicrophone ? SymbolRegular.Mic24 : SymbolRegular.Speaker224;

    private NotificationPlayer Player => engine.PlayerFor(profile.Flow);

    private void Sync()
    {
        var current = profile.Sounds(state.Settings);
        syncing = true;
        try
        {
            var options = new List<SoundOption> { None };
            options.AddRange(engine.Sounds.List().Select(x => new SoundOption(x.Id, x.Id, CanRemove: !x.IsBuiltIn)));
            foreach (var id in new[] { current.MuteSound, current.UnmuteSound })
            {
                if (Find(options, id) == null)
                {
                    options.Add(new SoundOption(id, $"{id} (missing)"));
                }
            }

            options.Add(AddAction);
            if (!Sounds.SequenceEqual(options))
            {
                Sounds.Clear();
                options.ForEach(Sounds.Add);
            }

            MuteSound = Find(Sounds, current.MuteSound);
            UnmuteSound = Find(Sounds, current.UnmuteSound);
            Volume = Math.Round(current.Volume * 100);
            SelectedOutput = DeviceOptions.Sync(Outputs, state.Speakers.Available, current.OutputDevice, null, state.Speakers.DefaultName);
        }
        finally
        {
            syncing = false;
        }
    }

    private static SoundOption? Find(IEnumerable<SoundOption> options, string? id) =>
        options.FirstOrDefault(x => !x.IsAddAction && string.Equals(x.Value, id ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void PlayMuteSound() => Player.Play(MuteSound?.Value, (float)(Volume / 100));

    [RelayCommand]
    private void PlayUnmuteSound() => Player.Play(UnmuteSound?.Value, (float)(Volume / 100));

    /// <summary>Adds a sound file and uses it for the list it was added from.</summary>
    private void AddSound(bool forMute)
    {
        var dialog = new OpenFileDialog { Title = "Add sound", Filter = SoundLibrary.FileFilter };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var id = engine.Sounds.AddFromFile(dialog.FileName);
                settings.Update(s =>
                {
                    if (forMute)
                    {
                        profile.Sounds(s).MuteSound = id;
                    }
                    else
                    {
                        profile.Sounds(s).UnmuteSound = id;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not add sound {Path}", dialog.FileName);
            }
        }

        // Also puts the selection back when nothing was added.
        Sync();
    }

    // Takes any object: while a list is being refilled the button's data is briefly not a sound.
    [RelayCommand]
    private void RemoveSound(object? parameter)
    {
        if (parameter is not SoundOption { CanRemove: true } option)
        {
            return;
        }

        try
        {
            engine.Sounds.Remove(option.Value);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not remove sound {Id}", option.Value);
        }

        // Wherever it was in use: a built-in sound of the same name takes over, otherwise the default does.
        if (!engine.Sounds.List().Any(x => Same(x.Id, option.Value)))
        {
            settings.Update(s =>
            {
                foreach (var (sounds, fallback) in new[] { (s.Sounds, new SoundSettings()), (s.Speakers.Sounds, SoundSettings.Silent) })
                {
                    if (Same(sounds.MuteSound, option.Value))
                    {
                        sounds.MuteSound = fallback.MuteSound;
                    }

                    if (Same(sounds.UnmuteSound, option.Value))
                    {
                        sounds.UnmuteSound = fallback.UnmuteSound;
                    }
                }
            });
        }

        Sync();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    partial void OnMuteSoundChanged(SoundOption? value) => OnSoundChosen(value, forMute: true);

    partial void OnUnmuteSoundChanged(SoundOption? value) => OnSoundChosen(value, forMute: false);

    private void OnSoundChosen(SoundOption? value, bool forMute)
    {
        if (syncing || value == null)
        {
            return;
        }

        if (value.IsAddAction)
        {
            // After the drop-down has closed.
            dispatcher.BeginInvoke(() => AddSound(forMute));
            return;
        }

        settings.Update(s =>
        {
            if (forMute)
            {
                profile.Sounds(s).MuteSound = value.Value;
            }
            else
            {
                profile.Sounds(s).UnmuteSound = value.Value;
            }
        });
    }

    partial void OnVolumeChanged(double value)
    {
        if (!syncing)
        {
            settings.Update(s => profile.Sounds(s).Volume = (float)(value / 100));
        }
    }

    partial void OnSelectedOutputChanged(DeviceOption? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => profile.Sounds(s).OutputDevice = value.Selection);
        }
    }
}

internal sealed class SpeakerSoundsViewModel(AppState state, AudioEngine engine, SettingsStore settings)
    : SoundsViewModel(DeviceProfile.Speakers, state, engine, settings);
