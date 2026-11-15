using MicSwitch.Audio;

namespace MicSwitch.Tests;

public class DeviceNameMatcherTests
{
    [Theory]
    [InlineData("Microphone (4- HyperX SoloCast)", "Microphone (HyperX SoloCast)")]
    [InlineData("Microphone (12-  HyperX SoloCast)", "Microphone (HyperX SoloCast)")]
    [InlineData("Microphone (HyperX SoloCast)", "Microphone (HyperX SoloCast)")]
    [InlineData("  Speakers  (2- Xonar U7 MKII) ", "Speakers (Xonar U7 MKII)")]
    [InlineData("Line (3-in-1 Dock)", "Line (3-in-1 Dock)")]
    [InlineData(null, "")]
    public void Normalize_removes_the_instance_number(string? name, string expected) =>
        Assert.Equal(expected, DeviceNameMatcher.Normalize(name));

    [Fact]
    public void A_renumbered_device_is_found()
    {
        EndpointInfo[] devices =
        [
            new("webcam", "Microphone (HD Webcam)"),
            new("new-id", "Microphone (5- HyperX SoloCast)")
        ];

        Assert.Equal("new-id", DeviceNameMatcher.FindMatch("Microphone (4- HyperX SoloCast)", devices)?.Id);
    }

    [Fact]
    public void An_exact_name_wins_over_a_renumbered_one()
    {
        EndpointInfo[] devices =
        [
            new("other", "Microphone (5- HyperX SoloCast)"),
            new("exact", "Microphone (4- HyperX SoloCast)")
        ];

        Assert.Equal("exact", DeviceNameMatcher.FindMatch("Microphone (4- HyperX SoloCast)", devices)?.Id);
    }

    [Fact]
    public void Two_equally_good_matches_are_not_guessed_between()
    {
        EndpointInfo[] devices =
        [
            new("a", "Microphone (2- HyperX SoloCast)"),
            new("b", "Microphone (3- HyperX SoloCast)")
        ];

        Assert.Null(DeviceNameMatcher.FindMatch("Microphone (4- HyperX SoloCast)", devices));
    }

    [Fact]
    public void A_different_device_is_not_a_match()
    {
        EndpointInfo[] devices = [new("webcam", "Microphone (HD Webcam)")];

        Assert.Null(DeviceNameMatcher.FindMatch("Microphone (4- HyperX SoloCast)", devices));
        Assert.Null(DeviceNameMatcher.FindMatch(null, devices));
    }
}
