using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Tell the Drivers after a fix that brings the night back on time (20 Nov 2026).
///
/// WHY: the Get back on time card hid as soon as no fixes were left on offer,
/// and Tell the Drivers lives on that card - so the fix that put the night
/// right took the button away at the very moment there was something to tell.
/// The card now stays, with just that button, until it's pressed.
/// </summary>
public sealed class TellTheDriversTests
{
    private static volatile string _now = "starting";

    private static void Settle(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);

        do
        {
            StaThread.Settle();
            Thread.Sleep(15);
        }
        while (DateTime.UtcNow < until);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void RefreshFixes(Control today) =>
        today.GetType().GetMethod("RefreshFixes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(today, null);

    [Fact]
    public void The_card_stays_with_Tell_the_Drivers_until_it_is_pressed_and_never_for_another_days_change()
    {
        var problem = StaThread.Run(
            () =>
            {
                using var data = new TempAppData();

                new AppSettings
                {
                    FontScale = 1f,
                    StartMaximized = false,
                    HasCompletedFirstRun = true,
                    HasSeenRaceDaySetup = true,
                    AutoBackupEnabled = false,
                }.Save();

                _now = "building the window";
                var window = new MainForm();

                try
                {
                    window.LayoutCheckMode = true;
                    window.StartPosition = FormStartPosition.Manual;
                    window.Location = new Point(-20000, -20000);
                    window.Show();
                    Settle(300);
                    window.SizeContentForLayoutCheck(new Size(1600, 900));

                    // A race day with no out-by time: nothing is late, so no fixes are on offer.
                    _now = "opening a race day";
                    var events = Field<RaceEventStore>(window, "_raceEvents");
                    var day = EventSpine.StartEvent(events, "Club night");
                    events.SetActive(day.Id);
                    window.ShowPageForLayoutCheck("Race Day");
                    Settle(500);

                    var today = Field<Control>(window, "_todayControl");
                    var card = Field<Control>(today, "_fixesCard");
                    var tell = Field<Button>(today, "_btnTell");
                    var apply = Field<Button>(today, "_btnApply");

                    RefreshFixes(today);
                    Settle(100);

                    if (card.Visible)
                    {
                        return "With nothing late and nothing to tell, the Get back on time card showed.";
                    }

                    // What Apply leaves behind when its fix put the night on time.
                    _now = "a change waiting to be told";
                    SetField(today, "_lastAnnouncement", "A change to tonight's program: Q3 is cancelled.");
                    SetField(today, "_lastAnnouncementDayId", day.Id);
                    RefreshFixes(today);
                    Settle(100);

                    if (!card.Visible || !tell.Visible || !tell.Enabled)
                    {
                        return $"With a change waiting, the card showed {card.Visible}, Tell the Drivers visible {tell.Visible}, enabled {tell.Enabled}.";
                    }

                    if (apply.Visible)
                    {
                        return "With no fixes on offer, Apply still showed beside Tell the Drivers.";
                    }

                    // Pressing it tells the room once, and the card goes.
                    _now = "telling the drivers";
                    tell.PerformClick();
                    Settle(200);

                    if (card.Visible || Field<string>(today, "_lastAnnouncement").Length > 0)
                    {
                        return "After Tell the Drivers, the card stayed (or the change was kept to tell again).";
                    }

                    // A change from another race day is never offered tonight.
                    _now = "another day's change";
                    SetField(today, "_lastAnnouncement", "A change to last week's program.");
                    SetField(today, "_lastAnnouncementDayId", "some-other-day");
                    RefreshFixes(today);
                    Settle(100);

                    return card.Visible ? "A change from another race day kept the card showing." : null;
                }
                finally
                {
                    window.Close();
                    window.Dispose();
                }
            },
            TimeSpan.FromMinutes(4),
            () => _now);

        Assert.Null(problem);
    }
}
