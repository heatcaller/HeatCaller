namespace HeatCaller.Tests;

/// <summary>
/// The shape of a driver's last few race NIGHTS, added 11 Oct 2026.
///
/// The depth plan's Tier 1 item 2 was marked done against the form guide,
/// which reads a driver's recent placings out in order - "fourth, second,
/// first, sixth". That is a results sheet read aloud. What the plan asked
/// for was the shape of the last month, and the difference is not detail,
/// it is the UNIT: a club member's memory is organised by nights. "He has
/// been on the podium three weeks running" is a thing a regular says to
/// another regular in the pits.
///
/// Every line here is a claim about somebody standing in the room, made to
/// a room of people who were also there - which is a harder audience than
/// any statistic in this app has had. So most of this file is about the
/// two ways it could be wrong in front of them: a window that is really a
/// calendar rather than the club's own nights, and a run "broken" by a
/// night the driver was away for.
/// </summary>
public sealed class RecentWeeksTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>Seven in the evening, which NightOf reads as that day's race night.</summary>
    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private const string Stock = "Box Stock";

    // ------------------------------------------------------------------
    //  The builders, over figures handed to them flat
    // ------------------------------------------------------------------

    private static DriverWeeks Weeks(
        string id = "A",
        string name = "Sam",
        int clubRan = 6,
        int out_ = 5,
        int missed = 1,
        int podiumRun = 0,
        int started = 11,
        bool finishedAll = false,
        string lastOut = "3 October") =>
        new(id, name, clubRan, out_, missed, podiumRun, started, finishedAll, lastOut);

    [Fact]
    public void A_run_of_podium_nights_says_how_many_and_over_what()
    {
        var block = CommentaryWeekFacts.OneDriver(Weeks(podiumRun: 4), Stock, "night-1");

        Assert.NotNull(block);
        Assert.Contains("Sam", block!.Text);
        Assert.Contains("top three", block.Text);

        // Spelled, because these lines are written to be spoken.
        Assert.Contains("four", block.Text);

        // THE WINDOW AND THE CLASS. "Three weeks running" without either
        // is a claim about a career, and at a club running two classes it
        // is one a driver can disprove by naming the other.
        Assert.Contains(Stock, block.Text);
        Assert.Contains("the club's last", block.Text);
    }

    [Fact]
    public void Two_podium_nights_in_a_row_is_a_coincidence()
    {
        for (var run = 0; run < CommentaryWeekFacts.LeastNightsRunning; run++)
        {
            var block = CommentaryWeekFacts.OneDriver(Weeks(podiumRun: run), Stock, "night-1");

            // Either nothing at all, or something that is not the run.
            if (block is not null)
            {
                Assert.DoesNotContain("top three", block.Text);
            }
        }

        Assert.Contains(
            "top three",
            CommentaryWeekFacts.OneDriver(
                Weeks(podiumRun: CommentaryWeekFacts.LeastNightsRunning), Stock, "night-1")!.Text);
    }

    [Fact]
    public void A_driver_on_their_third_night_at_the_club_has_no_shape_yet()
    {
        // Two nights is not a shape - the line would be a claim about
        // somebody's whole short history at the club.
        for (var nights = 0; nights < CommentaryWeekFacts.LeastNightsOut; nights++)
        {
            Assert.Null(CommentaryWeekFacts.OneDriver(
                Weeks(clubRan: nights, out_: nights, missed: 0, podiumRun: nights), Stock, "night-1"));
        }
    }

    [Fact]
    public void Perfect_attendance_is_only_said_when_the_window_is_full()
    {
        // "Out at every one of the club's last three nights" is true of
        // somebody who joined a month ago, and is not what it sounds like.
        var short_ = CommentaryWeekFacts.OneDriver(
            Weeks(clubRan: 4, out_: 4, missed: 0), Stock, "night-1");

        Assert.Null(short_);

        var full = CommentaryWeekFacts.OneDriver(
            Weeks(clubRan: CommentaryWeekFacts.NightsInWindow, out_: CommentaryWeekFacts.NightsInWindow, missed: 0),
            Stock,
            "night-1");

        Assert.NotNull(full);
        Assert.Contains("every one of the club's last", full!.Text);
    }

    [Fact]
    public void A_clean_run_of_finishes_needs_enough_races_to_mean_something()
    {
        // "He has finished both of his races" is the kind of true a room
        // laughs at.
        Assert.Null(CommentaryWeekFacts.OneDriver(
            Weeks(missed: 2, started: 2, finishedAll: true), Stock, "night-1"));

        var clean = CommentaryWeekFacts.OneDriver(
            Weeks(missed: 2, started: CommentaryWeekFacts.LeastRacesForClean, finishedAll: true), Stock, "night-1");

        Assert.NotNull(clean);
        Assert.Contains("finished every one of the 6 races", clean!.Text);
    }

    [Fact]
    public void A_driver_back_after_a_while_away_is_the_last_thing_reached_for()
    {
        // A room would rather hear what somebody did than what they
        // missed, so this only lands when nothing else is true.
        var back = CommentaryWeekFacts.OneDriver(
            Weeks(clubRan: 6, out_: 3, missed: 3, started: 3, lastOut: "22 August"), Stock, "night-1");

        Assert.NotNull(back);
        Assert.Contains("back after missing", back!.Text);
        Assert.Contains("22 August", back.Text);

        // And never without a date on it - "back after missing three
        // nights" with no "their last was" is a claim with nothing behind it.
        Assert.Null(CommentaryWeekFacts.OneDriver(
            Weeks(clubRan: 6, out_: 3, missed: 3, started: 3, lastOut: ""), Stock, "night-1"));
    }

    [Fact]
    public void One_line_per_driver_and_it_is_the_best_one_true_of_them()
    {
        // A driver on a podium run who has also not missed a night gets
        // the run. Two sentences about one person inside one segment is
        // the repetition the themes exist to prevent.
        var both = Weeks(
            clubRan: CommentaryWeekFacts.NightsInWindow,
            out_: CommentaryWeekFacts.NightsInWindow,
            missed: 0,
            podiumRun: 4,
            finishedAll: true);

        var block = CommentaryWeekFacts.OneDriver(both, Stock, "night-1");

        Assert.NotNull(block);
        Assert.Contains("top three", block!.Text);
        Assert.DoesNotContain("every one of the club's last", block.Text);
    }

    [Fact]
    public void A_club_with_no_class_on_file_still_gets_the_nights_named()
    {
        var block = CommentaryWeekFacts.OneDriver(Weeks(podiumRun: 3), null, "night-1");

        Assert.NotNull(block);
        Assert.Contains("race nights", block!.Text);
    }

    [Fact]
    public void Each_line_is_themed_on_its_driver_and_keyed_to_the_night()
    {
        var sam = CommentaryWeekFacts.OneDriver(Weeks(id: "A", name: "Sam", podiumRun: 3), Stock, "night-1");
        var bill = CommentaryWeekFacts.OneDriver(Weeks(id: "B", name: "Bill", podiumRun: 3), Stock, "night-1");
        var nextWeek = CommentaryWeekFacts.OneDriver(Weeks(id: "A", name: "Sam", podiumRun: 3), Stock, "night-2");

        Assert.Equal(FactTheme.Driver("A"), sam!.Theme);
        Assert.NotEqual(sam.Key, bill!.Key);
        Assert.NotEqual(sam.Key, nextWeek!.Key);
    }

    [Fact]
    public void The_section_takes_the_best_claims_rather_than_the_first_drivers()
    {
        // A segment that can carry two carries the two best, not whoever
        // happened to be first on the grid.
        var grid = new[]
        {
            Weeks(id: "A", name: "Sam", clubRan: 6, out_: 3, missed: 3, started: 3, lastOut: "22 August"),
            Weeks(id: "B", name: "Bill", clubRan: 6, out_: 6, missed: 0, started: 12, finishedAll: true),
            Weeks(id: "C", name: "Carl", podiumRun: 5),
        };

        var built = CommentaryWeekFacts.ForTheWeeks(grid, Stock, "night-1");

        Assert.Equal(CommentaryWeekFacts.MostLines, built.Count);

        // Carl's run is the strongest claim on the sheet, so it leads.
        Assert.Contains("Carl", built[0].Text);
        Assert.Contains("top three", built[0].Text);

        // Sam being back from a month off is the weakest, so it is the
        // one that gets squeezed.
        Assert.DoesNotContain("Sam", string.Join(" ", built.Select(b => b.Text)));
    }

    [Fact]
    public void A_club_in_its_first_weeks_gets_an_empty_section_rather_than_a_padded_one()
    {
        Assert.Empty(CommentaryWeekFacts.ForTheWeeks(null, Stock, "night-1"));
        Assert.Empty(CommentaryWeekFacts.ForTheWeeks(Array.Empty<DriverWeeks>(), Stock, "night-1"));

        // One night out of one night the club ran: nothing true clears a bar.
        Assert.Empty(CommentaryWeekFacts.ForTheWeeks(
            new[] { Weeks(clubRan: 1, out_: 1, missed: 0, started: 2) }, Stock, "night-1"));
    }

    // ------------------------------------------------------------------
    //  The reader, over a real history
    // ------------------------------------------------------------------

    /// <summary>
    /// One race on one night. <paramref name="finishers"/> is in finishing
    /// order, so the first is the winner.
    /// </summary>
    private static SessionRecord Raced(
        DateTime endedLocal,
        RaceSessionType type = RaceSessionType.Race,
        bool demo = false,
        string? roundId = null,
        params (string Transponder, int Position, string Outcome)[] finishers) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionType = type,
            IsDemo = demo,
            RoundId = roundId,
            TrackLayoutName = "Club Oval",
            StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
            EndedAtUtc = endedLocal.ToUniversalTime(),
            Results = finishers
                .Select(f => new SessionResultEntry
                {
                    Transponder = f.Transponder,
                    DriverName = f.Transponder,
                    Position = f.Position,
                    Outcome = f.Outcome,
                    Laps = 12,
                    FastLapMs = 11_500,
                })
                .ToList(),
        };

    private static ProfileStore ThreeDrivers()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "B", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "C", DriverName = "Carl" });

        return profiles;
    }

    private static IReadOnlyList<DriverWeeks> Read(
        SessionHistoryStore history,
        ProfileStore profiles,
        params string[] who) =>
        CommentaryReaders.RecentWeeks(who, string.Empty, profiles, history, RoundDefinitionStore.Load(), Now);

    [Fact]
    public void The_reader_counts_nights_rather_than_races()
    {
        // Two races on each of four nights. A driver out at all of them
        // has been out at FOUR nights, not eight.
        var sessions = new List<SessionRecord>();

        for (var week = 1; week <= 4; week++)
        {
            var night = Now.AddDays(-7 * week);

            sessions.Add(Raced(night.AddHours(-1), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }));
            sessions.Add(Raced(night, finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }));
        }

        var read = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A");
        var sam = read.Single();

        Assert.Equal(4, sam.NightsTheClubRan);
        Assert.Equal(4, sam.NightsOut);
        Assert.Equal(0, sam.NightsMissed);
        Assert.Equal(8, sam.RacesStarted);
        Assert.Equal(4, sam.PodiumNightsRunning);
        Assert.True(sam.FinishedEverything);
    }

    [Fact]
    public void A_night_the_driver_missed_neither_counts_nor_breaks_a_run()
    {
        // THE ONE A ROOM WOULD CATCH. A driver away for a fortnight has
        // not had his podium run ended by being on holiday.
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-28), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
            Raced(Now.AddDays(-21), finishers: new[] { ("B", 1, ""), ("C", 2, "") }),
            Raced(Now.AddDays(-14), finishers: new[] { ("B", 1, ""), ("C", 2, "") }),
            Raced(Now.AddDays(-7), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
        };

        var sam = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A").Single();

        Assert.Equal(4, sam.NightsTheClubRan);
        Assert.Equal(2, sam.NightsOut);
        Assert.Equal(2, sam.NightsMissed);

        // Both of the nights he WAS there for, he won - so the run is two,
        // not one, and the fortnight off is not in it.
        Assert.Equal(2, sam.PodiumNightsRunning);
    }

    [Fact]
    public void A_run_ends_on_a_night_they_were_there_for_and_did_not_manage_it()
    {
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-21), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
            Raced(Now.AddDays(-14), finishers: new[] { ("B", 1, ""), ("C", 2, ""), ("A", 7, "") }),
            Raced(Now.AddDays(-7), finishers: new[] { ("A", 2, ""), ("B", 1, ""), ("C", 3, "") }),
        };

        var sam = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A").Single();

        // Newest night first: second (counts), then seventh (ends it).
        Assert.Equal(1, sam.PodiumNightsRunning);
        Assert.Equal(3, sam.NightsOut);
    }

    [Fact]
    public void A_retirement_is_not_a_finish()
    {
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-21), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
            Raced(Now.AddDays(-14), finishers: new[] { ("A", 9, "DNF"), ("B", 1, ""), ("C", 2, "") }),
            Raced(Now.AddDays(-7), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
        };

        var sam = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A").Single();

        Assert.False(sam.FinishedEverything);

        // And the night he retired on is a night he was there for, so it
        // ends the run.
        Assert.Equal(1, sam.PodiumNightsRunning);
    }

    [Fact]
    public void The_window_is_the_clubs_own_nights_and_never_a_span_of_weeks()
    {
        // Ten weekly nights; only the last six are in the window. A club
        // that skipped a fortnight has not had a driver miss two weeks -
        // which is the same rule read the other way round.
        var sessions = Enumerable.Range(1, 10)
            .Select(week => Raced(Now.AddDays(-7 * week), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }))
            .ToList();

        var sam = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A").Single();

        Assert.Equal(CommentaryWeekFacts.NightsInWindow, sam.NightsTheClubRan);
        Assert.Equal(CommentaryWeekFacts.NightsInWindow, sam.NightsOut);
    }

    [Fact]
    public void Tonights_own_races_are_not_part_of_the_shape()
    {
        // A run of three that includes the heat an hour ago is the app
        // describing this evening as history in front of the room that
        // watched it.
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-14), finishers: new[] { ("A", 5, ""), ("B", 1, ""), ("C", 2, "") }),
            Raced(Now.AddDays(-7), finishers: new[] { ("A", 6, ""), ("B", 1, ""), ("C", 2, "") }),
            Raced(Now.AddHours(-1), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
        };

        var sam = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A").Single();

        Assert.Equal(2, sam.NightsTheClubRan);
        Assert.Equal(0, sam.PodiumNightsRunning);
        Assert.Equal(2, sam.RacesStarted);
    }

    [Fact]
    public void Practice_and_demo_nights_are_not_nights()
    {
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-21), type: RaceSessionType.Practice, finishers: new[] { ("A", 1, "") }),
            Raced(Now.AddDays(-14), demo: true, finishers: new[] { ("A", 1, "") }),
            Raced(Now.AddDays(-7), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
        };

        var sam = Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A").Single();

        Assert.Equal(1, sam.NightsTheClubRan);
        Assert.Equal(1, sam.RacesStarted);
    }

    [Fact]
    public void A_driver_with_no_name_on_their_profile_is_left_out_rather_than_named_by_transponder()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = string.Empty });

        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-7), finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
        };

        Assert.Empty(Read(SessionHistoryStore.FromSessions(sessions), profiles, "A"));
    }

    [Fact]
    public void A_driver_who_has_not_been_out_at_all_in_the_window_is_not_talked_about()
    {
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-7), finishers: new[] { ("B", 1, ""), ("C", 2, "") }),
        };

        Assert.Empty(Read(SessionHistoryStore.FromSessions(sessions), ThreeDrivers(), "A"));
    }

    [Fact]
    public void A_class_is_scoped_by_the_clubs_own_rounds()
    {
        // At a club running two classes, "three nights running" said about
        // one class's podiums is a claim a driver can disprove by naming
        // the other.
        var rounds = RoundDefinitionStore.Load();

        var stockRound = new RoundDefinition { ClassName = Stock, RoundLabel = "A-Main", Kind = RoundKind.Main };
        var openRound = new RoundDefinition { ClassName = "Open", RoundLabel = "A-Main", Kind = RoundKind.Main };

        rounds.Add(stockRound);
        rounds.Add(openRound);

        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-21), roundId: stockRound.Id, finishers: new[] { ("A", 1, ""), ("B", 2, ""), ("C", 3, "") }),
            Raced(Now.AddDays(-14), roundId: openRound.Id, finishers: new[] { ("A", 8, ""), ("B", 1, ""), ("C", 2, "") }),
            Raced(Now.AddDays(-7), roundId: stockRound.Id, finishers: new[] { ("A", 2, ""), ("B", 1, ""), ("C", 3, "") }),
        };

        var stock = CommentaryReaders.RecentWeeks(
            new[] { "A" }, Stock, ThreeDrivers(), SessionHistoryStore.FromSessions(sessions), rounds, Now).Single();

        // His eighth in the Open round is not in his Box Stock shape.
        Assert.Equal(2, stock.NightsTheClubRan);
        Assert.Equal(2, stock.PodiumNightsRunning);
    }
}
