using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience W1 (22 Oct 2026): the club Wi-Fi pages - penalties on
/// the follow card (item 1), call-up timing (item 3), the transponder check
/// (item 4), captions on the overlay and the broadcast graphics (item 9) and
/// the pit board with its keep-awake video (item 10).
///
/// The LAN state is checked as JSON, the way BugHunt5WebPrivacyTests checks
/// it; the pages are checked by what their scripts say, the way the coach
/// page's tests are.
/// </summary>
public sealed class DriverExp_W1_WiFiTests : IDisposable
{
    // Letters in it, so it cannot turn up inside a lap time or a hex id by chance.
    private const string Transponder = "W1D7K";

    private const string DriverName = "Sam Parker";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Item 1: penalties ------------------------------------------------------------------

    [Fact]
    public void Tonight_s_penalty_lines_go_on_the_follow_card_under_the_driver_s_opaque_id()
    {
        var server = Server();
        server.PublishPenalties(new Dictionary<string, IReadOnlyList<string>>
        {
            [Transponder] = new[] { "Q1 Heat 2: +5 s - jump start" },
            ["NOBODY1"] = new[] { "Q1 Heat 1: Warning 1 - Blocking" },
        });

        var state = State(server);
        var id = IdOf(state);
        var pens = state["pens"]!.AsObject();

        Assert.Equal("Q1 Heat 2: +5 s - jump start", Assert.Single(pens[id]!.AsArray())!.GetValue<string>());

        // Only for people who can be followed: nobody can pick anybody else.
        Assert.Single(pens);
        Assert.DoesNotContain(Transponder, Without(state, "cells").ToJsonString());
    }

    [Fact]
    public void No_penalties_means_no_pens_and_the_relay_copy_never_has_them()
    {
        var server = Server();

        Assert.Null(State(server)["pens"]);

        server.PublishPenalties(new Dictionary<string, IReadOnlyList<string>>
        {
            [Transponder] = new[] { "Q1 Heat 2: +5 s - Cut the chicane" },
        });

        // The reasons are private outside the room - see PenaltyWords.
        var relay = JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!.AsObject();
        Assert.Null(relay["pens"]);
        Assert.DoesNotContain("chicane", relay.ToJsonString());
    }

    [Fact]
    public void A_night_of_penalties_is_cut_to_the_latest_lines()
    {
        var server = Server();
        var lines = Enumerable.Range(1, WebServerService.PenaltyLinesPerDriver + 5).Select(i => $"Race {i}: +5 s - from race control").ToArray();

        server.PublishPenalties(new Dictionary<string, IReadOnlyList<string>> { [Transponder] = lines });

        var state = State(server);
        var kept = state["pens"]![IdOf(state)]!.AsArray().Select(l => l!.GetValue<string>()).ToList();

        Assert.Equal(WebServerService.PenaltyLinesPerDriver, kept.Count);
        Assert.Equal(lines[^1], kept[^1]);
    }

    [Fact]
    public void The_live_card_carries_this_race_s_warnings_and_penalty_time_only_when_there_are_any()
    {
        var race = Racing();
        var server = Server(race);

        var clean = State(server);
        var card = clean["cards"]![IdOf(clean)]!.AsObject();
        Assert.Null(card["warn"]);
        Assert.Null(card["penS"]);

        race.IssueWarning(Transponder, "Blocking");
        race.IssueWarning(Transponder, "Blocking");

        var standing = race.GetOrderedStandings().Single(s => s.TransponderId == Transponder);
        var warned = State(server);
        card = warned["cards"]![IdOf(warned)]!.AsObject();

        Assert.Equal(2, card["warn"]!.GetValue<int>());

        if (standing.PenaltyMs > 0)
        {
            Assert.Equal(Math.Round(standing.PenaltyMs / 1000.0, 1), card["penS"]!.GetValue<double>());
        }
        else
        {
            Assert.Null(card["penS"]);
        }
    }

