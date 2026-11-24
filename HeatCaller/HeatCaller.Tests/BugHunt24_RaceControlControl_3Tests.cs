namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race Control: the A-Main overall said with no
/// name, Redo Remaining Heats on a one-round day with Q2 built beside it,
/// the phone told a flag went out when a press only skipped a demo's On Air
/// opening, a shared heat's leftover name, a penalty credit's words, and
/// the achievements segment under "Lap times only".
/// </summary>
public sealed class BugHunt24_RaceControlControl_3Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- F215: the A-Main overall with an unnamed winner ---------------------

    [Fact]
    public void BugHunt24_The_A_Main_overall_names_an_unnamed_winner_rather_than_saying_no_name()
    {
        var line = RaceControlControl.AMainOverallLine(string.Empty, "1234567", 2, 3, "A");

        Assert.StartsWith("Unnamed driver wins the A-Main overall", line, StringComparison.Ordinal);
        Assert.Equal("Unnamed driver wins the A-Main overall, best two of three legs.", line);
    }

    [Fact]
    public void BugHunt24_The_A_Main_overall_still_says_a_named_winner()
    {
        Assert.Equal(
            "Sam wins the A-Main overall, best two of three legs.",
            RaceControlControl.AMainOverallLine("Sam", "1234567", 2, 3, "A"));
    }

    [Fact]
    public void BugHunt24_The_flag_result_builds_the_A_Main_overall_line_through_the_unnamed_fallback()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains("var overallLine = AMainOverallLine(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var overallLine = AMainLegs.OverallCall(", source, StringComparison.Ordinal);
    }

    // --- F230: Redo on a one-round day with Q2 built beside it ---------------

    private static RoundDefinition Heat(string label, bool recorded = false, params string[] drivers) => new()
    {
        ClassName = "Stock",
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        LinkedSessionId = recorded ? Guid.NewGuid().ToString("N") : null,
        AssignedTransponders = drivers.ToList(),
    };

    private static List<string> Drivers(string letters) => letters.Select(c => c.ToString()).ToList();

    private static List<string> Sorted(IEnumerable<string> drivers) =>
        drivers.OrderBy(t => t, StringComparer.Ordinal).ToList();

    [Fact]
    public void BugHunt24_Redo_keeps_Heat_1s_drivers_out_of_Heat_2_once_Q2_has_been_built_beside_them()
    {
        var heat1 = Heat("Heat 1", recorded: true, "A", "B", "C", "D", "E", "F", "G", "H");
        var heat2 = Heat("Heat 2");
        var q2h1 = Heat("Q2 Heat 1");
        var q2h2 = Heat("Q2 Heat 2");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { heat1, heat2, q2h1, q2h2 }, Drivers("ABCDEFGHIJKLMNOP"));

        Assert.Equal(Drivers("IJKLMNOP"), Sorted(dealt[heat2.Id]));
        Assert.Equal(Drivers("ABCDEFGHIJKLMNOP"), Sorted(dealt[q2h1.Id].Concat(dealt[q2h2.Id])));
        Assert.False(dealt.ContainsKey(heat1.Id));
    }

    [Fact]
    public void BugHunt24_Redo_still_deals_everyone_into_a_hand_added_heat_beside_a_numbered_Q1()
    {
        var q1h1 = Heat("Q1 Heat 1", recorded: true, "A", "B", "C", "D");
        var q2h1 = Heat("Q2 Heat 1");
        var extra = Heat("Heat 5");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { q1h1, q2h1, extra }, Drivers("ABCDEFGH"));

        Assert.Equal(Drivers("ABCDEFGH"), Sorted(dealt[extra.Id]));
    }

    // --- F221: the phone's answer when a press only skipped a demo's On Air --

    [Theory]
    [InlineData("countdown")]
    [InlineData("green")]
    [InlineData("yellow")]
    [InlineData("red")]
    public void BugHunt24_A_flag_pressed_in_a_demos_On_Air_opening_is_not_answered_as_thrown(string action)
    {
        var answer = RaceControlControl.OnAirSkippedAnswer(action, skippedOnAirToDemo: true);

        Assert.NotNull(answer);
        Assert.Contains("On Air skipped", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("flag out", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("Countdown started", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("race on", answer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("countdown")]
    [InlineData("red")]
    [InlineData("stage")]
    public void BugHunt24_A_press_that_did_not_skip_On_Air_keeps_its_own_answer(string action)
    {
        Assert.Null(RaceControlControl.OnAirSkippedAnswer(action, skippedOnAirToDemo: false));
        Assert.Null(RaceControlControl.OnAirSkippedAnswer("stage", skippedOnAirToDemo: true));
    }

    [Fact]
    public void BugHunt24_Skipping_a_demos_On_Air_is_noted_for_the_phone_and_read_before_the_buttons_answer()
    {
        var onAir = Source("RaceControlControl.OnAir.cs");
        var skip = onAir[onAir.IndexOf("private bool SkipOnAirToTheDemo(", StringComparison.Ordinal)..];
        skip = skip[..skip.IndexOf("return true;", StringComparison.Ordinal)];

        Assert.Contains("_remoteSkippedOnAirToDemo = true;", skip, StringComparison.Ordinal);

        var main = Source("RaceControlControl.cs");
        var answerAt = main.IndexOf("OnAirSkippedAnswer(action, _remoteSkippedOnAirToDemo)", StringComparison.Ordinal);
        var buttonsAt = main.IndexOf("\"red\" => \"Red flag out.\"", StringComparison.Ordinal);

        Assert.True(answerAt > 0, "RunRemoteCommand does not read the On Air skip");
        Assert.True(buttonsAt > answerAt, "the On Air skip must be read before the per-button answer");
    }

    // --- F231: a shared heat's leftover name ---------------------------------

    private static RoundDefinition Shared(string label) => new()
    {
        ClassName = "Stock",
        SharedClasses = new List<string> { "Mod" },
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
    };

    [Fact]
    public void BugHunt24_A_shared_heats_derived_name_is_the_one_MainForm_writes()
    {
        var heat = Shared("Q1 Heat 1");

        Assert.Equal("Stock + Mod - Q1 Heat 1", RaceControlControl.DerivedRoundName(heat));
        Assert.Equal($"{MixedHeats.ClassLabel(heat)} - {heat.RoundLabel}", RaceControlControl.DerivedRoundName(heat));
    }

    [Fact]
    public void BugHunt24_A_shared_heats_leftover_name_is_not_filed_for_the_next_heat()
    {
        var heat1 = Shared("Q1 Heat 1");
        var heat2 = Shared("Q1 Heat 2");

        var label = RaceControlControl.TypedLabelFor("Stock + Mod - Q1 Heat 1", heat2, new[] { heat1, heat2 });

        Assert.Equal("Stock + Mod - Q1 Heat 2", label);
    }

    [Fact]
    public void BugHunt24_A_one_class_heat_and_a_name_the_director_typed_are_unchanged()
    {
        var heat1 = Heat("Q1 Heat 1");
        var heat2 = Heat("Q1 Heat 2");
        var all = new[] { heat1, heat2 };

        Assert.Equal("Stock - Q1 Heat 2", RaceControlControl.TypedLabelFor("Stock - Q1 Heat 1", heat2, all));
        Assert.Equal("Grudge match", RaceControlControl.TypedLabelFor("Grudge match", heat2, all));
    }

    // --- F225: a penalty credit's words --------------------------------------

    private static RaceControlSession Racing()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        return race;
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

    [Fact]
    public void BugHunt24_A_credit_larger_than_the_penalty_is_named_for_what_came_off()
    {
        var race = Racing();
        Cross(race, "X", 4_000);
        Assert.True(race.AdjustPenaltyTime("X", 5_000));

        var edit = race.EditStanding("X", () => race.AdjustPenaltyTime("X", -8_000));

        Assert.NotNull(edit);
        var applied = edit!.PenaltyAfter - edit.PenaltyBefore;

        Assert.Equal(-5_000, applied);
        Assert.Equal("5s credit", RaceControlControl.CorrectionWords(0, applied));
        Assert.Contains("Only 5s", RaceControlControl.CreditShortfallWords(-8_000, applied, "Sam"), StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_A_credit_on_a_driver_with_no_penalty_says_nothing_was_changed()
    {
        var race = Racing();
        Cross(race, "X", 4_000);

        var edit = race.EditStanding("X", () => race.AdjustPenaltyTime("X", -3_000));

        Assert.Null(edit);
        Assert.Equal(
            "Sam has no penalty time to take back - nothing was changed.",
            RaceControlControl.CreditShortfallWords(-3_000, 0, "Sam"));
    }

    [Fact]
    public void BugHunt24_Penalties_and_full_credits_keep_their_words()
    {
        Assert.Equal("5s penalty", RaceControlControl.CorrectionWords(0, 5_000));
        Assert.Equal("3s credit", RaceControlControl.CorrectionWords(0, -3_000));
        Assert.Equal("Add a lap", RaceControlControl.CorrectionWords(1, 0));
        Assert.Null(RaceControlControl.CreditShortfallWords(-3_000, -3_000, "Sam"));
        Assert.Null(RaceControlControl.CreditShortfallWords(5_000, 5_000, "Sam"));
    }

    [Fact]
    public void BugHunt24_The_penalty_dialog_does_not_promise_time_off_it_cannot_give()
    {
        var hint = RaceControlControl.TimePenaltyForm.TimePenaltyHint;

        Assert.DoesNotContain("takes time off", hint, StringComparison.Ordinal);
        Assert.Contains("penalty time", hint, StringComparison.Ordinal);
    }

    // --- F216: the achievements segment under "Lap times only" ---------------

    [Fact]
    public void BugHunt24_No_achievements_segment_under_lap_times_only()
    {
        var settings = new AppSettings { RecapBadgeHonours = true };

        Assert.True(RaceControlControl.HonoursSegmentAllowed(settings));

        settings.LapTimesOnly = new LapTimesOnlyPutBack();

        Assert.False(RaceControlControl.HonoursSegmentAllowed(settings));
        Assert.True(settings.RecapBadgeHonours);

        settings.LapTimesOnly = null;

        Assert.True(RaceControlControl.HonoursSegmentAllowed(settings));
    }

    [Fact]
    public void BugHunt24_Planning_the_achievements_segment_asks_about_lap_times_only()
    {
        var source = Source("RaceControlControl.AfterRace.cs");
        var plan = source[source.IndexOf("private HonoursPlan? PlanHonours(", StringComparison.Ordinal)..];
        plan = plan[..plan.IndexOf("Commentary?.Invoke()", StringComparison.Ordinal)];

        Assert.Contains("HonoursSegmentAllowed(_settings)", plan, StringComparison.Ordinal);
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

            throw new DirectoryNotFoundException("Couldn't find the repo root above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");
}
