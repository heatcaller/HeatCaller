using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 14 (7 Nov 2026): the championship figures and the cast.
///
///  - C1: the public standings' "what it takes" counted each driver's
///    dropped rounds twice in the rounds held, and a night of two counting
///    races as two rounds.
///  - C2: a race the director marked DNF (laps kept) was said to be
///    "entered without completing a lap".
///  - C2/C3 recap wording lives in CommentaryGapsTests and RecapBroadcastTests.
///  - B1: the On Air skips now cancel with CancelAsync - see
///    OnAirSkipSilencesTests.On_Air_skips_cancel_without_running_On_Air_inline.
///  - B2: a sponsor video's sound played over On Air's jingle.
///  - B3: Replay Moment pressed again during a race replayed the replay.
/// </summary>
public sealed class BugHunt14_ChampCastTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    //  C1 - the rounds held, for the public standings
    // ------------------------------------------------------------------

    /// <summary>
    /// Races, one per entry, each put on the race day its index in
    /// <paramref name="nights"/> says - so two races can share a night.
    /// </summary>
    private static (List<SessionRecord> Races, RaceEventStore Days) Club(int[] nights)
    {
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);
        var races = new List<SessionRecord>();
        var onNight = new Dictionary<int, List<string>>();

        for (var i = 0; i < nights.Length; i++)
        {
            var roundId = "round-" + i.ToString(CultureInfo.InvariantCulture);

            races.Add(new SessionRecord
            {
                Id = "r" + i.ToString(CultureInfo.InvariantCulture),
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                RoundId = roundId,
                StartedAtUtc = when.AddDays(7 * nights[i]),
                EndedAtUtc = when.AddDays(7 * nights[i]).AddMinutes(8),
                Results = new List<SessionResultEntry>
                {
                    new() { Transponder = "bill", DriverName = "Bill", Position = 1, Laps = 12 },
                    new() { Transponder = "sam", DriverName = "Sam", Position = 2, Laps = 12 },
                },
            });

            if (!onNight.TryGetValue(nights[i], out var ids))
            {
                ids = new List<string>();
                onNight[nights[i]] = ids;
            }

            ids.Add(roundId);
        }

        var days = RaceEventStore.Load();

        foreach (var (night, ids) in onNight)
        {
            days.Upsert(new RaceEvent
            {
                Name = "Night " + (night + 1).ToString(CultureInfo.InvariantCulture),
                StartedUtc = when.AddDays(7 * night),
                FinishedUtc = when.AddDays(7 * night).AddHours(4),
                RoundIds = ids,
            });
        }

        return (races, days);
    }

    private static Season BestSixOfTen() => new()
    {
        Name = "Winter Series",
        CountBestRounds = 6,
        PlannedRounds = 10,
        PointsScheme = Enumerable.Range(1, 10)
            .Select(p => new PointsSchemeEntry { Position = p, Points = 11 - p })
            .ToList(),
    };

    [Fact]
    public void Eight_one_race_nights_of_a_best_six_season_are_eight_rounds_held()
    {
        var season = BestSixOfTen();
        var (races, days) = Club(Enumerable.Range(0, 8).ToArray());

        // What the old figure was built from: SessionsCounted already holds
        // the dropped rounds, so adding them gave ten.
        var bill = SeasonStore.ComputeStandings(season, races).Single(e => e.DriverName == "Bill");
        Assert.Equal(8, bill.SessionsCounted);
        Assert.Equal(2, bill.DroppedRounds);

        Assert.Equal((10, 8), Championship.WhatItTakesLength(season, races, days));
    }

    [Fact]
    public void Two_counting_races_on_one_night_keep_the_page_quiet()
    {
        // An A and a B main on the second night: four races, three rounds.
        // The page prices each round left as one race, so it says nothing.
        var (races, days) = Club(new[] { 0, 1, 1, 2 });

        Assert.Equal((0, 0), Championship.WhatItTakesLength(BestSixOfTen(), races, days));
    }

    [Fact]
    public void Races_that_cannot_be_put_on_days_keep_the_page_quiet()
    {
        var (races, _) = Club(new[] { 0, 1, 2 });

        // No race days at all - and, separately, a race ticked in by hand
        // with no round.
        Assert.Equal((0, 0), Championship.WhatItTakesLength(BestSixOfTen(), races, null));

        var (more, days) = Club(new[] { 0, 1, 2 });
        more[1].RoundId = null;
        Assert.Equal((0, 0), Championship.WhatItTakesLength(BestSixOfTen(), more, days));
    }

    [Fact]
    public void A_season_with_nothing_run_yet_has_every_round_still_to_go()
    {
        Assert.Equal((10, 0), Championship.WhatItTakesLength(BestSixOfTen(), new List<SessionRecord>(), null));
    }

    [Fact]
    public void The_public_standings_send_the_rounds_held_from_the_days()
    {
        var standings = MethodBody(Source("MainForm.Relay.cs"), "private string BuildStandingsDocument()", "standings = table.Select(");

        Assert.Contains("Championship.WhatItTakesLength(season, counted, _raceEvents)", standings, StringComparison.Ordinal);
        Assert.Contains("planned = length.Planned,", standings, StringComparison.Ordinal);
        Assert.Contains("held = length.Held,", standings, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionsCounted + entry.DroppedRounds", standings, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  C2 - a DNF with laps
    // ------------------------------------------------------------------

    [Fact]
    public void A_DNF_with_laps_is_not_said_to_have_completed_no_lap()
    {
        var race = new SessionRecord
        {
            Id = "r1",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "bill", DriverName = "Bill", Position = 1, Laps = 12 },
                new() { Transponder = "sam", DriverName = "Sam", Position = 2, Laps = 9, Outcome = ResultEditor.Dnf },
            },
        };

        var sam = SeasonStore.ComputeStandings(BestSixOfTen(), new[] { race }).Single(e => e.DriverName == "Sam");

        // The race counts, its points stand, and it is a DNF.
        Assert.Equal(1, sam.Dnfs);
        Assert.Equal(1, sam.SessionsCounted);

        var behind = CommentaryService.BehindTheTotal(sam.DroppedRounds, 0, sam.CarriedIn, sam.Dnfs);

        Assert.Equal("; 1 race without a finish", behind);
        Assert.DoesNotContain("without completing a lap", behind, StringComparison.Ordinal);
        Assert.True(CommentaryService.AnyBehindTheTotal(new[] { "P2 Sam - 9 points" + behind }));
        Assert.Contains("races without a finish", CommentaryService.ChampionshipFiguresRule, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  B2 - a sponsor video over On Air's jingle
    // ------------------------------------------------------------------

    [Fact]
    public void On_Air_takes_the_sponsor_spot_off_before_its_jingle()
    {
        var onAir = WithoutComments(MethodBody(Source("RaceControlControl.OnAir.cs"),
            "private async Task OnAirAsync(", "\n    }\n"));

        var stopped = onAir.IndexOf("StopSponsorSpot(", StringComparison.Ordinal);
        var jingle = onAir.IndexOf("BroadcastImaging.Start(ImagingCue.TopOfRace)", StringComparison.Ordinal);

        Assert.True(stopped > 0 && jingle > stopped,
            "On Air must stop a sponsor video, sound and all, before the top-of-race jingle starts.");
    }

    [Fact]
    public void The_sponsor_spot_says_who_took_it_down()
    {
        var stop = MethodBody(Source("BigScreenForm.cs"), "internal void StopSponsorSpot(", "private void StopSponsorVideo()");

        Assert.Contains("string reason = \"Reset\"", stop, StringComparison.Ordinal);
        Assert.Contains("[sponsor] {reason} - the sponsor spot is taken off the Big Screen.", stop, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  B3 - Replay Moment pressed again during a race
    // ------------------------------------------------------------------

    [Fact]
    public void A_replay_during_the_race_starts_the_held_audio_again_after_it()
    {
        var replay = WithoutComments(MethodBody(Source("RaceControlControl.Broadcast.cs"),
            "private async Task ReplayTheMomentAsync()", "private bool StopTheReplay()"));

        var cleanUp = replay[replay.LastIndexOf("finally", StringComparison.Ordinal)..];
        var check = cleanUp.IndexOf("if (!finished)", StringComparison.Ordinal);
        var restart = cleanUp.IndexOf("buffer.Restart();", StringComparison.Ordinal);

        Assert.True(check > 0 && restart > check,
            "The replay's finally must restart the held audio, and only while the race is still being recorded.");

        // The ring is never paused - a gap in Put would splice two moments.
        Assert.DoesNotContain("Pause", replay, StringComparison.Ordinal);
    }

    [Fact]
    public void After_a_restart_only_what_came_after_is_replayed()
    {
        var buffer = new RaceReplayBuffer(RaceReplayBuffer.SampleRate * 5);

        void Write(short value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                buffer.Put(value);
            }

            buffer.Commit();
        }

        Write(1, RaceReplayBuffer.SampleRate);
        buffer.Restart();
        Write(2, RaceReplayBuffer.SampleRate / 2);

        var last = buffer.Last(20);

        Assert.Equal(RaceReplayBuffer.SampleRate / 2, last.Length);
        Assert.All(last, sample => Assert.Equal((short)2, sample));
    }

    // ------------------------------------------------------------------

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");

    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, from.Length, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