    [Fact]
    public void Tonight_s_penalties_are_worked_out_per_driver_with_the_race_named()
    {
        var now = DateTime.Now;
        var race = new SessionRecord
        {
            Label = "Q1 Heat 2",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow,
            Results =
            {
                new SessionResultEntry { Transponder = Transponder, RacePenaltyMs = 5_000, JumpStarted = true },
                new SessionResultEntry { Transponder = "CLEAN1" },
                new SessionResultEntry { Transponder = "GHOST-ABC123", RacePenaltyMs = 5_000 },
            },
        };

        var demo = new SessionRecord
        {
            Label = "Q1 Heat 3",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow,
            IsDemo = true,
            Results = { new SessionResultEntry { Transponder = Transponder, RacePenaltyMs = 5_000 } },
        };

        var lastWeek = new SessionRecord
        {
            Label = "Q1 Heat 1",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow.AddDays(-7),
            Results = { new SessionResultEntry { Transponder = Transponder, RacePenaltyMs = 5_000 } },
        };

        var pens = MainForm.PenaltiesTonight(new[] { race, demo, lastWeek }, now);

        Assert.Equal("Q1 Heat 2: +5 s - jump start", Assert.Single(pens[Transponder]));
        Assert.Single(pens);
    }

    // --- Item 3: when the race should start -------------------------------------------------

