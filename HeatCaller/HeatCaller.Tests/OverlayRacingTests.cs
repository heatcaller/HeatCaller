using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The stream overlays made for racers (14 Nov 2026, plans/obs-overlay-plan.md
/// sections 5 and 6): laps/time, the finish, car colours, last laps, the next
/// races, sponsor logos on the stream and OBS scenes.
/// </summary>
[Collection(SponsorStreamCollection.Name)]
public sealed class OverlayRacingTests
{
    [Theory]
    [InlineData(23, 302_341, 0, "23/5:02.341")]
    [InlineData(1, 9_840, 0, "1/0:09.840")]
    [InlineData(99, 1_805_120, 0, "99/30:05.120")]
    [InlineData(120, 3_725_120, 0, "120/1:02:05.120")]
    [InlineData(12, 120_000, 2, "12*/2:00.000")]
    [InlineData(0, 5_000, 0, "-")]
    [InlineData(5, 0, 0, "-")]
    public void A_result_reads_the_rc_way(int laps, long elapsedMs, int byHand, string expected) =>
        Assert.Equal(expected, KpiCatalog.ResultText(laps, elapsedMs, byHand));

    [Fact]
    public void Result_is_a_column_a_club_can_pick()
    {
        var result = KpiCatalog.All.Single(k => k.Key == "result");

        Assert.Equal("Result", result.Label);
    }

    [Theory]
    [InlineData("#e53935", "#E53935")]
    [InlineData(" #1E88E5 ", "#1E88E5")]
    [InlineData("red", "")]
    [InlineData("#12345", "")]
    [InlineData("#GGGGGG", "")]
    [InlineData("#123456;background:url(x)", "")]
    [InlineData(null, "")]
    public void A_car_colour_is_only_ever_a_plain_hex_colour(string? typed, string expected) =>
        Assert.Equal(expected, CarColour.Clean(typed));

    [Fact]
    public void A_colour_round_trips_through_the_picker()
    {
        Assert.Equal("#0A0B0C", CarColour.FromColour(Color.FromArgb(10, 11, 12)));
        Assert.Equal(Color.FromArgb(10, 11, 12).ToArgb(), CarColour.ToColour("#0a0b0c")!.Value.ToArgb());
        Assert.Null(CarColour.ToColour(""));
    }

    [Fact]
    public void A_profile_saved_before_car_colours_has_none()
    {
        var old = JsonSerializer.Deserialize<DriverProfile>("{\"Transponder\":\"1\",\"DriverName\":\"Mia\"}")!;

        Assert.Equal(string.Empty, old.BodyColour);
    }

    private static readonly SponsorStream.Logo Hut = new("s1", "Hobby Hut", "/sponsor-logo/sponsor-logo-111111111111.png", 2);
    private static readonly SponsorStream.Logo Volt = new("s2", "Volt & Co.", "/sponsor-logo/sponsor-logo-222222222222.png", 1);

    [Fact]
    public void Sponsors_take_turns_in_proportion_to_their_share()
    {
        var start = new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);
        var turns = Enumerable.Range(0, 30)
            .Select(i => SponsorStream.Up(new[] { Hut, Volt }, start.AddSeconds(i * SponsorStream.TurnSeconds), null)!.Id)
            .ToList();

        Assert.Equal(20, turns.Count(t => t == "s1"));
        Assert.Equal(10, turns.Count(t => t == "s2"));
        // Spread out rather than back to back: never three of one in a row.
        Assert.DoesNotContain(Enumerable.Range(0, 28), i => turns[i] == turns[i + 1] && turns[i + 1] == turns[i + 2]);
    }

    [Fact]
    public void A_sponsor_whose_read_is_on_air_is_the_one_up()
    {
        var now = new DateTime(2026, 11, 14, 19, 0, 7, DateTimeKind.Utc);

        Assert.Equal("s2", SponsorStream.Up(new[] { Hut, Volt }, now, " volt & co. ")!.Id);
        Assert.Null(SponsorStream.Up(Array.Empty<SponsorStream.Logo>(), now, null));
    }

    [Fact]
    public void Time_on_screen_is_counted_only_while_a_page_says_it_is_showing()
    {
        SponsorStream.ResetForTests();
        var at = new DateTime(2026, 11, 14, 20, 0, 0, DateTimeKind.Local).ToUniversalTime();

        SponsorStream.Shown("s1", at);
        for (var i = 1; i <= 10; i++)
        {
            SponsorStream.Shown("s1", at.AddSeconds(i));
        }

        // A second source polling in between adds only the moment between
        // the two asks, never another whole second of its own.
        SponsorStream.Shown("s1", at.AddSeconds(10.3));

        // A page that stopped for a minute: the gap is not logo time.
        SponsorStream.Shown("s1", at.AddSeconds(70));

        var night = TonightAtTheTrack.NightOf(at.ToLocalTime());
        Assert.Equal(10, SponsorStream.SecondsOn(night)["s1"]);
        Assert.Empty(SponsorStream.SecondsOn(night.AddDays(-1)));
    }

    [Fact]
    public void The_report_says_how_long_their_logo_was_on_the_stream()
    {
        var hut = new SponsorBooking { Id = "s1", Name = "Hobby Hut" };

        Assert.Equal("  Their logo was on the stream for about 41 minutes.",
            SponsorReport.StreamLine(hut, new Dictionary<string, int> { ["s1"] = 41 * 60 + 10 }));
        Assert.Equal("  Their logo was on the stream for under a minute.",
            SponsorReport.StreamLine(hut, new Dictionary<string, int> { ["s1"] = 20 }));
        Assert.Equal(string.Empty, SponsorReport.StreamLine(hut, new Dictionary<string, int>()));
        Assert.Equal(string.Empty, SponsorReport.StreamLine(hut, null));
    }

    [Theory]
    [InlineData(RaceState.Staging, "Grid")]
    [InlineData(RaceState.Countdown, "Grid")]
    [InlineData(RaceState.Racing, "Race cam")]
    [InlineData(RaceState.Finished, "")]
    [InlineData(RaceState.Idle, "Intermission")]
    public void Each_part_of_the_night_has_its_scene_and_a_blank_one_leaves_obs_alone(RaceState state, string expected)
    {
        var settings = new AppSettings
        {
            ObsSceneStaging = "Grid",
            ObsSceneRacing = " Race cam ",
            ObsSceneFinished = "",
            ObsSceneBetween = "Intermission",
        };

        Assert.Equal(expected, ObsScenes.For(settings, state));
    }

    [Fact]
    public void Obs_saying_it_has_no_such_scene_is_read_and_anything_else_is_not()
    {
        using var failed = JsonDocument.Parse("{\"op\":7,\"d\":{\"requestType\":\"SetCurrentProgramScene\",\"requestId\":\"hc-scene-3\",\"requestStatus\":{\"result\":false,\"code\":600,\"comment\":\"No source was found by the name of `Race cam`.\"}}}");
        using var worked = JsonDocument.Parse("{\"op\":7,\"d\":{\"requestId\":\"hc-scene-4\",\"requestStatus\":{\"result\":true,\"code\":100}}}");
        using var other = JsonDocument.Parse("{\"op\":7,\"d\":{\"requestId\":\"hc-status\",\"requestStatus\":{\"result\":false}}}");

        Assert.Equal("No source was found by the name of `Race cam`.", ObsLink.SceneProblem(failed.RootElement));
        Assert.Null(ObsLink.SceneProblem(worked.RootElement));
        Assert.Null(ObsLink.SceneProblem(other.RootElement));
    }

    [Fact]
    public void Scene_switching_is_off_until_a_club_turns_it_on()
    {
        Assert.False(new AppSettings().ObsSwitchScenes);
    }

    [Fact]
    public void A_junior_is_a_first_name_and_initial_on_the_stream_by_default()
    {
        var map = JuniorNames.ForStream(new[] { "Mia Kent", "Sam Trent", "Sam Tran", "Ollie", "" }, null);

        Assert.Equal("Mia K", map["Mia Kent"]);
        // Two who would both be "Sam T" are told apart.
        Assert.NotEqual(map["Sam Trent"], map["Sam Tran"]);
        Assert.StartsWith("Sam Tr", map["Sam Trent"]);
        // A one-word name has nothing to take off.
        Assert.False(map.ContainsKey("Ollie"));
    }

    [Fact]
    public void A_club_can_choose_first_names_only_or_full_names()
    {
        Assert.Equal("Mia", JuniorNames.ForStream(new[] { "Mia Kent" }, JuniorNames.First)["Mia Kent"]);
        Assert.Empty(JuniorNames.ForStream(new[] { "Mia Kent" }, JuniorNames.Full));
        Assert.Equal(JuniorNames.Initial, JuniorNames.Rule("anything else"));
        Assert.Equal(JuniorNames.Initial, new AppSettings().JuniorNamesOnStream);
    }

    [Fact]
    public void Every_name_the_stream_draws_goes_through_the_junior_rule()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId));

        Assert.Contains("el.children[3].textContent = sn(s.name);", page);
        Assert.Contains("snText(m.line || '')", page);
        Assert.Contains("el.textContent = snText(newest.t);", page);
        Assert.Contains("esc(sn(close.a.name))", page);
        Assert.DoesNotContain("esc(s.name)", page);

        var table = Server().BuildOverlayPage();
        Assert.Contains("esc(snText(c.text))", table);
        Assert.Contains("el.textContent = snText(newest.t);", table);
    }

    [Fact]
    public void A_junior_on_the_board_is_sent_with_the_name_the_stream_uses()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "7001", DriverName = "Mia Kentworth", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "7002", DriverName = "Terry Byteman" });

        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        Cross(race, "7001", 10_000);
        Cross(race, "7002", 10_500);

        var server = new WebServerService(new RaceTiming(race), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var names = JsonDocument.Parse(server.BuildStateJson()).RootElement.GetProperty("streamNames");

        Assert.Equal("Mia K", names.GetProperty("Mia Kentworth").GetString());
        Assert.False(names.TryGetProperty("Terry Byteman", out _));
    }

    [Theory]
    [InlineData("On the Box", "First podium")]
    [InlineData("First Timer", "First race")]
    [InlineData("Winner", "First win")]
    [InlineData("New Heights", "Best lap ever")]
    [InlineData("Spotless", "A clean race")]
    [InlineData("Wire to Wire", null)]
    [InlineData("not a badge", null)]
    public void The_firsts_a_family_cheers_are_said_in_plain_words(string badge, string? plain) =>
        Assert.Equal(plain, MilestoneWords.PlainTitle(badge));

    [Fact]
    public void The_stream_says_1st_2nd_and_3rd_and_the_basics_between_races()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn("between"));

        Assert.Contains("'<div class=\"line\"><b>' + ordinal(i + 1) + '</b>'", page);
        Assert.Contains("[\"New to RC racing?\",\"Races run for a set time.", page);
        Assert.Contains("const basics = want('basics') && PART === 'between' && !over && minutesToGo >= 2;", page);
        Assert.Contains("That\\'s it for tonight", page);
        Assert.Contains("'Starting soon'", page);
    }

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = car, DeviceTimeMs = raceTimeMs, SignalHits = 1, SignalStrength = 100, IsSynthetic = true });

    private static WebServerService Server() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    [Fact]
    public void The_stream_page_draws_results_colours_last_laps_and_the_next_races()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId));

        Assert.Contains("if ((c.done || finished) && c.result) g = c.result;", page);
        Assert.Contains("d.finishing ? 'FINAL LAP'", page);
        Assert.Contains("/^#[0-9A-F]{6}$/.test(pc.col || '')", page);
        Assert.Contains("const modes = ['interval', 'leader', 'last'];", page);
        Assert.Contains("Array.isArray(d.upNext)", page);
        Assert.Contains("Marshals: ", page);
    }

    [Fact]
    public void The_sponsor_graphic_stays_out_of_the_way_of_the_finish()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn("corner"));

        Assert.Contains("const busy = racing && (d.finishing || lastMinute(d.countdown) || battleUp);", page);
        Assert.Contains("if (sponsorShowing) said.push('sponsor=1');", page);
        // Only an address the laptop makes is ever put in an img.
        Assert.Contains("const LOGO_URL = /^\\/sponsor-logo\\/sponsor-logo-[0-9a-f]{12}\\.png$/;", page);
        Assert.Contains("const m = /(\\d+):(\\d{2})/.exec(t);", page);
    }

    [Fact]
    public void The_live_state_carries_the_finish_the_next_races_and_the_sponsor_up()
    {
        var doc = JsonDocument.Parse(Server().BuildStateJson()).RootElement;

        Assert.True(doc.TryGetProperty("finishing", out _));
        Assert.Equal(JsonValueKind.Array, doc.GetProperty("upNext").ValueKind);
        Assert.True(doc.TryGetProperty("sponsorUp", out _));
        Assert.True(doc.TryGetProperty("presentedLogos", out _));
    }
}

/// <summary>SponsorStream's count is one for the whole app: these tests take turns with it.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SponsorStreamCollection
{
    public const string Name = "SponsorStream count";
}
