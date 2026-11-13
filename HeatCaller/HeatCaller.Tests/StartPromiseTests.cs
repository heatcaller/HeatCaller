namespace HeatCaller.Tests;

/// <summary>
/// Setup plan step 8 (12 Nov 2026): never more than 2 minutes early - the
/// promised start on each round (StartPromise), Auto start waiting for it
/// (AutoStartClock.NotBefore, "waiting for its time", NEXT RACE AT 20:40),
/// the estimates that show the promise when the night is early, the keys
/// for Hold and Start now, and the Big Screen's up-next line with its times.
/// </summary>
public sealed class StartPromiseTests
{
    private static readonly DateTime Flag = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<string> Nobody = Array.Empty<string>();

    private static DateTime At(double seconds) => Flag.AddSeconds(seconds);

    private static RoundDefinition Heat(string label, bool run = false, DateTime? promised = null) => new()
    {
        RoundLabel = label,
        ClassName = "Stock",
        Kind = RoundKind.Qualifier,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
        PromisedStartUtc = promised,
    };

    // --- The promise ---------------------------------------------------------

    [Fact]
    public void Stamping_promises_each_race_to_run_its_start_to_the_minute()
    {
        var ran = Heat("Q1 Heat 1", run: true);
        var next = Heat("Q1 Heat 2");
        var practice = new RoundDefinition { Kind = RoundKind.Practice, RoundLabel = "Practice" };
        var noEstimate = Heat("Q1 Heat 3");
        var starts = new Dictionary<string, DateTime> { [next.Id] = At(41 * 60 + 40), [ran.Id] = At(0), [practice.Id] = At(0) };

        var changed = StartPromise.Stamp(new[] { ran, next, practice, noEstimate }, id => starts.TryGetValue(id, out var at) ? at : null, overwrite: true);

        Assert.Equal(new[] { next }, changed);
        Assert.Equal(At(42 * 60), next.PromisedStartUtc);
        Assert.Null(ran.PromisedStartUtc);
        Assert.Null(practice.PromisedStartUtc);
    }

    [Fact]
    public void Without_overwrite_a_promise_made_is_kept_and_the_race_going_out_is_skipped()
    {
        var kept = Heat("Q1 Heat 2", promised: At(600));
        var goingOut = Heat("Q1 Heat 1");

        var changed = StartPromise.Stamp(new[] { goingOut, kept }, _ => At(60), overwrite: false, skipRoundId: goingOut.Id);

        Assert.Empty(changed);
        Assert.Equal(At(600), kept.PromisedStartUtc);
        Assert.Null(goingOut.PromisedStartUtc);
    }

    [Fact]
    public void A_night_that_does_not_post_promises_at_its_first_race_only()
    {
        var day = new RaceEvent();
        Assert.True(StartPromise.OwedAtFirstRace(day, new[] { Heat("Q1 Heat 1") }));
        Assert.False(StartPromise.OwedAtFirstRace(day, new[] { Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2") }));
        Assert.False(StartPromise.OwedAtFirstRace(day, new[] { Heat("Q1 Heat 1", promised: At(0)) }));
        Assert.False(StartPromise.OwedAtFirstRace(new RaceEvent { HeatsPostedUtc = At(0) }, new[] { Heat("Q1 Heat 1") }));
    }

    [Fact]
    public void Not_before_is_two_minutes_ahead_of_the_promise_and_only_with_the_setting_on()
    {
        var heat = Heat("Q1 Heat 2", promised: At(600));

        Assert.Equal(At(480), StartPromise.NotBefore(heat, neverEarly: true));
        Assert.Null(StartPromise.NotBefore(heat, neverEarly: false));
        Assert.Null(StartPromise.NotBefore(Heat("Q1 Heat 3"), neverEarly: true));
    }

    [Fact]
    public void The_setting_is_on_by_default_and_on_the_auto_start_menu()
    {
        Assert.True(new AppSettings().AutoStartNeverEarly);
        Assert.Contains(nameof(AppSettings.AutoStartNeverEarly), Source("SettingsTable.cs"), StringComparison.Ordinal);
        Assert.Contains("Never start a race more than 2 minutes before the time drivers were given", Source("RaceControlControl.AutoStart.cs"), StringComparison.Ordinal);
    }

    // --- Auto start waits ---------------------------------------------------

    [Fact]
    public void An_early_night_waits_for_its_time_and_then_starts()
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, TimeSpan.FromSeconds(150));

        // Promised 19:10, so not before 19:08 - well after the 2:30 gap.
        clock.NotBefore(At(480));

        Assert.Equal(AutoStartState.WaitingForTime, clock.Step(At(10), false, Nobody));
        Assert.Equal(At(480), clock.StartsAtUtc(At(10)));
        Assert.Equal(TimeSpan.FromSeconds(330), clock.WaitForTime(At(10)));

        // The gap alone is up, but the race still waits.
        Assert.Equal(AutoStartState.WaitingForTime, clock.Step(At(200), false, Nobody));

        Assert.Equal(AutoStartState.Start, clock.Step(At(480), false, Nobody));
    }

    [Fact]
    public void Start_now_overrides_the_wait()
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, TimeSpan.FromSeconds(150));
        clock.NotBefore(At(480));

        clock.StartNow();

