namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Local Screens page's Remote PIN tile.
///
/// The tile read "Set" for any PIN of four or more characters, so "race1" or
/// "12 34" showed Set in the normal colour while the remote refused every
/// command from the phone.
/// </summary>
public sealed class BugHunt24_WebServerControlTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("123456")]
    [InlineData(" 4321 ")]
    public void RemotePinTile_SaysSetForAPinTheRemoteTakes(string pin)
    {
        var (text, ok) = WebServerControl.RemotePinTile(pin);

        Assert.Equal("Set", text);
        Assert.True(ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RemotePinTile_SaysNotSetWithNoPin(string? pin)
    {
        var (text, ok) = WebServerControl.RemotePinTile(pin);

        Assert.Equal("Not set", text);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("race1", "Digits only")]
    [InlineData("12 34", "Digits only")]
    [InlineData("12a4", "Digits only")]
    [InlineData("123", "Too short")]
    public void RemotePinTile_DoesNotSaySetForAPinTheRemoteRefuses(string pin, string expected)
    {
        var (text, ok) = WebServerControl.RemotePinTile(pin);

        Assert.Equal(expected, text);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("race1")]
    [InlineData("12 34")]
    [InlineData("123")]
    [InlineData("")]
    [InlineData("0000000")]
    public void RemotePinTile_AgreesWithTheServersRule(string pin)
    {
        var (_, ok) = WebServerControl.RemotePinTile(pin);

        Assert.Equal(WebServerService.IsUsableRemotePin(pin.Trim()), ok);
    }
}
