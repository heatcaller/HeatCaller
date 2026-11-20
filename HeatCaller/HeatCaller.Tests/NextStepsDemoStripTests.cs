namespace HeatCaller.Tests;

/// <summary>
/// 20 Nov 2026, head-to-head baseline: the strip above Next Steps' Start
/// button on a demo night. It said "Timer not connected. Laps won't be
/// counted." over demo races whose laps were counted, and said nothing when
/// a demo could not move the night on because "Let a demo fill in the race
/// program" was off.
/// </summary>
public sealed class NextStepsDemoStripTests
{
    [Fact]
    public void With_no_timer_and_no_demos_filing_it_still_says_laps_will_not_count()
    {
        var strip = TodayControl.BlockerFor(timerDown: true, demoFillsTheProgram: false, demoOfThisRaceNotFiled: false);

        Assert.Equal((TodayControl.TimerDownWords, TodayControl.ConnectTimerCaption, false), strip);
    }

    [Fact]
    public void On_a_demo_night_it_says_demo_laps_only_rather_than_that_laps_are_not_counted()
    {
        var strip = TodayControl.BlockerFor(timerDown: true, demoFillsTheProgram: true, demoOfThisRaceNotFiled: false);

        Assert.NotNull(strip);
        Assert.Equal(TodayControl.DemoLapsOnlyWords, strip!.Value.Text);
        Assert.DoesNotContain("won't be counted", strip.Value.Text);
        Assert.Equal(TodayControl.ConnectTimerCaption, strip.Value.Button);
    }

    [Fact]
    public void A_demo_that_was_not_filed_names_the_setting_and_opens_Demo_Mode()
    {
        var strip = TodayControl.BlockerFor(timerDown: true, demoFillsTheProgram: false, demoOfThisRaceNotFiled: true);

        Assert.NotNull(strip);
        Assert.Contains("\"Let a demo fill in the race program\"", strip!.Value.Text);
        Assert.Contains("Demo Mode", strip.Value.Text);
        Assert.Equal(TodayControl.OpenDemoModeCaption, strip.Value.Button);
        Assert.True(strip.Value.OpensDemoMode);

        // Said with the timer connected too: it is about the program, not the timer.
        Assert.Equal(strip, TodayControl.BlockerFor(timerDown: false, demoFillsTheProgram: false, demoOfThisRaceNotFiled: true));
    }

    [Fact]
    public void With_the_timer_connected_and_nothing_to_say_there_is_no_strip() =>
        Assert.Null(TodayControl.BlockerFor(timerDown: false, demoFillsTheProgram: true, demoOfThisRaceNotFiled: false));

    [Fact]
    public void The_card_s_strip_button_says_where_it_goes() => StaThread.Run(() =>
    {
        // That the words fit at every page size is LayoutTests.NextStepsKeepsTheProgramsButtonsInViewAtEverySize's.
        using var card = new NowCard(() => (60, 20));

        card.ShowRace("Stock Buggy", "Q1 Heat 1", string.Empty, new List<(string, string)>(), TodayControl.DemoNotFiledWords, "Start Q1 Heat 1",
            TodayControl.OpenDemoModeCaption);

        var button = (Button)typeof(NowCard).GetField("_btnConnect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(card)!;
        Assert.Equal(TodayControl.OpenDemoModeCaption, button.Text);

        // Back on a race with no timer: the usual button again.
        card.ShowRace("Stock Buggy", "Q1 Heat 1", string.Empty, new List<(string, string)>(), TodayControl.TimerDownWords, "Start Q1 Heat 1");
        Assert.Equal(TodayControl.ConnectTimerCaption, button.Text);

        return 0;
    }, TimeSpan.FromMinutes(1), () => "the card");
}
