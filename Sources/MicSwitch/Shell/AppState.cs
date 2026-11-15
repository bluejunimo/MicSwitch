using System.Windows.Threading;
using MicSwitch.Audio;
using MicSwitch.Settings;

namespace MicSwitch.Shell;

/// <summary>
/// The latest device state and settings, delivered on the UI thread. Windows and view models listen
/// here instead of to the audio thread directly.
/// </summary>
internal sealed class AppState
{
    private readonly Dispatcher dispatcher;

    public AppState(AudioEngine engine, SettingsStore settings, Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        Microphone = engine.Microphone.State;
        Speakers = engine.Speakers.State;
        Settings = settings.Current;

        engine.Microphone.StateChanged += state => OnUi(() =>
        {
            Microphone = state;
            MicrophoneChanged?.Invoke();
        });
        engine.Speakers.StateChanged += state => OnUi(() =>
        {
            Speakers = state;
            SpeakersChanged?.Invoke();
        });
        settings.Changed += changed => OnUi(() =>
        {
            Settings = changed;
            SettingsChanged?.Invoke();
        });
    }

    public DeviceState Microphone { get; private set; }

    public DeviceState Speakers { get; private set; }

    public AppSettings Settings { get; private set; }

    public event Action? MicrophoneChanged;

    public event Action? SpeakersChanged;

    public event Action? SettingsChanged;

    private void OnUi(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