        Assert.Equal(TimeSpan.Zero, clock.WaitForTime(At(30)));
        Assert.Equal(AutoStartState.Start, clock.Step(At(30), false, Nobody));
    }

    [Fact]
    public void A_late_night_counts_the_gap_as_always()
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, TimeSpan.FromSeconds(150));
        clock.NotBefore(At(60));

        Assert.Equal(AutoStartState.Counting, clock.Step(At(10), false, Nobody));
        Assert.Equal(At(150), clock.StartsAtUtc(At(10)));
        Assert.Equal(AutoStartState.Start, clock.Step(At(150), false, Nobody));
    }

    [Fact]
    public void A_new_gap_forgets_the_last_races_limit()
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, TimeSpan.FromSeconds(150));
        clock.NotBefore(At(480));
        clock.Open("8", "heat3", At(600), TimeSpan.FromSeconds(150));

        Assert.Null(clock.NotBeforeUtc);
    }

    [Fact]
    public void Waiting_for_its_time_says_the_time_on_the_bar_and_the_big_screen()
    {
        var at = At(480);
        var time = AppTime.TimeOfDay(at.ToLocalTime());

        Assert.Equal($"NEXT RACE AT {time}", AutoStartClock.Band(AutoStartState.WaitingForTime, TimeSpan.FromMinutes(4), at));
        Assert.Equal("NEXT RACE IN 4:00", AutoStartClock.Band(AutoStartState.WaitingForTime, TimeSpan.FromMinutes(4)));
        Assert.StartsWith($"waiting for its time - starts at {time} (in 4:00)",
            AutoStartClock.Words(AutoStartState.WaitingForTime, TimeSpan.FromMinutes(4), Nobody, Nobody, at));
    }

    // --- The estimates ------------------------------------------------------

    [Fact]
    public void An_early_night_shows_the_promise_and_the_races_after_wait_too()
    {
        var now = Flag;
        var next = Heat("Q1 Heat 2", promised: now.AddMinutes(20));
        var after = Heat("Q1 Heat 3");
        var five = TimeSpan.FromMinutes(5);

        var held = NightPlan.For(new[] { next, after }, _ => five, null, null, null, null, null, now, keepPromises: true)!;

        // Waits until 18 minutes (two early), shows the promise itself.
        Assert.Equal(now.AddMinutes(18), held.StartOf(next.Id));
        Assert.Equal(now.AddMinutes(20), held.ShownStartOf(next.Id));
        Assert.Equal(now.AddMinutes(18) + five + NightPlan.DefaultGap, held.StartOf(after.Id));
        Assert.Equal(NightPlan.About(now.AddMinutes(20)), NightPlan.AboutOf(held)(next.Id));

        // Without holding to promises (Auto start's setting off), the plan
        // runs early - but the phones still never say earlier than promised.
        var loose = NightPlan.For(new[] { next, after }, _ => five, null, null, null, null, null, now)!;
        Assert.Equal(now, loose.StartOf(next.Id));
        Assert.Equal(NightPlan.About(now.AddMinutes(20)), NightPlan.AboutOf(loose)(next.Id));
    }

    [Fact]
    public void A_late_night_shows_the_estimate()
    {
        Assert.Equal(At(900), StartPromise.Shown(At(900), At(600)));
        Assert.Equal(At(900), StartPromise.Shown(At(900), null));
        Assert.Equal(At(1200), StartPromise.Shown(At(900), At(1200)));
    }

    [Fact]
    public void Posting_promises_the_starts_and_undo_takes_them_back()
    {
        var posting = Source("RaceProgramControl.Posting.cs");
        Assert.Contains("PromiseStarts(dayRounds, overwrite: true)", posting, StringComparison.Ordinal);
        Assert.Contains("round.PromisedStartUtc = promisedBefore.GetValueOrDefault(round.Id);", posting, StringComparison.Ordinal);
        Assert.Contains("raceProgram.PromiseStartsAtFirstRace(_activeRound?.Id)", Source("MainForm.PaceAndStarts.cs"), StringComparison.Ordinal);
    }

    // --- The Big Screen -----------------------------------------------------

    [Fact]
    public void The_up_next_line_lists_the_races_to_come_with_their_times()
    {
        Assert.Equal("UP NEXT:  Stock Q2 Heat 3 about 20:42  •  Mod Q2 Heat 1",
            NightPlan.UpNext(new (string, string, string?)[] { ("Stock", "Q2 Heat 3", "about 20:42"), ("Mod", "Q2 Heat 1", null) }));
        Assert.Equal(string.Empty, NightPlan.UpNext(Array.Empty<(string, string, string?)>()));
    }

    [Fact]
    public void The_big_screen_is_handed_the_up_next_line()
    {
        Assert.Contains("upNext: BigScreenUpNext", Source("RaceControlControl.Layout.cs"), StringComparison.Ordinal);
        Assert.Contains("_upNext?.Invoke()", Source("BigScreenForm.cs"), StringComparison.Ordinal);
    }

    // --- The keys -----------------------------------------------------------

    [Fact]
    public void H_holds_and_N_starts_now_on_race_control()
    {
        var keys = Source("MainForm.Keyboard.cs");
        Assert.Contains("case Keys.H when !e.Shift: rc.TriggerAutoStartHold(); break;", keys, StringComparison.Ordinal);
        Assert.Contains("case Keys.N when !e.Shift: rc.TriggerAutoStartNow(); break;", keys, StringComparison.Ordinal);

        // The desk view's guard stays the first thing ProcessCmdKey does.
        var guard = keys.IndexOf("if (_deskOnly && !DeskViewAllowsKey(keyData))", StringComparison.Ordinal);
        var f11 = keys.IndexOf("if (keyData == Keys.F11)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < f11);

        var raceControl = ShortcutCatalog.Sections.Single(s => s.Title == "Race Control").Items;
        Assert.Contains(raceControl, s => s.Keys == "H");
        Assert.Contains(raceControl, s => s.Keys == "N");
        Assert.Equal(raceControl.Count, raceControl.Select(s => s.Keys).Distinct().Count());
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
