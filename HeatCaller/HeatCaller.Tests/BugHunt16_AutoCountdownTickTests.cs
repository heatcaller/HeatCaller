using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// 10 Nov 2026, Bug Hunt 16.
///
/// "Start the countdown by itself from the run sheet" is one setting that
/// more than one control can show. Each used to read it once, when it was
/// built, and a card's next unrelated edit saved its stale tick back over it,
/// so the run sheet's countdown calls stopped with nothing on screen to say so.
///
/// The Timetable's Reminder countdown card had the second tick until 13 Nov
/// 2026; these now drive the setting through AutoCountdownTick directly, the
/// way any other control showing it would.
/// </summary>
public sealed class BugHunt16_AutoCountdownTickTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var below in All(child))
            {
                yield return below;
            }
        }
    }

    private static CheckBox Box(Control root, string startsWith) =>
        All(root).OfType<CheckBox>().Single(b => b.Text.StartsWith(startsWith, StringComparison.Ordinal));

    [Fact]
    public void A_tick_from_elsewhere_survives_an_edit_on_the_between_races_card()
    {
        var seen = StaThread.Run(() =>
        {
            var settings = new AppSettings();
            var (card, _) = BetweenRacesCard.Build(settings, new TextSize(), null, null, null, null);
            using var cardOwner = card;

            AutoCountdownTick.Set(settings, true);
            var cardFollowed = Box(card, "Start the countdown by itself").Checked;

            // Something else on the card, which saves the whole card.
            var callUps = Box(card, "Call-up: next race");
            callUps.Checked = !callUps.Checked;

            return (cardFollowed, settings.BetweenRacesAutoCountdown);
        }, TimeSpan.FromSeconds(60), () => "building the Between races card");

        Assert.True(seen.cardFollowed);
        Assert.True(seen.BetweenRacesAutoCountdown);
    }

    [Fact]
    public void Two_cards_over_one_setting_show_the_same_tick()
    {
        var seen = StaThread.Run(() =>
        {
            var settings = new AppSettings();
            var (first, _) = BetweenRacesCard.Build(settings, new TextSize(), null, null, null, null);
            using var firstOwner = first;
            var (second, _) = BetweenRacesCard.Build(settings, new TextSize(), null, null, null, null);
            using var secondOwner = second;

            Box(first, "Start the countdown by itself").Checked = true;
            var on = (Box(second, "Start the countdown by itself").Checked, settings.BetweenRacesAutoCountdown);

            Box(first, "Start the countdown by itself").Checked = false;
            var off = (Box(second, "Start the countdown by itself").Checked, settings.BetweenRacesAutoCountdown);

            return (on, off);
        }, TimeSpan.FromSeconds(60), () => "building two Between races cards");

        Assert.Equal((true, true), seen.on);
        Assert.Equal((false, false), seen.off);
    }

    [Fact]
    public void Another_settings_tick_leaves_this_one_alone()
    {
        var seen = StaThread.Run(() =>
        {
            var mine = new AppSettings();
            var (card, _) = BetweenRacesCard.Build(mine, new TextSize(), null, null, null, null);
            using var cardOwner = card;

            AutoCountdownTick.Set(new AppSettings(), true);
            return Box(card, "Start the countdown by itself").Checked;
        }, TimeSpan.FromSeconds(60), () => "building the Between races card");

        Assert.False(seen);
    }

    /// <summary>13 Nov 2026: the Timetable has no countdown of its own any more - no Start, no tick.</summary>
    [Fact]
    public void The_timetable_has_no_reminder_countdown()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        var page = File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", "DayScheduleControl.cs"));

        Assert.DoesNotContain("new Section(\"Reminder countdown\")", page);
        Assert.DoesNotContain("SpeakReminder", page);
        Assert.DoesNotContain("AutoCountdownTick", page);
    }
}
