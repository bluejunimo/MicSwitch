using MicSwitch.Audio;
using MicSwitch.Hotkeys;
using MicSwitch.Services;
using MicSwitch.Settings;

namespace MicSwitch.Tests;

public class NotificationPlayerTests
{
    private static readonly CachedSound Beep = new(new float[SoundLibrary.Format.SampleRate * SoundLibrary.Format.Channels]); // one second

    private readonly TestQueue queue = new();
    private readonly List<FakeSoundOutput> outputs = [];
    private readonly List<DeviceSelection> openedOn = [];
    private readonly NotificationPlayer player;

    public NotificationPlayerTests()
    {
        player = new NotificationPlayer(
            queue,
            id => id == "beep" ? Beep : null,
            device =>
            {
                openedOn.Add(device);
                outputs.Add(new FakeSoundOutput());
                return outputs[^1];
            });
    }

    [Fact]
    public void A_burst_of_sounds_reuses_one_stream_and_never_queues()
    {
        for (var i = 0; i < 100; i++)
        {
            player.Play("beep", 0.5f);
        }

        // Every request went straight to the open stream, which replaces whatever was playing.
        var output = Assert.Single(outputs);
        Assert.Equal(100, output.Played.Count);
        Assert.False(output.Disposed);
    }

    [Fact]
    public void The_stream_closes_once_the_last_sound_has_been_quiet_for_a_while()
    {
        player.Play("beep", 1);
        queue.Advance(TimeSpan.FromSeconds(3));
        player.Play("beep", 1);

        // Five seconds after the first sound, but not yet after the second.
        queue.Advance(TimeSpan.FromSeconds(4));
        Assert.False(outputs[0].Disposed);

        queue.Advance(Beep.Duration + NotificationPlayer.IdleTimeout);
        Assert.True(outputs[0].Disposed);

        player.Play("beep", 1);
        Assert.Equal(2, outputs.Count);
    }

    [Fact]
    public void Silence_and_unknown_sounds_open_nothing()
    {
        player.Play(null, 1);
        player.Play("", 1);
        player.Play("missing", 1);
        player.Play("beep", 0);

        Assert.Empty(outputs);
    }

    [Fact]
    public void A_stale_or_failed_stream_is_replaced()
    {
        player.Play("beep", 1);
        outputs[0].FailNextPlay = true;

        player.Play("beep", 1);
        Assert.True(outputs[0].Disposed);
        Assert.Single(outputs[1].Played);

        outputs[1].Fault();
        Assert.True(outputs[1].Disposed);

        player.Play("beep", 1);
        Assert.Single(outputs[2].Played);
    }

    [Fact]
    public void Changing_the_output_device_reopens_the_stream()
    {
        player.Play("beep", 1);
        var headset = DeviceSelection.Specific("headset", "Headset");

        player.SetDevice(headset);
        Assert.True(outputs[0].Disposed);

        player.Play("beep", 1);
        Assert.Equal([DeviceSelection.Default, headset], openedOn);

        player.InvalidateOutput();
        Assert.True(outputs[1].Disposed);
    }
}

