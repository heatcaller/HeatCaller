namespace HeatCaller.Tests;

/// <summary>
/// Driver experience P0 (22 Oct 2026): the call-up records carry Staged and
/// the ETA across, and a staged race is worded as one. Also the contract
/// pieces later packages build on: DriverAsk and the new defaults.
/// </summary>
public sealed class DriverExp_P0_PhoneFollowStagedTests
{
    [Fact]
    public void Staged_and_the_eta_are_copied_to_each_call_up()
    {
        var eta = new DateTime(2026, 10, 22, 19, 42, 0, DateTimeKind.Utc);
        var upcoming = new[]
        {
            new UpcomingRace("Q2 Heat 1", new[] { "A" }, OnTrack: true, RoundId: "r1", Staged: true),
            new UpcomingRace("Q2 Heat 2", new[] { "B" }, OnTrack: false, RoundId: "r2", EtaUtc: eta),
        };

        var callUps = PhoneFollow.CallUps(upcoming);

        Assert.Equal(new CallUp("Q2 Heat 1", 0, 1, true, Staged: true), callUps["A"]);
        Assert.Equal(new CallUp("Q2 Heat 2", 0, 1, false, EtaUtc: eta), callUps["B"]);
    }

    [Fact]
    public void Older_callers_get_what_they_always_did()
    {
        var race = new UpcomingRace("Heat", new[] { "A" }, false);

        Assert.Null(race.RoundId);
        Assert.False(race.Staged);
        Assert.Null(race.EtaUtc);

        var callUp = new CallUp("Heat", 1, 2, false);
        Assert.False(callUp.Staged);
        Assert.Null(callUp.EtaUtc);
        Assert.Equal("1 race before yours", PhoneFollow.Words(callUp));
    }

    [Fact]
    public void A_staged_race_says_where_to_go()
    {
        Assert.Equal("Staged - go to the driver stand", PhoneFollow.Words(new CallUp("Heat 1", 0, 2, true, Staged: true)));
        Assert.Equal(PhoneFollow.StagedWords, PhoneFollow.Words(new CallUp("Heat 1", 0, 2, false, Staged: true)));
        Assert.Equal("On track now", PhoneFollow.Words(new CallUp("Heat 1", 0, 2, true)));
    }

    [Theory]
    [InlineData("a1b2c3d4e5f60718293a4b5c", "A1B2C3")]
    [InlineData("  ffffff  ", "FFFFFF")]
    [InlineData("abc", "")]
    [InlineData("zzzzzzzz", "")]
    [InlineData(null, "")]
    public void An_asks_reference_is_its_first_six_hex_in_capitals(string? id, string expected)
    {
        Assert.Equal(expected, DriverAsk.RefOf(id));
    }

    [Fact]
    public void Ask_kinds_and_states_are_spelled_as_on_the_wire()
    {
        Assert.True(DriverAsk.IsKind("details"));
        Assert.True(DriverAsk.IsKind("pack"));
        Assert.True(DriverAsk.IsKind("query"));
        Assert.False(DriverAsk.IsKind("Details"));

        foreach (var state in new[] { "waiting", "approved", "noted", "declined" })
        {
            Assert.True(DriverAsk.IsState(state));
        }

        Assert.False(DriverAsk.IsState("Replaced"));
        Assert.Equal(new DriverAskAnswer("A1B2C3", "pack", "approved", ""), new DriverAskAnswer("A1B2C3", "pack", "approved", ""));
    }

    [Fact]
    public void Every_new_setting_and_profile_flag_starts_off()
    {
        var settings = new AppSettings();

        Assert.False(settings.RelayDriverRequests);
        Assert.False(settings.RelayCaptions);
        Assert.False(settings.BigScreenCaptions);
        Assert.False(settings.NightEmailOffered);
        Assert.False(settings.NightEmailAutomatic);
        Assert.Equal(string.Empty, settings.NightEmailSentFor);
        Assert.Empty(settings.NightEmailSentTo);
        Assert.Empty(settings.PrivacyRepublishOwed);

        var profile = new DriverProfile { Transponder = "T1" };
        Assert.False(profile.NoPhotoOnline);
        Assert.False(profile.NoVideoOnline);
        Assert.False(profile.InitialsOnline);
        Assert.False(profile.EmailMyNight);
        Assert.Equal(string.Empty, profile.AboutLine);
        Assert.Equal(120, DriverProfile.AboutLineMaxChars);

        Assert.False(new SessionResultEntry().JumpStarted);
    }

    [Fact]
    public void A_profile_snapshot_keeps_the_new_fields_so_undo_does()
    {
        var profile = new DriverProfile
        {
            Transponder = "T1",
            NoPhotoOnline = true,
            NoVideoOnline = true,
            InitialsOnline = true,
            EmailMyNight = true,
            AboutLine = "Racing since I was eight",
        };

        var copy = ProfileStore.Snapshot(profile);

        Assert.True(copy.NoPhotoOnline);
        Assert.True(copy.NoVideoOnline);
        Assert.True(copy.InitialsOnline);
        Assert.True(copy.EmailMyNight);
        Assert.Equal("Racing since I was eight", copy.AboutLine);
    }
}