    [Fact]
    public void A_call_up_says_staged_and_its_eta_only_when_they_apply_and_the_state_carries_the_laptop_s_clock()
    {
        var eta = new DateTime(2026, 10, 22, 19, 42, 0, DateTimeKind.Utc);
        var server = Server();
        var staged = false;
        DateTime? when = null;

        server.UpcomingRaces = () => new[] { new UpcomingRace("Q2 Heat 1 (Stock Buggy)", new[] { Transponder }, true, "r1", staged, when) };

        var plain = State(server);
        var call = plain["callups"]![IdOf(plain)]!.AsObject();
        Assert.Null(call["staged"]);
        Assert.Null(call["eta"]);

        staged = true;
        when = eta;

        var ready = State(server);
        call = ready["callups"]![IdOf(ready)]!.AsObject();
        Assert.True(call["staged"]!.GetValue<bool>());
        Assert.Equal("2026-10-22T19:42:00.000Z", call["eta"]!.GetValue<string>());
        Assert.Equal(PhoneFollow.StagedWords, call["words"]!.GetValue<string>());

        var now = DateTime.Parse(ready["now"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.InRange(now, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Times_go_out_as_utc_whatever_kind_they_arrive_as()
    {
        Assert.Equal("2026-10-22T19:42:05.000Z", WebServerService.IsoUtc(new DateTime(2026, 10, 22, 19, 42, 5, DateTimeKind.Utc)));
        Assert.Equal("2026-10-22T19:42:05.000Z", WebServerService.IsoUtc(new DateTime(2026, 10, 22, 19, 42, 5, DateTimeKind.Unspecified)));

        var local = new DateTime(2026, 10, 22, 19, 42, 5, DateTimeKind.Local);
        Assert.Equal(WebServerService.IsoUtc(local.ToUniversalTime()), WebServerService.IsoUtc(local));
    }

    [Fact]
    public void The_follow_view_buzzes_when_the_race_is_next_and_again_when_it_is_staged()
    {
        var page = Server().BuildLiveResultsPage();

        Assert.Contains("const key = call.race + '|' + call.away + '|' + (call.staged ? 'staged' : '');", page);
        Assert.Contains("(call.staged || (!call.now && call.away === 0))) buzz();", page);

        // "about 7 min (19:42)", by the laptop's clock rather than the phone's.
        Assert.Contains("'about ' + mins + ' min'", page);
        Assert.Contains("takeClock(d.now);", page);
    }

    // --- Item 4: the transponder check ------------------------------------------------------

    [Fact]
    public void A_sighting_goes_out_as_seconds_ago_and_a_signal_word_for_the_people_list()
    {
        var server = Server();
        var heard = DateTime.UtcNow.AddSeconds(-30);
        server.SightingOf = t => t == Transponder ? (heard, "weak") : null;

        var state = State(server);
        var seen = state["seen"]![IdOf(state)]!.AsObject();

        Assert.InRange(seen["ago"]!.GetValue<int>(), 29, 40);
        Assert.Equal("weak", seen["sig"]!.GetValue<string>());

        // Anything but the three words reads as unknown.
        server.SightingOf = t => t == Transponder ? (heard, "excellent") : null;
        var odd = State(server);
        Assert.Equal("unknown", odd["seen"]![IdOf(odd)]!["sig"]!.GetValue<string>());

        // Never heard: no "seen" at all, and never in the relay's copy.
        server.SightingOf = _ => null;
        Assert.Null(State(server)["seen"]);
        Assert.Null(JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!["seen"]);
    }

    [Fact]
    public void A_sighting_reads_good_weak_or_unknown()
    {
        var good = new TransponderWatch.Sighting { TransponderId = "A", Passes = 4, SignalReadings = 4, WeakPasses = 0, LastHits = 5 };
        var weak = new TransponderWatch.Sighting { TransponderId = "B", Passes = 4, SignalReadings = 4, WeakPasses = 3, LastHits = 1 };
        var unknown = new TransponderWatch.Sighting { TransponderId = "C", Passes = 4, SignalReadings = 0, LastHits = 0 };

        Assert.Equal("good", MainForm.SignalWord(good));
        Assert.Equal("weak", MainForm.SignalWord(weak));
        Assert.Equal("unknown", MainForm.SignalWord(unknown));
    }

    [Fact]
    public void The_check_my_transponder_button_watches_for_a_sighting_newer_than_the_press()
    {
        var page = Server().BuildLiveResultsPage();

        Assert.Contains("Check my transponder", page);
        Assert.Contains("Drive past the line now", page);
        Assert.Contains("docNow - s.ago * 1000 > checkFrom", page);
        Assert.Contains("'Seen at ' + clockAt(at)", page);
        Assert.Contains("const CHECK_MS = 3 * 60 * 1000;", page);
    }

    // --- Item 9: captions -------------------------------------------------------------------

    [Fact]
    public void The_lan_state_carries_the_last_four_captions_and_none_when_nothing_was_said()
    {
        var server = Server();
        var log = new CaptionLog();
        server.Captions = () => log.Last();

        Assert.Null(State(server)["captions"]);

        for (var i = 1; i <= 6; i++)
        {
            log.Add($"Line {i}");
        }

        var captions = State(server)["captions"]!.AsArray();

        Assert.Equal(WebServerService.CaptionsInState, captions.Count);
        Assert.Equal(6L, captions[^1]!["n"]!.GetValue<long>());
        Assert.Equal("Line 6", captions[^1]!["t"]!.GetValue<string>());
        Assert.Equal("Line 3", captions[0]!["t"]!.GetValue<string>());

        // The relay has its own, redacted, captions document.
        Assert.Null(JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!["captions"]);
    }

    [Fact]
    public void The_overlay_and_the_broadcast_graphics_show_captions_only_when_asked()
    {
        var server = Server();

        foreach (var page in new[] { server.BuildLiveResultsPage(), server.BuildBroadcastPage() })
        {
            Assert.Contains("get('captions') === '1'", page);
            Assert.Contains("id=\"captions\"", page);
            Assert.Contains("const CAPTION_MS = 8000;", page);
            Assert.Contains("showCaptions(d.captions);", page);
            Assert.Contains("newest.n === captionSeen", page);
        }
    }

    // --- Item 10: the pit board and keeping awake -------------------------------------------

    [Fact]
    public void The_pit_board_is_on_the_coach_page_and_reads_only_the_board()
    {
        var page = Server().BuildCoachPage();

        Assert.Contains("id=\"boardBtn\"", page);
        Assert.Contains(">Pit board<", page);
        Assert.Contains("get('board') === '1'", page);
        Assert.Contains("root.requestFullscreen || root.webkitRequestFullscreen", page);
        Assert.Contains("grid-template-areas:\"last pos\" \"last laps\" \"ahead target\"", page);
        Assert.Contains("font-size:min(20vw, 40vh)", page);
        Assert.Contains("boardEl.classList.toggle('slow', target > 0 && last > target);", page);

        // Nothing new on the wire about anybody.
        Assert.DoesNotContain("/api/roster", page);
        Assert.DoesNotContain("/api/command", page);
    }

    [Fact]
    public void The_pit_board_keeps_the_screen_on_with_a_wake_lock_then_the_video_then_a_note()
    {
        var page = Server().BuildCoachPage();

        Assert.Contains("window.isSecureContext && navigator.wakeLock && navigator.wakeLock.request", page);
        Assert.Contains("navigator.wakeLock.request('screen')", page);
        Assert.Contains("wakeVideo.src = '" + KeepAwake.Address + "';", page);
        Assert.Contains("wakeVideo.muted = true;", page);
        Assert.Contains("wakeVideo.setAttribute('playsinline', '');", page);
        Assert.Contains("set Auto-Lock to Never", page);
        Assert.Contains("document.addEventListener('visibilitychange'", page);
        Assert.Contains("function lookBusy(){ return boardOpen || sayBox.checked; }", page);
    }

    [Fact]
    public void The_keep_awake_video_is_answered_as_an_mp4_with_ranges()
    {
        Assert.Equal("/keepawake.mp4", KeepAwake.Address);
        Assert.Equal("video/mp4", KeepAwake.ContentType);

        var (status, range, body) = KeepAwake.Answer(null);
        Assert.Equal(200, status);
        Assert.Null(range);
        Assert.Equal(3_753, body.Length);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(body, 4, 4));

        // NoSleep.js 0.12.0's own file, byte for byte - see KeepAwake.
        Assert.Equal("a27edba0e34b2648a90a800ae94fdef3e39016d1b9bd6e54a31ede1f1cddfed0",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant());

        // Safari's first ask, before it will play anything.
        var (partStatus, partRange, part) = KeepAwake.Answer("bytes=0-1");
        Assert.Equal(206, partStatus);
        Assert.Equal("bytes 0-1/3753", partRange);
        Assert.Equal(body.Take(2), part);
    }

    // --- Helpers ----------------------------------------------------------------------------

    private static JsonObject State(WebServerService server)
    {
        var document = JsonNode.Parse(server.BuildStateJson())!.AsObject();

        // AddFollowing is wrapped in a catch that serves the board without
        // it, so a throw in it shows up here rather than as a missing field.
        Assert.True(document["people"] is JsonArray, "No follow list in the document - AddFollowing threw.");

        return document;
    }

    /// <summary>The driver's opaque id, from the follow list.</summary>
    private static string IdOf(JsonObject state) =>
        state["people"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == DriverName)!["id"]!.GetValue<string>();

    /// <summary>The document without each row's cells, which carry whatever columns the club chose.</summary>
    private static JsonObject Without(JsonObject state, string rowField)
    {
        var copy = JsonNode.Parse(state.ToJsonString())!.AsObject();

        foreach (var row in copy["standings"]!.AsArray())
        {
            row!.AsObject().Remove(rowField);
        }

        return copy;
    }

    private static RaceControlSession Racing()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        Cross(race, Transponder, 11_000);
        Cross(race, Transponder, 23_000);

        return race;
    }

    private static WebServerService Server(RaceControlSession? race = null)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = Transponder, DriverName = DriverName });

        // One of the app's own looks rather than "match the app", for the
        // reason BugHunt4WebTests gives.
        return new WebServerService(new RaceTiming(race ?? Racing()), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });
    }

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });
}