public class MuteServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MicSwitch.Tests", Guid.NewGuid().ToString("N"));
    private readonly TestQueue queue = new();
    private readonly FakeEndpointSource source = new();
    private readonly FakeSoundOutput output = new();
    private readonly List<string?> requestedSounds = [];
    private SettingsStore? settings;
    private DeviceController? controller;

    public void Dispose()
    {
        settings?.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private MuteService Start(Action<AppSettings> configure, DeviceProfile? profile = null)
    {
        var initial = new AppSettings();
        initial.Microphone.Device = DeviceSelection.Specific("solo", "Mic");
        initial.Sounds.MuteSound = "muted";
        initial.Sounds.UnmuteSound = "unmuted";
        configure(initial);

        settings = new SettingsStore(Path.Combine(directory, "settings.json"), initial);
        profile ??= DeviceProfile.Microphone;
        controller = new DeviceController(source, queue, profile.Flow);
        var player = new NotificationPlayer(
            queue,
            id =>
            {
                requestedSounds.Add(id);
                return new CachedSound(new float[2]);
            },
            _ => output);
        var service = new MuteService(profile, controller, player, settings, queue);
        service.Start();
        return service;
    }

    private void Press(MuteService service, MuteAction action, bool isDown) => queue.Post(() => service.OnHotkey(action, isDown));

    [Fact]
    public void Push_to_mute_unmutes_at_startup_without_a_sound_then_follows_the_key()
    {
        var mic = source.Plug("solo", "Mic", muted: true);
        var service = Start(s => s.Microphone.Mode = MuteMode.PushToMute);

        Assert.False(mic.Mute);
        Assert.Empty(requestedSounds);

        Press(service, MuteAction.Main, true);
        Assert.True(mic.Mute);
        Press(service, MuteAction.Main, false);
        Assert.False(mic.Mute);

        Assert.Equal(["muted", "unmuted"], requestedSounds);
    }

    [Fact]
    public void Push_to_talk_starts_muted_and_unmutes_only_while_held()
    {
        var mic = source.Plug("solo", "Mic");
        var service = Start(s => s.Microphone.Mode = MuteMode.PushToTalk);
        Assert.True(mic.Mute);

        Press(service, MuteAction.Main, true);
        Assert.False(mic.Mute);
        Press(service, MuteAction.Main, false);
        Assert.True(mic.Mute);
    }

    [Fact]
    public void Toggle_mode_flips_on_press_only_and_respects_the_startup_state()
    {
        var mic = source.Plug("solo", "Mic");
        var service = Start(s => s.Microphone.StartupState = StartupMicState.Mute);
        Assert.True(mic.Mute);
        Assert.Empty(requestedSounds);

        Press(service, MuteAction.Main, true);
        Press(service, MuteAction.Main, false);
        Assert.False(mic.Mute);

        Press(service, MuteAction.Main, true);
        Assert.True(mic.Mute);
        Assert.Equal(["unmuted", "muted"], requestedSounds);
    }

    [Fact]
    public void Toggle_mode_leaves_the_microphone_alone_at_startup_by_default()
    {
        var mic = source.Plug("solo", "Mic", muted: true);
        Start(_ => { });

        Assert.True(mic.Mute);
        Assert.Empty(requestedSounds);
    }

    [Fact]
    public void The_startup_state_waits_for_a_microphone_that_shows_up_late()
    {
        Start(s => s.Microphone.Mode = MuteMode.PushToTalk);

        var mic = source.Plug("solo", "Mic");
        queue.Advance(TimeSpan.FromSeconds(1));

        Assert.True(mic.Mute);
        Assert.Empty(requestedSounds);
    }

    [Fact]
    public void A_mute_from_another_program_plays_the_sound_but_a_reconnect_does_not()
    {
        var mic = source.Plug("solo", "Mic");
        Start(_ => { });

        mic.ChangeExternally(true);
        Assert.Equal(["muted"], requestedSounds);

        source.Unplug("solo");
        queue.Advance(TimeSpan.FromSeconds(1));
        source.Plug("solo", "Mic");
        queue.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["muted"], requestedSounds);
    }

    [Fact]
    public void A_hundred_quick_presses_play_a_hundred_sounds_on_one_stream()
    {
        source.Plug("solo", "Mic");
        var service = Start(s => s.Microphone.Mode = MuteMode.PushToMute);

        for (var i = 0; i < 50; i++)
        {
            Press(service, MuteAction.Main, true);
            Press(service, MuteAction.Main, false);
        }

        Assert.Equal(100, output.Played.Count);
        Assert.Equal("unmuted", requestedSounds[^1]);
    }

    [Fact]
    public void Extra_hotkeys_do_what_they_say()
    {
        var mic = source.Plug("solo", "Mic");
        var service = Start(_ => { });

        Press(service, MuteAction.Mute, true);
        Press(service, MuteAction.Mute, false);
        Assert.True(mic.Mute);

        Press(service, MuteAction.Unmute, true);
        Assert.False(mic.Mute);

        Press(service, MuteAction.PushToMute, true);
        Assert.True(mic.Mute);
        Press(service, MuteAction.PushToMute, false);
        Assert.False(mic.Mute);

        Press(service, MuteAction.Toggle, true);
        Assert.True(mic.Mute);
    }

    [Fact]
    public void A_rebound_device_is_saved_and_volume_is_restored_when_asked()
    {
        var mic = source.Plug("solo-new", "Mic");
        mic.Volume = 0.2f;
        Start(s =>
        {
            s.Microphone.RestoreVolume = true;
            s.Microphone.Volume = 0.9f;
        });

        Assert.Equal(0.9f, mic.Volume);
        Assert.Equal(DeviceSelection.Specific("solo-new", "Mic"), settings!.Current.Microphone.Device);

        controller!.SetVolume(0.4f);
        Assert.Equal(0.4f, settings.Current.Microphone.Volume);
    }

    [Fact]
    public void Speakers_follow_their_own_mode_sounds_and_device()
    {
        var speakers = source.Plug("out", "Speakers");
        var service = Start(
            s =>
            {
                s.Speakers.Device = DeviceSelection.Specific("out", "Speakers");
                s.Speakers.Mode = MuteMode.PushToMute;
                s.Speakers.Sounds.MuteSound = "quiet";
                s.Speakers.Sounds.UnmuteSound = "loud";
            },
            DeviceProfile.Speakers);

        Press(service, MuteAction.Main, true);
        Assert.True(speakers.Mute);
        Press(service, MuteAction.Main, false);
        Assert.False(speakers.Mute);

        // Its own sounds, not the microphone's.
        Assert.Equal(["quiet", "loud"], requestedSounds);
    }

    [Fact]
    public void Speakers_make_no_sound_until_one_is_chosen()
    {
        var speakers = source.Plug("out", "Speakers");
        var service = Start(s => s.Speakers.Device = DeviceSelection.Specific("out", "Speakers"), DeviceProfile.Speakers);

        Press(service, MuteAction.Toggle, true);

        Assert.True(speakers.Mute);
        Assert.Empty(requestedSounds);
        Assert.Empty(output.Played);
    }
}
