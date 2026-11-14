namespace HeatCaller.Tests;

/// <summary>
/// The warm-up flag (14 Nov 2026): a warm-up is a track flag of its own,
/// FlagState.WarmUp, out while the warm-up is open and gone - with the flag
/// it went out over back - when it ends. It has a button on the flag row,
/// before Stage, and a "Warm-up first" switch at the foot of the board makes
/// every start open one. Warm-up, then staging, then the countdown.
/// </summary>
public sealed class WarmUpFlagTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    [Fact]
    public void The_warm_up_flag_is_the_last_flag_so_old_journals_read_back_the_same()
    {
        // The journal stores the flag as a number.
        Assert.Equal(5, (int)FlagState.WarmUp);
        Assert.Equal(FlagState.WarmUp, Enum.GetValues<FlagState>().Last());
        Assert.Equal(4, (int)FlagState.Checkered);
    }

    [Fact]
    public void Opening_a_warm_up_puts_the_warm_up_flag_out()
    {
        var race = new RaceControlSession();
        var flags = new List<FlagState>();
        race.FlagChanged += flags.Add;

        Assert.True(race.BeginWarmUp(TimeSpan.FromMinutes(2)));

        Assert.Equal(FlagState.WarmUp, race.Flag);
        Assert.Equal(RaceState.Idle, race.State);
        Assert.Equal(new[] { FlagState.WarmUp }, flags);
    }

    [Fact]
    public void Ending_it_puts_back_the_flag_it_went_out_over()
    {
        var race = new RaceControlSession();
        race.BeginWarmUp(TimeSpan.FromMinutes(2));

        race.EndWarmUp();

        Assert.False(race.IsWarmingUp);
        Assert.Equal(FlagState.None, race.Flag);
    }

    [Fact]
    public void After_a_race_the_checkered_comes_back_when_the_warm_up_ends()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        race.CheckeredFlag();
        Assert.Equal(FlagState.Checkered, race.Flag);

        // A checkered with no car to take it may leave the race finishing
        // rather than finished; the warm-up is only for between races.
        if (race.State != RaceState.Finished)
        {
            return;
        }

        Assert.True(race.BeginWarmUp(TimeSpan.FromMinutes(2)));
        Assert.Equal(FlagState.WarmUp, race.Flag);

        race.EndWarmUp();
        Assert.Equal(FlagState.Checkered, race.Flag);
    }

    [Fact]
    public void Staging_ends_the_warm_up_and_its_flag()
    {
        var race = new RaceControlSession();
        race.BeginWarmUp(TimeSpan.FromMinutes(2));

        race.Stage();

        Assert.False(race.IsWarmingUp);
        Assert.Equal(RaceState.Staging, race.State);
        Assert.Equal(FlagState.None, race.Flag);
    }

    [Fact]
    public void A_countdown_or_green_pressed_early_ends_it_too()
    {
        var countdown = new RaceControlSession();
        countdown.BeginWarmUp(TimeSpan.FromMinutes(2));
        countdown.StartCountdown();
        Assert.False(countdown.IsWarmingUp);
        Assert.NotEqual(FlagState.WarmUp, countdown.Flag);

        var green = new RaceControlSession();
        green.BeginWarmUp(TimeSpan.FromMinutes(2));
        green.GreenFlag(100_000);
        Assert.False(green.IsWarmingUp);
        Assert.Equal(FlagState.Green, green.Flag);
    }

    [Fact]
    public void Reset_ends_it_with_no_flag_out()
    {
        var race = new RaceControlSession();
        race.BeginWarmUp(TimeSpan.FromMinutes(2));

        race.Reset();

        Assert.False(race.IsWarmingUp);
        Assert.Equal(FlagState.None, race.Flag);
    }

    [Fact]
    public void A_snapshot_never_keeps_the_warm_up_flag()
    {
        // Undo and the journal do not bring a warm-up back - its timer is
        // Race Control's - so they keep the flag it went out over instead.
        var race = new RaceControlSession();
        race.BeginWarmUp(TimeSpan.FromMinutes(2));

        var snapshot = race.CreateSnapshot();
        Assert.Equal(FlagState.None, snapshot.Flag);

        race.RestoreSnapshot(snapshot with { Flag = FlagState.WarmUp });
        Assert.False(race.IsWarmingUp);
        Assert.Equal(FlagState.None, race.Flag);
    }

    [Fact]
    public void No_warm_up_over_a_race_that_is_staged()
    {
        var race = new RaceControlSession();
        race.Stage();

        Assert.False(race.BeginWarmUp(TimeSpan.FromMinutes(2)));
        Assert.Equal(FlagState.None, race.Flag);
    }

    [Fact]
    public void The_flag_reads_Warm_up_with_its_hyphen()
    {
        Assert.Equal("Warm-up", FlagText.Name(FlagState.WarmUp));
        Assert.Equal("WARM-UP FLAG", FlagText.Shouted(FlagState.WarmUp));
        Assert.Equal("Green", FlagText.Name(FlagState.Green));
        Assert.Equal("CHECKERED FLAG", FlagText.Shouted(FlagState.Checkered));
    }

    [Fact]
    public void The_warm_up_button_leads_the_row_before_the_green()
    {
        foreach (var state in new[] { RaceState.Idle, RaceState.Staging, RaceState.Countdown })
        {
            var shown = RaceBar.ButtonsFor(state, FlagState.None, practice: false, warmingUp: false);
            Assert.True(shown.HasFlag(RaceBarButtons.WarmUp), $"Warm-up at {state}");
        }

        // While it is open, so pressing it again can skip the rest.
        Assert.True(RaceBar.ButtonsFor(RaceState.Finished, FlagState.WarmUp, false, warmingUp: true).HasFlag(RaceBarButtons.WarmUp));
        Assert.Equal(RacePhase.WarmUp, RaceBar.PhaseFor(RaceState.Finished, FlagState.WarmUp, false, warmingUp: true));

        // Not once the race is green, nor through practice.
        Assert.False(RaceBar.ButtonsFor(RaceState.Racing, FlagState.Green, false, false).HasFlag(RaceBarButtons.WarmUp));
        Assert.Equal(RaceBarButtons.None, RaceBar.ButtonsFor(RaceState.Idle, FlagState.None, practice: true, warmingUp: false));

        // Leftmost on the row, before Stage.
        Assert.Contains("_btnWarmUp, _btnStage, _btnCountdown", Source("RaceControlControl.Layout.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(90, false, 90)]
    [InlineData(90, true, 90)]
    [InlineData(0, false, 0)]
    [InlineData(0, true, RaceControlControl.WarmUpWhenFormatHasNone)]
    public void A_warm_up_asked_for_with_none_in_the_format_runs_two_minutes(int formatSeconds, bool wanted, int expected)
    {
        Assert.Equal(120, RaceControlControl.WarmUpWhenFormatHasNone);
        Assert.Equal(expected, RaceControlControl.WarmUpSecondsFor(formatSeconds, wanted));
    }

    [Fact]
    public void Never_past_the_longest_warm_up_a_format_allows()
    {
        Assert.Equal(RaceFormat.MaxWarmUpSeconds, RaceControlControl.WarmUpSecondsFor(RaceFormat.MaxWarmUpSeconds + 600, wanted: true));
    }

    [Fact]
    public void Warm_up_first_is_off_by_default_and_kept_with_the_settings()
    {
        Assert.False(new AppSettings().RaceStartWithWarmUp);
        Assert.Contains("nameof(AppSettings.RaceStartWithWarmUp)", Source("SettingsTable.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_footer_has_Warm_up_first_beside_Start_races()
    {
        var layout = Source("RaceControlControl.Layout.cs");
        var onAir = Source("RaceControlControl.OnAir.cs");

        Assert.Contains("SimpleMode.Hide(_chkStartWithOnAir), SimpleMode.Hide(_swWarmUpFirst), _swAutoStart", layout, StringComparison.Ordinal);
        Assert.Contains("new(\"Warm-up first:\", \"Off\", \"On\")", onAir, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stage_at_the_end_of_a_warm_up_runs_on_into_the_countdown()
    {
        // Warm-up, staging, countdown - whatever the format's auto-advance.
        var layout = Source("RaceControlControl.Layout.cs");

        Assert.Contains("var afterWarmUp = _warmUpJustFinished;", layout, StringComparison.Ordinal);
        Assert.Contains("if (_fmt.AutoAdvance || afterWarmUp)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_starts_next_race_in_counts_on_past_the_warm_up_to_the_green()
    {
        // The gap's clock runs out when Auto start starts the race; with a
        // warm-up first, the band, the Next Up words and the phones count on
        // to the green - and only while counting, not to a promised time.
        var autoStart = Source("RaceControlControl.AutoStart.cs");

        Assert.Contains("AutoStartClock.Band(_autoStartState, ToTheGreen(", autoStart, StringComparison.Ordinal);
        Assert.Contains("AutoStartClock.Words(_autoStartState, ToTheGreen(", autoStart, StringComparison.Ordinal);
        Assert.Contains("_autoStartState == AutoStartState.Counting && remaining is { } left ? left + StartToGreen() : remaining", autoStart, StringComparison.Ordinal);
        Assert.Contains("WarmUpSecondsFor(format.WarmUpSeconds, StartWithWarmUp), format.AutoAdvance, format.StagingSeconds, format.CountdownSeconds", autoStart, StringComparison.Ordinal);
        Assert.Contains("_autoStartState == AutoStartState.Counting ? at + StartToGreen()", Source("RaceControlControl.Marshals.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_count_to_the_green_takes_the_next_races_format_not_the_race_just_run()
    {
        // Between races the Round box still holds the race just run; the
        // count to the green is for the race Auto start is counting down.
        var autoStart = Source("RaceControlControl.AutoStart.cs");
        var body = autoStart[autoStart.IndexOf("private TimeSpan StartToGreen()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("internal static TimeSpan LeadToTheGreen", StringComparison.Ordinal)];

        Assert.Contains("var next = NextRound();", body, StringComparison.Ordinal);
        Assert.Contains("FormatFor(next, ClassFor(next),", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EffectiveFormat(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heat_and_the_main_after_it_resolve_their_own_formats()
    {
        // A Stock heat with no warm-up just ran; the A-Main next has a
        // three-minute warm-up. Each round resolves its own format.
        var stock = new RaceClass
        {
            Name = "Stock",
            QualifierFormat = new RaceFormat { WarmUpSeconds = 0, AutoAdvance = false },
            Format = new RaceFormat { WarmUpSeconds = 180, AutoAdvance = true },
        };
        var heat = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };
        var main = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        var appDefault = new RaceFormat { WarmUpSeconds = 60 };

        Assert.Same(stock.QualifierFormat, RaceControlControl.FormatFor(heat, stock, () => appDefault, out var heatSource));
        Assert.Equal("Stock qualifiers", heatSource);
        Assert.Same(stock.Format, RaceControlControl.FormatFor(main, stock, () => appDefault, out var mainSource));
        Assert.Equal("Stock class", mainSource);

        var overridden = new RaceFormat { WarmUpSeconds = 30 };
        var heat2 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 2", Kind = RoundKind.Qualifier, FormatOverride = overridden };
        Assert.Same(overridden, RaceControlControl.FormatFor(heat2, stock, () => appDefault, out var overrideSource));
        Assert.Equal("Heat 2 override", overrideSource);

        Assert.Same(appDefault, RaceControlControl.FormatFor(main, null, () => appDefault, out var defaultSource));
        Assert.Equal("app default", defaultSource);
        Assert.Same(appDefault, RaceControlControl.FormatFor(null, null, () => appDefault, out _));
    }

    [Theory]
    // Auto-advance: warm-up, staging and countdown all run by themselves.
    [InlineData(120, true, 15, 5, 140)]
    [InlineData(0, true, 15, 5, 20)]
    // No auto-advance: a warm-up still stages and counts down by itself,
    // but the green waits for its button - so the clock stops at the countdown.
    [InlineData(120, false, 15, 5, 135)]
    // No auto-advance, no warm-up: Start only stages; the rest is by hand.
    [InlineData(0, false, 15, 5, 0)]
    // Negative lengths from an old or hand-edited format count as none.
    [InlineData(-5, true, -1, -1, 0)]
    public void The_count_to_the_green_takes_in_only_the_steps_that_come_by_themselves(int warmUp, bool autoAdvance, int staging, int countdown, int expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), RaceControlControl.LeadToTheGreen(warmUp, autoAdvance, staging, countdown));
    }
}
