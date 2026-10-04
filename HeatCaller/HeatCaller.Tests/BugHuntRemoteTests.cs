namespace HeatCaller.Tests;

/// <summary>
/// Pins for the remote-control bug hunt (4 Oct): the club's PIN salt is only
/// handed out once it is on disk (PinKdf.EnsureSalt), and /api/manage picks
/// out race.flag requests so they go through RemoteCommandGate like the flag
/// remote's do (WebServerService.ManageFlagRequest).
/// </summary>
public sealed class BugHuntRemoteTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_salt_that_could_not_be_saved_is_not_handed_out_and_one_is_made_once_it_can_be()
    {
        // A folder where settings.json should be: every save fails, the way
        // a file held by a sync tool or a full disk makes it fail.
        var blocked = _data.PathOf("settings.json");
        Directory.CreateDirectory(blocked);

        var settings = new AppSettings();

        Assert.Equal(string.Empty, PinKdf.EnsureSalt(settings));
        Assert.Equal(string.Empty, settings.RelayPinSalt);

        Directory.Delete(blocked);

        var salt = PinKdf.EnsureSalt(settings);

        Assert.True(PinKdf.IsSalt(salt));
        Assert.Equal(salt, AppSettings.Load().RelayPinSalt);
    }

    [Fact]
    public void A_manage_flag_request_is_picked_out_for_the_gate_with_its_flag_and_id()
    {
        var (isFlag, name, id) = WebServerService.ManageFlagRequest(
            "{\"id\":\"7\",\"op\":\" race.flag \",\"args\":{\"flag\":\"red\"}}");

        Assert.True(isFlag);
        Assert.Equal("RED", name);
        Assert.Equal("7", id);
    }

    [Fact]
    public void A_manage_flag_request_with_no_readable_flag_is_still_counted()
    {
        var (isFlag, name, _) = WebServerService.ManageFlagRequest("{\"op\":\"race.flag\",\"args\":{\"flag\":42}}");

        Assert.True(isFlag);
        Assert.Equal("a flag", name);
    }

    [Theory]
    [InlineData("{\"op\":\"results.list\",\"args\":{}}")]
    [InlineData("{\"op\":\"race.startNext\"}")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Other_manage_requests_are_left_to_the_race_day_service(string body)
    {
        Assert.False(WebServerService.ManageFlagRequest(body).IsFlag);
    }
}
