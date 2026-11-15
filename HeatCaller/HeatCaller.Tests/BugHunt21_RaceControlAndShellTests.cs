namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21 (15 Nov 2026): the PA's countdown and the night plan counting
/// to Auto start's Start press while every screen counted to the green
/// (#22); a startup warm-up step taking the keyboard focus (#34); and the
/// sidebar slide running in Race Mode, where the sidebar is hidden (#35).
/// </summary>
public sealed class BugHunt21_RaceControlAndShellTests
{
    private static readonly DateTime Start = new(2026, 11, 15, 20, 40, 0, DateTimeKind.Utc);

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    [Fact]
    public void A_counting_gap_is_due_at_the_green_not_at_the_start_press()
    {
        var lead = RaceControlControl.LeadToTheGreen(warmUpSeconds: 60, autoAdvance: true, stagingSeconds: 30, countdownSeconds: 10);

        Assert.Equal(Start.AddSeconds(100), RaceControlControl.GreenAt(Start, AutoStartState.Counting, () => lead));
    }

    [Fact]
    public void A_race_waiting_for_its_given_time_keeps_that_time()
    {
        var asked = false;

        var at = RaceControlControl.GreenAt(Start, AutoStartState.WaitingForTime, () =>
        {
            asked = true;
            return TimeSpan.FromMinutes(1);
        });

        Assert.Equal(Start, at);
        Assert.False(asked);
    }

    [Fact]
    public void The_PA_and_the_night_plan_read_the_green()
    {
        var between = Source("RaceControlControl.BetweenRaces.cs");
        var due = between.Substring(between.IndexOf("private DateTime? DueFor(", StringComparison.Ordinal), 600);

        Assert.Contains("if (AutoGreenAtUtc(round.Id, now) is { } autoStart)", due);
        Assert.DoesNotContain("AutoStartsAtUtc(", due);

        var plan = Source("RaceControlControl.NextStart.cs");
        Assert.Contains("new NextStartFact(id, AutoGreenAtUtc(id, now), Gap.PushedBackMinutes)", plan);

        Assert.Contains(
            "AutoStartsAtUtc(roundId, now) is { } start ? GreenAt(start, _autoStartState, StartToGreen) : null",
            Source("RaceControlControl.AutoStart.cs"));
    }

    [Fact]
    public void Focus_is_put_back_only_to_a_control_that_can_take_it()
    {
        Assert.False(MainForm.FocusWentAway(null, null));

        // Never shown: it cannot take the focus, so it is left alone.
        using var hidden = new TextBox();
        Assert.False(MainForm.FocusWentAway(hidden, null));

        var gone = new TextBox();
        gone.Dispose();
        Assert.False(MainForm.FocusWentAway(gone, null));
    }

    [Fact]
    public void Nothing_focused_is_nothing_to_put_back()
    {
        using var form = new Form();
        Assert.Null(MainForm.DeepestFocus(form));
    }

    [Fact]
    public void Every_warm_up_step_puts_the_focus_back()
    {
        var nav = Source("MainForm.Navigation.cs");
        var before = nav.IndexOf("var focused = DeepestFocus(this);", StringComparison.Ordinal);
        var begin = nav.IndexOf("NoRedraw.Begin(pnlContent);", before, StringComparison.Ordinal);
        var after = nav.IndexOf("if (FocusWentAway(focused, DeepestFocus(this)))", begin, StringComparison.Ordinal);
        var end = nav.IndexOf("NoRedraw.End(pnlContent);", begin, StringComparison.Ordinal);

        Assert.True(before > 0 && begin > before);
        Assert.True(after > begin && end > after);
        Assert.Contains("focused!.Focus();", nav.Substring(after, end - after));
    }

    [Fact]
    public void The_sidebar_does_not_slide_while_it_is_hidden()
    {
        var slide = Source("MainForm.NavSlide.cs");
        var method = slide.Substring(slide.IndexOf("internal void SlideNavIconsOnly(", StringComparison.Ordinal), 900);

        Assert.Contains("|| !pnlSidebar.Visible)", method);
        Assert.True(method.IndexOf("!pnlSidebar.Visible", StringComparison.Ordinal) < method.IndexOf("SetNavIconsOnly(iconsOnly);", StringComparison.Ordinal));
    }
}
