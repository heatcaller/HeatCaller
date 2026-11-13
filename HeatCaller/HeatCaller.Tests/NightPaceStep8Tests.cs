namespace HeatCaller.Tests;

/// <summary>
/// Setup plan step 8 (12 Nov 2026): the race director's two measures
/// (NightMeasures), the night-pace gaps - called and saved times, the worst
/// green to green, "idle director" and "break", and what the desk did in a
/// gap (RoundDefinition.GapNotes) - and night pace on the online results.
/// </summary>
public sealed class NightPaceStep8Tests
{
    private static readonly DateTime Seven = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double minutes) => Seven.AddMinutes(minutes);

    private static SessionRecord Race(string label, double start, double length = 5, string? roundId = null) => new()
    {
        Id = "s-" + label,
        SessionType = RaceSessionType.Race,
        Label = label,
        RoundId = roundId,
        StartedAtUtc = At(start),
        EndedAtUtc = At(start + length),
    };

    private static RoundDefinition Round(string id, string label = "Q1 Heat 1", RoundKind kind = RoundKind.Qualifier) => new()
    {
        Id = id,
        RoundLabel = label,
        Kind = kind,
        CreatedAtUtc = At(-120),
    };

    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(150);

    // --- The two measures ---------------------------------------------------

    [Fact]
    public void Entries_close_to_heats_posted_and_doors_open_to_first_green()
    {
        var closeLocal = At(-30).ToLocalTime().TimeOfDay;
        var day = new RaceEvent
        {
            EntriesCloseLocalTime = closeLocal,
            HeatsPostedUtc = At(-26),
            DoorsOpenUtc = At(-75),
        };

        var figures = NightMeasures.For(day, new[] { Race("H1", 0), Race("H2", 10) });

        Assert.Equal(TimeSpan.FromMinutes(4), figures.EntriesToHeats);
        Assert.False(figures.HeatsNotPosted);
        Assert.Equal(TimeSpan.FromMinutes(75), figures.DoorsToGreen);
        Assert.Equal("entries close to heats posted 4:00 · doors open to first green 1 h 15 min", NightMeasures.Line(figures));
    }

    [Fact]
    public void Heats_never_posted_are_measured_to_the_first_green_and_say_so()
    {
        var day = new RaceEvent { EntriesCloseLocalTime = At(-20).ToLocalTime().TimeOfDay };

        var figures = NightMeasures.For(day, new[] { Race("H1", 0) });

        Assert.True(figures.HeatsNotPosted);
        Assert.Equal(TimeSpan.FromMinutes(20), figures.EntriesToHeats);
        Assert.Equal("entries close to first green 20:00 (heats not posted)", NightMeasures.Line(figures));
    }

    [Fact]
    public void Heats_posted_before_entries_closed_says_so_rather_than_a_negative_time()
    {
        var day = new RaceEvent { EntriesCloseLocalTime = At(-10).ToLocalTime().TimeOfDay, HeatsPostedUtc = At(-15) };

        Assert.Equal("heats posted 5:00 before entries closed", NightMeasures.Line(NightMeasures.For(day, Array.Empty<SessionRecord>())));
    }

    [Fact]
    public void The_first_green_skips_practice_demo_and_practice_rounds()
    {
        var practiceRound = Round("p", "Open practice", RoundKind.Practice);
        var practice = Race("P", -40, roundId: "p");
        var demo = Race("D", -30);
        demo.IsDemo = true;

        Assert.Equal(At(0), NightMeasures.FirstGreen(new[] { practice, demo, Race("H1", 0) }, new[] { practiceRound }));
    }

    [Fact]
    public void Doors_open_is_stamped_once_and_never_on_the_legacy_holder_or_a_closed_day()
    {
        var day = new RaceEvent();
        Assert.True(NightMeasures.StampDoorsOpen(day, At(-60)));
        Assert.False(NightMeasures.StampDoorsOpen(day, At(-10)));
        Assert.Equal(At(-60), day.DoorsOpenUtc);

        Assert.False(NightMeasures.StampDoorsOpen(new RaceEvent { IsLegacy = true }, At(0)));
        Assert.False(NightMeasures.StampDoorsOpen(new RaceEvent { FinishedUtc = At(0) }, At(0)));
        Assert.False(NightMeasures.StampDoorsOpen(null, At(0)));
    }

    [Fact]
    public void Nothing_to_measure_is_an_empty_line()
    {
        Assert.True(NightMeasures.For(null, Array.Empty<SessionRecord>()).Empty);
        Assert.Equal(string.Empty, NightMeasures.Line(NightMeasures.For(new RaceEvent(), Array.Empty<SessionRecord>())));
    }

    [Fact]
    public void A_check_in_by_any_route_tells_whoever_stamps_doors_open()
    {
        var store = Source("CheckInStore.cs");
        Assert.Contains("CheckedIn?.Invoke(CurrentEventId);", store, StringComparison.Ordinal);

        var wiring = Source("MainForm.PaceAndStarts.cs");
        Assert.Contains("NightMeasures.StampDoorsOpen(day, DateTime.UtcNow)", wiring, StringComparison.Ordinal);
    }

    // --- The gaps -----------------------------------------------------------

    [Fact]
    public void The_worst_green_to_green_is_found_with_its_cause()
    {
        // Greens at 0, 8, 30, 38: the worst pair is H2 to H3.
        var main = new RoundDefinition { Id = "built", Kind = RoundKind.Main, CreatedAtUtc = At(20) };
        var pace = NightPace.For(new[] { Race("H1", 0), Race("H2", 8), Race("H3", 30), Race("H4", 38) }, new[] { main }, setGap: Gap)!;

        Assert.Equal("H2", pace.WorstGreenToGreen!.Before.Label);
        Assert.Equal(TimeSpan.FromMinutes(22), pace.WorstGreenToGreen.GreenToGreen);
        Assert.Equal("building the finals", pace.WorstGreenToGreen.CauseText);
        Assert.StartsWith("Green to green 8:00, worst 22:00 · longest gap 17:00 (building the finals)", NightPace.Line(pace));
    }

    [Fact]
    public void A_gap_well_over_the_set_gap_with_nothing_going_on_is_an_idle_director()
    {
        var h2 = Round("h2", "Q1 Heat 2");
        h2.CalledAtUtc = At(12);

        // H1 finishes at 5; H2 goes green at 15: ten minutes against a 2:30 gap.
        var gap = NightPace.For(new[] { Race("H1", 0, roundId: "h1"), Race("H2", 15, roundId: "h2") }, new[] { Round("h1"), h2 }, setGap: Gap)!.Longest!;

        Assert.Equal("idle director (called up 7:00 after the flag)", gap.CauseText);
        Assert.Equal(TimeSpan.FromMinutes(3), gap.CalledToGreen);
    }

    [Fact]
    public void A_gap_only_a_little_over_is_not_idle()
    {
        var gap = NightPace.For(new[] { Race("H1", 0), Race("H2", 11) }, setGap: Gap)!.Longest!;

        Assert.Empty(gap.Causes);
    }

    [Fact]
    public void A_long_gap_with_nothing_going_on_is_a_break_and_a_planned_one_says_how_long()
    {
        var unplanned = NightPace.For(new[] { Race("H1", 0), Race("H2", 35) }, setGap: Gap)!.Longest!;
        Assert.Equal("a break", unplanned.CauseText);

        var q1 = Round("q1", "Q1 Heat 1");
        var q2 = Round("q2", "Q2 Heat 1");
        var breaks = new List<PlannedBreak> { new() { AfterRound = 1, Minutes = 10 } };
        var planned = NightPace.For(new[] { Race("Q1", 0, roundId: "q1"), Race("Q2", 20, roundId: "q2") }, new[] { q1, q2 }, setGap: Gap, breaks: breaks)!.Longest!;

        Assert.Equal("a planned break (10 min)", planned.CauseText);
    }

    [Fact]
    public void What_the_desk_did_in_the_gap_is_named()
    {
        var h2 = Round("h2", "Q1 Heat 2");
        h2.GapNotes = new List<GapNote>
        {
            new() { Kind = GapNoteKind.Held, AtUtc = At(7), Seconds = 180 },
            new() { Kind = GapNoteKind.PushedBack, AtUtc = At(8), Seconds = 60 },
            new() { Kind = GapNoteKind.PushedBack, AtUtc = At(9), Seconds = 60 },
            new() { Kind = GapNoteKind.DriverAskedForTime, AtUtc = At(9), Seconds = 300 },
            new() { Kind = GapNoteKind.WaitedForPromise, AtUtc = At(19), Seconds = 45 },
            new() { Kind = GapNoteKind.StartedNow, AtUtc = At(19), Seconds = 0 },

            // From some earlier gap - not this one's.
            new() { Kind = GapNoteKind.Held, AtUtc = At(-30), Seconds = 600 },
        };

        var gap = NightPace.For(new[] { Race("H1", 0, roundId: "h1"), Race("H2", 20, roundId: "h2") }, new[] { Round("h1"), h2 }, setGap: Gap)!.Longest!;

        Assert.Equal(new[]
        {
            "held 3 min at the desk",
            "pushed back 2 min (+1 min)",
            "5 min more for a driver who asked",
            "waited 45 s for the time drivers were given",
        }, gap.Causes);
    }

    [Fact]
    public void A_slow_save_is_named_and_the_typical_save_is_measured()
    {
        var h1 = Round("h1");
        h1.SavedAtUtc = At(9);

        var pace = NightPace.For(new[] { Race("H1", 0, roundId: "h1"), Race("H2", 12, roundId: "h2") }, new[] { h1, Round("h2", "Q1 Heat 2") }, setGap: Gap)!;

        Assert.Equal(TimeSpan.FromMinutes(4), pace.MedianFinishToSaved);
        Assert.Contains("saving the result took 4:00", pace.Longest!.Causes);
    }

    [Fact]
    public void A_save_outside_the_gap_is_not_this_gaps()
    {
        var h1 = Round("h1");
        h1.SavedAtUtc = At(40);

        var pace = NightPace.For(new[] { Race("H1", 0, roundId: "h1"), Race("H2", 10, roundId: "h2") }, new[] { h1 }, setGap: Gap)!;

        Assert.Null(pace.MedianFinishToSaved);
        Assert.Null(pace.Longest!.FinishToSaved);
    }

    [Fact]
    public void Saving_a_round_stamps_when_and_saving_it_again_keeps_the_first()
    {
        var store = Source("RoundDefinitionStore.cs");
        Assert.Contains("round.SavedAtUtc = DateTime.UtcNow;", store, StringComparison.Ordinal);
    }

    [Fact]
    public void Hold_plus_one_start_now_and_the_call_up_log_into_the_gap()
    {
        var auto = Source("RaceControlControl.AutoStart.cs");
        Assert.Contains("NoteGap(hold.RoundId, GapNoteKind.Held", auto, StringComparison.Ordinal);
        Assert.Contains("GapNoteKind.StartedNow", auto, StringComparison.Ordinal);
        Assert.Contains("GapNoteKind.DriverAskedForTime", auto, StringComparison.Ordinal);
        Assert.Contains("GapNoteKind.WaitedForPromise", auto, StringComparison.Ordinal);

        var between = Source("RaceControlControl.BetweenRaces.cs");
        Assert.Contains("GapNoteKind.PushedBack", between, StringComparison.Ordinal);
        Assert.Contains("RaceCalledUp?.Invoke(calledId);", between, StringComparison.Ordinal);
    }

    // --- Where it is shown --------------------------------------------------

    [Fact]
    public void The_public_line_has_the_numbers_and_never_the_causes()
    {
        var h2 = Round("h2", "Q1 Heat 2");
        var pace = NightPace.For(new[] { Race("H1", 0, roundId: "h1"), Race("H2", 8, roundId: "h2"), Race("H3", 30) }, new[] { Round("h1"), h2 }, setGap: Gap)!;

        var line = NightPace.PublicLine(pace);

        Assert.StartsWith("Green to green 15:00 (worst 22:00)", line);
        Assert.DoesNotContain("idle", line);
        Assert.DoesNotContain("break", line);
    }

    [Fact]
    public void The_results_index_carries_a_days_pace_and_leaves_a_day_without_one_alone()
    {
        var all = new[] { Race("H1", 0), Race("H2", 10) };

        var with = RelayResultsIndex.Build("2026", all, _ => null, false, null, paceOfDay: (_, sessions) => sessions.Count == 2 ? "Green to green 10:00" : null);
        Assert.Contains("\"pace\":\"Green to green 10:00\"", with, StringComparison.Ordinal);

        var without = RelayResultsIndex.Build("2026", all, _ => null, false, null);
        Assert.DoesNotContain("\"pace\"", without, StringComparison.Ordinal);
    }

    [Fact]
    public void A_days_public_pace_reads_its_race_day()
    {
        var q1 = Round("q1");
        q1.LinkedSessionId = "s-H1";
        var day = new RaceEvent { RoundIds = new List<string> { "q1", "q2" } };

        var line = RelayResultsIndex.PaceOf(new[] { Race("H1", 0, roundId: "q1"), Race("H2", 10) }, new[] { q1, Round("q2") }, new[] { day });

        Assert.Equal("Green to green 10:00 · 67% on track · 1 of 2 races run", line);
        Assert.Null(RelayResultsIndex.PaceOf(new[] { Race("H1", 0) }, Array.Empty<RoundDefinition>(), Array.Empty<RaceEvent>()));
    }

    [Fact]
    public void The_next_steps_note_carries_the_two_measures()
    {
        var q1 = Round("q1");
        q1.LinkedSessionId = "s-H1";
        var q2 = Round("q2", "Q1 Heat 2");
        q2.LinkedSessionId = "s-H2";
        var day = new RaceEvent { DoorsOpenUtc = At(-40), HeatsPostedUtc = At(-10), EntriesCloseLocalTime = At(-15).ToLocalTime().TimeOfDay };

        var note = ProgramNextStep.PaceNote(new[] { q1, q2 }, new[] { Race("H1", 0, roundId: "q1"), Race("H2", 10, roundId: "q2") }, day);

        Assert.StartsWith(" Night pace: Green to green 10:00", note);
        Assert.EndsWith(" Entries close to heats posted 5:00 · doors open to first green 40:00.", note);
    }

    [Fact]
    public void The_event_report_and_the_closing_checklist_show_the_measures()
    {
        var report = Source("EventReportBuilder.cs");
        Assert.Contains("Doors open to first green", report, StringComparison.Ordinal);
        Assert.Contains("Worst green to green", report, StringComparison.Ordinal);

        Assert.Contains("PaceLine = () => program.ClosingPaceLine(dayId)", Source("MainForm.NightClose.cs"), StringComparison.Ordinal);
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", file));

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeatCaller", "HeatCaller", "HeatCaller.csproj")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root.");
        }
    }
}
