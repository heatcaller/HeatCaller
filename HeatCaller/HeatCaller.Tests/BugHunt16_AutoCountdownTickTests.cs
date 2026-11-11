using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// 10 Nov 2026, Bug Hunt 16.
///
/// "Start the countdown by itself from the run sheet" has a tick on the
/// Between races card and another on the Timetable's Reminder countdown card,
/// over the one setting. Each read the setting once, when it was built. A tick
/// on the Timetable was then saved back over by the card's next unrelated
/// edit (the card saves every field from its own, stale, tick), and the run
/// sheet's countdown calls stopped with nothing on screen to say so.
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

    private static DayScheduleControl Timetable(AppSettings settings)
    {
        var timetable = new DayScheduleControl(
            DayScheduleStore.Load(),
            RaceClassStore.Load(),
            new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load()),
            new UndoService(),
            RoundDefinitionStore.Load(),
            RaceEventStore.Load());
        timetable.Settings = settings;
        return timetable;
    }

    [Fact]
    public void A_tick_on_the_timetable_survives_an_edit_on_the_between_races_card()
    {
        var seen = StaThread.Run(() =>
        {
            var settings = new AppSettings();
            var (card, _) = BetweenRacesCard.Build(settings, new TextSize(), null, null, null, null);
            using var cardOwner = card;
            using var timetable = Timetable(settings);

            Box(timetable, "Count down by itself").Checked = true;
            var cardFollowed = Box(card, "Start the countdown by itself").Checked;

            // Something else on the card, which saves the whole card.
            var callUps = Box(card, "Call-up: next race");
            callUps.Checked = !callUps.Checked;

            return (cardFollowed, settings.BetweenRacesAutoCountdown);
        }, TimeSpan.FromSeconds(60), () => "building the Between races card and the Timetable");

        Assert.True(seen.cardFollowed);
        Assert.True(seen.BetweenRacesAutoCountdown);
    }

    [Fact]
    public void A_tick_on_the_card_shows_on_the_timetable()
    {
        var seen = StaThread.Run(() =>
        {
            var settings = new AppSettings();
            var (card, _) = BetweenRacesCard.Build(settings, new TextSize(), null, null, null, null);
            using var cardOwner = card;
            using var timetable = Timetable(settings);

            Box(card, "Start the countdown by itself").Checked = true;
            var on = (Box(timetable, "Count down by itself").Checked, settings.BetweenRacesAutoCountdown);

            Box(card, "Start the countdown by itself").Checked = false;
            var off = (Box(timetable, "Count down by itself").Checked, settings.BetweenRacesAutoCountdown);

            return (on, off);
        }, TimeSpan.FromSeconds(60), () => "building the Between races card and the Timetable");

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
}
