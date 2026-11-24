using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the web server slice: the address shown when
/// a VPN holds the route (#325), "Corrected" on a fresh result with the
/// same race name (#57), the between-races cards after midnight (#324),
/// the overlay pictures refused in their own frames (#58), a junior's full
/// name in the stream's captions (#2) and the between-races times always
/// 24-hour (#326).
/// </summary>
public sealed class BugHunt24_WebServerService_1Tests
{
    private static WebServerService Server(ProfileStore profiles, RaceControlSession? race = null) =>
        new(new RaceTiming(race ?? new RaceControlSession()), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = car, DeviceTimeMs = raceTimeMs, SignalHits = 1, SignalStrength = 100, IsSynthetic = true });

    /// <summary>A server with an adult racing, so the stream's extras (and its name map) are built.</summary>
    private static WebServerService RacingServer(ProfileStore profiles, string adultTx)
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        Cross(race, adultTx, 10_000);

        return Server(profiles, race);
    }

    private static string AllCaptionText(WebServerService server)
    {
        var captions = JsonNode.Parse(server.BuildStateJson())!["captions"]?.AsArray();

        return captions is null
            ? string.Empty
            : string.Join(" ", captions.Select(c => c!["t"]!.GetValue<string>()));
    }

    // --- #325: the address a phone on the venue Wi-Fi can reach --------------------------------

    [Fact]
    public void A_vpns_routed_address_is_not_shown_instead_of_the_venue_one()
    {
        Assert.Equal("192.168.1.20", WebServerService.PickLanAddress(new[] { "192.168.1.20" }, "10.8.0.2"));
        Assert.Equal("192.168.1.20", WebServerService.PickLanAddress(new[] { "192.168.1.20" }, "100.72.3.4"));
        Assert.Equal("192.168.1.20", WebServerService.PickLanAddress(new[] { "192.168.1.20" }, null));
    }

    [Fact]
    public void The_route_still_picks_between_two_venue_addresses()
    {
        Assert.Equal("192.168.56.1", WebServerService.PickLanAddress(new[] { "192.168.1.20", "192.168.56.1" }, "192.168.56.1"));
    }

    // --- #57: "Corrected" on a fresh result -----------------------------------------------------

    [Fact]
    public void A_second_race_with_the_same_name_is_not_shown_as_Corrected()
    {
        var page = Server(ProfileStore.Load()).BuildBroadcastPage(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId));

        var reset = page.IndexOf("if (!finished) podiumSeen = null;", StringComparison.Ordinal);
        var podium = page.IndexOf("if (want('podium') && finished && podiumRows.length)", StringComparison.Ordinal);

        Assert.True(reset >= 0, "the podium record is never forgotten between races");
        Assert.True(podium > reset, "the record must be forgotten before the podium is drawn");
    }

    // --- #324: the between-races cards after midnight -------------------------------------------

    [Fact]
    public void The_between_races_cards_count_the_minutes_from_the_moment_not_the_words()
    {
        var page = Server(ProfileStore.Load()).BuildBroadcastPage(OverlayLayouts.BuiltIn("between"));

        Assert.Contains("Date.parse(up[0].etaUtc || '')", page);
        Assert.DoesNotContain("at.setHours(Number(firstEta[1])", page);
    }

    [Fact]
    public void A_race_due_after_midnight_is_sent_as_its_own_moment()
    {
        var utc = new DateTime(2026, 11, 24, 0, 5, 0, DateTimeKind.Utc);

        var (_, etaUtc) = WebServerService.UpNextWhen(utc);

        Assert.Equal("2026-11-24T00:05:00Z", etaUtc);
        Assert.Equal(utc, DateTime.Parse(etaUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal));
    }

    [Fact]
    public void No_estimate_is_no_words_and_no_moment()
    {
        Assert.Equal((string.Empty, string.Empty), WebServerService.UpNextWhen(null));
    }

    // --- #58: the overlay pictures in their own frames ------------------------------------------

    [Theory]
    [InlineData("/overlay")]
    [InlineData("/overlay/")]
    [InlineData("/overlay/broadcast")]
    [InlineData("/OVERLAY/Between")]
    public void The_overlay_pictures_may_be_framed_by_this_server(string path)
    {
        var (xFrame, ancestors) = WebServerService.FramePolicyFor(path);

        Assert.Equal("SAMEORIGIN", xFrame);
        Assert.Equal("'self'", ancestors);
        Assert.EndsWith("frame-ancestors 'self'", WebServerService.ContentSecurityPolicy(ancestors));
    }

    [Theory]
    [InlineData("/remote")]
    [InlineData("/manage")]
    [InlineData("/checkin")]
    [InlineData("/overlay-gallery")]
    [InlineData("/overlay-edit")]
    [InlineData("/overlayx")]
    [InlineData("/")]
    [InlineData(null)]
    public void Every_other_page_still_may_not_be_framed(string? path)
    {
        var (xFrame, ancestors) = WebServerService.FramePolicyFor(path);

        Assert.Equal("DENY", xFrame);
        Assert.Equal("'none'", ancestors);
    }

    // --- #2: a junior's full name in the stream's captions --------------------------------------

    [Fact]
    public void A_caption_cut_inside_a_juniors_name_keeps_the_surname_off_the_stream()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "24201", DriverName = "Zelda Quickfoot", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "24202", DriverName = "Terry Grownupson" });

        // One sentence of more than 140 characters, made so CaptionLog cuts
        // it between the junior's first name and surname.
        var whole = "Running order " + new string('a', 120) + " Zelda Quickfoot holds second, Terry Grownupson third";
        var pieces = CaptionLog.Pieces(whole);
        Assert.Equal(2, pieces.Count);
        Assert.EndsWith("Zelda", pieces[0]);
        Assert.StartsWith("Quickfoot", pieces[1]);

        var now = DateTime.UtcNow;
        var lines = new[]
        {
            new CaptionLine(1, pieces[0], now) { WholeText = whole, PieceIndex = 0 },
            new CaptionLine(2, pieces[1], now) { WholeText = whole, PieceIndex = 1 },
        };

        var server = RacingServer(profiles, "24202");
        server.Captions = () => lines;

        var said = AllCaptionText(server);

        Assert.DoesNotContain("Quickfoot", said);
        Assert.Contains("Zelda Q", said);
        Assert.Contains("Terry Grownupson", said);
    }

    [Fact]
    public void A_juniors_announcer_name_is_shortened_in_the_stream_captions()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "24211", DriverName = "Yolanda Brightwater", AnnouncerName = "Yolahnda Brightwater", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "24212", DriverName = "Gordon Grownupman" });

        var server = RacingServer(profiles, "24212");
        server.Captions = () => new[] { new CaptionLine(1, "Yolahnda Brightwater takes the lead from Gordon Grownupman.", DateTime.UtcNow) };

        var said = AllCaptionText(server);

        Assert.Equal("Yolanda B takes the lead from Gordon Grownupman.", said);
    }

    [Fact]
    public void A_juniors_announcer_name_is_shortened_in_the_captions_with_nothing_racing()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "24231", DriverName = "Wilhelmina Stormcloud", AnnouncerName = "Willa Stormcloud", IsJunior = true });

        var server = Server(profiles);
        server.Captions = () => new[] { new CaptionLine(1, "Next up on the grid, Willa Stormcloud.", DateTime.UtcNow) };

        var said = AllCaptionText(server);

        Assert.Equal("Next up on the grid, Wilhelmina S.", said);
    }

    [Fact]
    public void A_junior_kept_off_the_stream_is_their_car_number_in_the_captions_by_any_name()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "24221", DriverName = "Xander Fastlane", AnnouncerName = "Zander Fastlane", IsJunior = true, JuniorOffStream = true, RaceNumber = "4" });
        profiles.Upsert(new DriverProfile { Transponder = "24222", DriverName = "Harold Grownupwell" });

        var server = RacingServer(profiles, "24222");
        server.Captions = () => new[] { new CaptionLine(1, "What a clean run from Zander Fastlane's car.", DateTime.UtcNow) };

        var said = AllCaptionText(server);

        Assert.Equal("What a clean run from Car 4's car.", said);
        Assert.DoesNotContain("Fastlane", said);
    }

    [Fact]
    public void A_juniors_nickname_is_not_swapped_inside_an_adults_name()
    {
        var profiles = new[]
        {
            new DriverProfile { Transponder = "1", DriverName = "Samira Lowe", AnnouncerName = "Sam", IsJunior = true },
            new DriverProfile { Transponder = "2", DriverName = "Sam Jones" },
        };
        var swaps = WebServerService.StreamCaptionSwaps(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Samira Lowe"] = "Samira L" },
            profiles);

        Assert.Equal("Sam Jones beats Samira L, then Samira L again.",
            WebServerService.ForStreamCaption("Sam Jones beats Sam, then Samira Lowe again.", swaps));
    }

    [Fact]
    public void A_caption_with_no_junior_in_it_is_left_as_heard()
    {
        var swaps = WebServerService.StreamCaptionSwaps(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Samira Lowe"] = "Samira L" },
            Array.Empty<DriverProfile>());

        var line = new CaptionLine(7, "Green flag, here we go.", DateTime.UtcNow);

        Assert.Equal("Green flag, here we go.", WebServerService.StreamCaptionPiece(line, swaps));
    }
}

/// <summary>
/// #326: the between-races start times follow the 24-hour tick on Themes.
/// Flips the app-wide flag, so runs with the other tests that do.
/// </summary>
[Collection(AppTimeCollection.Name)]
public sealed class BugHunt24_WebServerService_1_ClockTests
{
    [Fact]
    public void Between_races_start_times_follow_the_12_or_24_hour_choice()
    {
        var was = AppTime.Use24Hour;

        try
        {
            var local = new DateTime(2026, 11, 23, 19, 40, 0, DateTimeKind.Local);
            var utc = local.ToUniversalTime();

            AppTime.Use24Hour = false;
            var (twelve, _) = WebServerService.UpNextWhen(utc);
            Assert.StartsWith("about 7", twelve);
            Assert.Equal("about " + AppTime.TimeOfDay(local), twelve);

            AppTime.Use24Hour = true;
            var (twentyFour, _) = WebServerService.UpNextWhen(utc);
            Assert.Equal("about " + AppTime.TimeOfDay(local), twentyFour);
            Assert.StartsWith("about 19", twentyFour);
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }
}
