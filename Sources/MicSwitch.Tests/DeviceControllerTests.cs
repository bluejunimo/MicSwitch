using MicSwitch.Audio;

namespace MicSwitch.Tests;

public class DeviceControllerTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    private readonly TestQueue queue = new();
    private readonly FakeEndpointSource source = new();

    private DeviceController Create(DeviceSelection selection)
    {
        var controller = new DeviceController(source, queue, AudioFlow.Capture);
        controller.Select(selection);
        controller.Start();
        return controller;
    }

    [Fact]
    public void Binds_to_the_saved_device_by_id()
    {
        source.Plug("other", "Microphone (HD Webcam)");
        source.Plug("solo", "Microphone (4- HyperX SoloCast)", muted: true);

        var controller = Create(DeviceSelection.Specific("solo", "Microphone (4- HyperX SoloCast)"));

        Assert.True(controller.State.IsConnected);
        Assert.Equal("solo", Assert.Single(controller.State.Bound).Id);
        Assert.True(controller.State.Mute);
    }

    [Fact]
    public void A_missing_device_stays_selected_and_nothing_else_is_used()
    {
        source.Plug("other", "Microphone (HD Webcam)");
        var saved = DeviceSelection.Specific("solo", "Microphone (4- HyperX SoloCast)");

        var controller = Create(saved);

        Assert.False(controller.State.IsConnected);
        Assert.Null(controller.State.Mute);
        Assert.Equal(saved, controller.State.Selection);
        Assert.Empty(source.Opened);
    }

    [Fact]
    public void Reconnects_when_the_device_comes_back_under_a_new_id_and_number()
    {
        source.Plug("solo", "Microphone (4- HyperX SoloCast)");
        var controller = Create(DeviceSelection.Specific("solo", "Microphone (4- HyperX SoloCast)"));
        DeviceSelection? rebound = null;
        controller.SelectionRebound += x => rebound = x;

        source.Unplug("solo");
        queue.Advance(Settle);
        Assert.False(controller.State.IsConnected);

        source.Plug("solo-new", "Microphone (5- HyperX SoloCast)");
        queue.Advance(Settle);

        Assert.True(controller.State.IsConnected);
        Assert.Equal("solo-new", Assert.Single(controller.State.Bound).Id);
        Assert.Equal(DeviceSelection.Specific("solo-new", "Microphone (5- HyperX SoloCast)"), rebound);
    }

    [Fact]
    public void The_id_wins_over_another_device_with_the_same_name()
    {
        source.Plug("twin", "Microphone (4- HyperX SoloCast)");
        source.Plug("solo", "Microphone (4- HyperX SoloCast)");

        var controller = Create(DeviceSelection.Specific("solo", "Microphone (4- HyperX SoloCast)"));

        Assert.Equal("solo", Assert.Single(controller.State.Bound).Id);
    }

    [Fact]
    public void A_burst_of_device_events_causes_one_rescan()
    {
        var controller = Create(DeviceSelection.All);
        var changes = 0;
        controller.StateChanged += _ => changes++;

        source.Plug("a", "Mic A");
        source.Unplug("a");
        source.Plug("a", "Mic A");
        Assert.Equal(0, changes);

        queue.Advance(Settle);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Windows_default_follows_the_default_device()
    {
        source.Plug("a", "Mic A");
        source.Plug("b", "Mic B");
        source.DefaultIds.Add("a");
        var controller = Create(DeviceSelection.Default);
        Assert.Equal("a", Assert.Single(controller.State.Bound).Id);

        source.DefaultIds[0] = "b";
        controller.RequestRefresh();
        queue.Advance(Settle);

        Assert.Equal("b", Assert.Single(controller.State.Bound).Id);
    }

    [Fact]
    public void All_devices_are_muted_together_and_the_first_toggle_works()
    {
        var a = source.Plug("a", "Mic A");
        var b = source.Plug("b", "Mic B", muted: true);
        var controller = Create(DeviceSelection.All);

        // One live microphone means "not muted".
        Assert.False(controller.State.Mute);

        controller.ToggleMute();
        Assert.True(a.Mute);
        Assert.True(b.Mute);
        Assert.True(controller.State.Mute);
    }

    [Fact]
    public void Selecting_all_devices_does_not_change_them_but_a_later_arrival_takes_the_current_state()
    {
        var a = source.Plug("a", "Mic A");
        var controller = Create(DeviceSelection.All);
        Assert.False(a.Mute);

        controller.SetMute(true);
        var late = source.Plug("late", "Mic Late");
        queue.Advance(Settle);

        Assert.True(late.Mute);
        Assert.Equal(2, controller.State.Bound.Count);
    }

    [Fact]
    public void A_reconnected_single_device_is_left_as_it_is()
    {
        source.Plug("solo", "Mic");
        var controller = Create(DeviceSelection.Specific("solo", "Mic"));
        controller.SetMute(true);

        source.Unplug("solo");
        queue.Advance(Settle);
        var back = source.Plug("solo", "Mic");
        queue.Advance(Settle);

        Assert.False(back.Mute);
        Assert.False(controller.State.Mute);
    }

    [Fact]
    public void Changes_made_by_other_programs_are_reported()
    {
        var mic = source.Plug("solo", "Mic");
        var controller = Create(DeviceSelection.Specific("solo", "Mic"));
        var seen = new List<bool?>();
        controller.StateChanged += x => seen.Add(x.Mute);

        mic.ChangeExternally(true);
        mic.ChangeExternally(false);

        Assert.Equal([true, false], seen);
    }

    [Fact]
    public void A_device_that_stops_responding_is_dropped_and_looked_for_again()
    {
        var mic = source.Plug("solo", "Mic");
        var controller = Create(DeviceSelection.Specific("solo", "Mic"));

        mic.Gone = true;
        controller.SetMute(true);

        Assert.False(controller.State.IsConnected);
        Assert.True(mic.Disposed);

        mic.Gone = false;
        queue.Advance(Settle);
        Assert.True(controller.State.IsConnected);
    }

    [Fact]
    public void Recovers_after_the_audio_service_restarts()
    {
        source.Plug("solo", "Mic");
        var controller = Create(DeviceSelection.Specific("solo", "Mic"));

        source.Broken = true;
        controller.RequestRefresh();
        queue.Advance(TimeSpan.FromSeconds(10));
        Assert.False(controller.State.IsConnected);
        Assert.True(source.Resets > 0);

        source.Broken = false;
        queue.Advance(TimeSpan.FromSeconds(10));
        Assert.True(controller.State.IsConnected);
    }

    [Fact]
    public void Volume_is_clamped_and_applied()
    {
        var mic = source.Plug("solo", "Mic");
        var controller = Create(DeviceSelection.Specific("solo", "Mic"));

        controller.SetVolume(0.8f);
        Assert.Equal(0.8f, mic.Volume);

        controller.AdjustVolume(0.5f);
        Assert.Equal(1f, mic.Volume);
        Assert.Equal(1f, controller.State.Volume);
    }

    [Fact]
    public void Only_the_newest_of_several_waiting_volume_requests_is_applied()
    {
        var mic = source.Plug("solo", "Mic");
        var controller = Create(DeviceSelection.Specific("solo", "Mic"));
        var before = mic.VolumeWrites;

        // Requests that arrive while the audio thread is busy, like a slider being dragged.
        queue.Post(() =>
        {
            controller.SetVolume(0.2f);
            controller.SetVolume(0.3f);
            controller.SetVolume(0.9f);
        });

        Assert.Equal(0.9f, mic.Volume);
        Assert.Equal(1, mic.VolumeWrites - before);

        controller.SetVolume(0.4f);
        Assert.Equal(0.4f, mic.Volume);
    }

    [Fact]
    public void Reports_the_current_Windows_default_and_follows_it_when_it_changes()
    {
        source.Plug("solo", "Microphone (HyperX SoloCast)");
        source.Plug("cam", "Microphone (HD Webcam)");
        source.DefaultIds.Add("solo");

        // The name is reported whatever is selected, so the "Windows default" entry can show it.
        var controller = Create(DeviceSelection.Specific("cam", "Microphone (HD Webcam)"));
        Assert.Equal("Microphone (HyperX SoloCast)", controller.State.DefaultName);

        source.DefaultIds[0] = "cam";
        controller.RequestRefresh();
        queue.Advance(Settle);
        Assert.Equal("Microphone (HD Webcam)", controller.State.DefaultName);

        source.DefaultIds.Clear();
        controller.RequestRefresh();
        queue.Advance(Settle);
        Assert.Null(controller.State.DefaultName);
    }

    [Fact]
    public void The_Windows_default_is_listed_first_even_when_another_default_was_bound_earlier()
    {
        source.Plug("old", "Microphone (Old)");
        source.Plug("new", "Microphone (New)");
        source.DefaultIds.Add("old");
        var controller = Create(DeviceSelection.Default);
        Assert.Equal(["old"], controller.State.Bound.Select(x => x.Id));

        // "new" becomes the default; "old" stays on as the default for calls.
        source.DefaultIds.Clear();
        source.DefaultIds.AddRange(["new", "old"]);
        controller.RequestRefresh();
        queue.Advance(Settle);

        Assert.Equal(["new", "old"], controller.State.Bound.Select(x => x.Id));
        Assert.Equal("Microphone (New)", controller.State.DefaultName);
    }
}
