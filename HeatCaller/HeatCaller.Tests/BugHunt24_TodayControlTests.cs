using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race Day's Next Steps page (TodayControl):
/// the Get back on time card's minutes and keyboard, and the go/no-go
/// list's phones row.
/// </summary>
public sealed class BugHunt24_TodayControlTests
{
    private static RunSheet.Choice Shorter(int minutes) =>
        new(NightPlan.FixKind.ShorterQualifiers, "Qualifiers 1 min shorter", TimeSpan.FromMinutes(minutes), null);

    private static RunSheet.Choice Drop(int minutes) =>
        new(NightPlan.FixKind.DropRound, "Drop Q3 (4 races)", TimeSpan.FromMinutes(minutes), 3);

    private static RunSheet.Choice Fewer(int minutes) =>
        new(NightPlan.FixKind.OneMainFewer, "One main fewer", TimeSpan.FromMinutes(minutes), null);

    private static void Key(FixList list, Keys key) =>
        typeof(FixList).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(list, new object[] { new KeyEventArgs(key) });

    // --- F317: the same fixes with new minutes ---

    [Fact]
    public void BugHunt24_Same_fixes_with_new_minutes_are_handed_over_and_keep_their_ticks()
    {
        var shown = new List<RunSheet.Choice> { Drop(31), Shorter(12) };

        Assert.Equal(TodayControl.FixesChange.Same, TodayControl.FixesChangeOf(shown, new List<RunSheet.Choice> { Drop(31), Shorter(12) }));
        Assert.Equal(TodayControl.FixesChange.Figures, TodayControl.FixesChangeOf(shown, new List<RunSheet.Choice> { Drop(31), Shorter(6) }));
        Assert.Equal(TodayControl.FixesChange.New, TodayControl.FixesChangeOf(shown, new List<RunSheet.Choice> { Shorter(12) }));
        Assert.Equal(TodayControl.FixesChange.New, TodayControl.FixesChangeOf(shown, new List<RunSheet.Choice> { Shorter(40), Drop(31) }));
    }

    [Fact]
    public void BugHunt24_A_ticked_fix_follows_its_new_saving()
    {
        var seen = StaThread.Run(() =>
        {
            using var list = new FixList();
            list.SetChoices(new List<RunSheet.Choice> { Drop(31), Shorter(12) });

            Key(list, Keys.Down);
            Key(list, Keys.Space);
            var before = list.Ticked.Select(c => c.Saves).ToList();

            list.UpdateChoices(new List<RunSheet.Choice> { Drop(31), Shorter(6) });

            return (before, after: list.Ticked.ToList());
        }, TimeSpan.FromSeconds(30), () => "ticking a fix and updating its minutes");

        Assert.Equal(new[] { TimeSpan.FromMinutes(12) }, seen.before);
        var ticked = Assert.Single(seen.after);
        Assert.Equal(NightPlan.FixKind.ShorterQualifiers, ticked.Kind);
        Assert.Equal(TimeSpan.FromMinutes(6), ticked.Saves);
        Assert.Equal(TimeSpan.FromMinutes(6), RunSheet.Saving(seen.after));
    }

    // --- F319: ticking any fix from the keyboard ---

    [Fact]
    public void BugHunt24_Down_then_Space_ticks_the_second_fix_only()
    {
        var seen = StaThread.Run(() =>
        {
            using var list = new FixList();
            list.SetChoices(new List<RunSheet.Choice> { Drop(31), Shorter(12), Fewer(8) });

            Key(list, Keys.Down);
            Key(list, Keys.Space);
            var second = list.Ticked.ToList();

            // Down past the last row stays on it; Space there ticks the third.
            Key(list, Keys.Down);
            Key(list, Keys.Down);
            Key(list, Keys.Down);
            Key(list, Keys.Space);
            var third = list.Ticked.ToList();

            return (second, third);
        }, TimeSpan.FromSeconds(30), () => "moving through the fixes by keyboard");

        Assert.Equal(NightPlan.FixKind.ShorterQualifiers, Assert.Single(seen.second).Kind);
        Assert.Equal(new[] { NightPlan.FixKind.ShorterQualifiers, NightPlan.FixKind.OneMainFewer }, seen.third.Select(c => c.Kind));
    }

    [Fact]
    public void BugHunt24_The_keyboard_row_stays_within_the_list()
    {
        Assert.Equal(1, FixList.NextFocus(0, 3, Keys.Down));
        Assert.Equal(2, FixList.NextFocus(2, 3, Keys.Down));
        Assert.Equal(0, FixList.NextFocus(0, 3, Keys.Up));
        Assert.Equal(1, FixList.NextFocus(2, 3, Keys.Up));
        Assert.Equal(1, FixList.NextFocus(1, 3, Keys.Space));
        Assert.Equal(0, FixList.NextFocus(4, 0, Keys.Down));
    }

    // --- F318: the phones row when the relay has just dropped ---

    [Fact]
    public void BugHunt24_Phones_row_says_offline_for_a_relay_that_is_on_but_just_dropped()
    {
        var justDropped = MissionControl.Website(new RelayStatus(true, true, false, DateTime.UtcNow.AddMinutes(-1), 0, 0, "Timed out."));
        var row = TodayControl.PhonesRow(justDropped);

        Assert.Equal(MissionControl.Level.Off, row.Level);
        Assert.Equal(RunSheet.Mark.None, row.Mark);
        Assert.Equal(" · offline", row.Rest);
    }

    [Fact]
    public void BugHunt24_Phones_row_still_says_off_when_publishing_is_off_and_live_when_online()
    {
        Assert.Equal(" · off", TodayControl.PhonesRow(MissionControl.Website(null)).Rest);
        Assert.Equal(" · off", TodayControl.PhonesRow(MissionControl.Website(RelayStatus.Off)).Rest);
        Assert.Equal(" · relay live", TodayControl.PhonesRow(MissionControl.Website(new RelayStatus(true, true, true, DateTime.UtcNow, 0, 0, null))).Rest);

        var behind = TodayControl.PhonesRow(MissionControl.Website(new RelayStatus(true, true, false, DateTime.UtcNow.AddMinutes(-10), 3, 0, null)));
        Assert.Equal(RunSheet.Mark.Triangle, behind.Mark);
        Assert.Equal(" · offline", behind.Rest);
    }
}
