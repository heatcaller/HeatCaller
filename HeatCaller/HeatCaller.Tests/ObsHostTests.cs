using System.Net;

namespace HeatCaller.Tests;

/// <summary>
/// 28 Nov 2026 (stream setup plan S2.8, network review N0): OBS on this laptop,
/// typed as 127.0.0.2, was given the Wi-Fi address for its overlay, because
/// only "localhost" and "127.0.0.1" counted as this PC.
/// </summary>
public class ObsHostTests
{
    private static readonly IPAddress[] Own = [IPAddress.Parse("192.168.1.142"), IPAddress.Parse("100.64.100.6"), IPAddress.Parse("fe80::1")];

    private static bool Here(string? host) => ObsHost.IsThisPc(host, "ASHER-LAPTOP", Own);

    [Theory]
    [InlineData("127.0.0.2")] // the 28 Nov case
    [InlineData("127.0.0.1")]
    [InlineData("127.255.0.9")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData(" localhost ")]
    [InlineData("localhost.")]
    public void Any_loopback_address_is_this_pc(string host) => Assert.True(Here(host));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_obs_address_is_this_pc_as_before(string? host) => Assert.True(Here(host));

    [Theory]
    [InlineData("192.168.1.142")] // the laptop's own Wi-Fi address
    [InlineData("100.64.100.6")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.142")]
    [InlineData("asher-laptop")]
    [InlineData("ASHER-LAPTOP.local")]
    public void The_laptops_own_name_and_addresses_are_this_pc(string host) => Assert.True(Here(host));

    [Theory]
    [InlineData("192.168.1.40")] // the streaming PC the guide suggests
    [InlineData("10.0.0.5")]
    [InlineData("obs-pc")]
    [InlineData("obs-pc.local")]
    [InlineData("asher-laptop2")]
    public void Another_pc_is_not_this_pc(string host) => Assert.False(Here(host));

    [Fact]
    public void A_blank_machine_name_never_matches_a_name()
    {
        Assert.False(ObsHost.IsThisPc("obs-pc", string.Empty, Own));
        Assert.False(ObsHost.IsThisPc(".local", string.Empty, Own));
    }
}
