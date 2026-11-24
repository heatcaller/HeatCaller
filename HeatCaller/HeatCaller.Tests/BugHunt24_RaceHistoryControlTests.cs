using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race History: an & in a class or track name
/// shows in the results title as typed, and the Lap Times line and hover
/// leave out the start lap by position, so two drivers who set their best on
/// the same lap are told the same lap number.
/// </summary>
public sealed class BugHunt24_RaceHistoryControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------ #265 ampersand in the title

    [Fact]
    public void The_results_title_draws_an_ampersand_in_a_class_or_track_name_as_text()
    {
        var history = SessionHistoryStore.Load();
        var start = new DateTime(2026, 11, 20, 19, 0, 0, DateTimeKind.Utc);
        history.Add(new SessionRecord
        {
            Id = "s1",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = start,
            EndedAtUtc = start.AddMinutes(5),
            Label = "2wd & 4wd A Main",
            TrackLayoutName = "Smith & Sons Raceway",
            Results = { new SessionResultEntry { Position = 1, Transponder = "100", DriverName = "Sam Price", Laps = 20 } },
        });

        var (text, mnemonic) = StaThread.Run(() =>
        {
            using var control = new RaceHistoryControl(history, new UndoService(), new AppSettings());
            _ = control.Handle;

            var list = (ListView)typeof(RaceHistoryControl).GetField("_sessionList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;
            list.Items[0].Selected = true;
            typeof(RaceHistoryControl).GetMethod("ShowDetail", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, null);

            var title = (Label)typeof(RaceHistoryControl).GetField("_lblDetailTitle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;
            return (title.Text, title.UseMnemonic);
        }, TimeSpan.FromSeconds(60), () => "Race History with an & in the names");

        Assert.Contains("Smith & Sons Raceway", text);
        Assert.Contains("2wd & 4wd A Main", text);
        Assert.False(mnemonic);
    }

    // ------------------------------------------------------------ #266 start lap in the lap line

    private static SessionResultEntry Driver(long fastLapMs, params long[] laps) =>
        new() { Transponder = "1", DriverName = "D", FastLapMs = fastLapMs, LapTimesMs = laps.ToList() };

    [Fact]
    public void A_start_lap_slower_than_the_best_lap_is_not_drawn_as_a_lap()
    {
        var quickStart = Driver(10000, 1400, 10500, 10000, 10600);
        var slowStart = Driver(10000, 12000, 10500, 10000, 10600);

        Assert.Equal(new long[] { 10500, 10000, 10600 }, RaceHistoryControl.LapsToDraw(quickStart));
        Assert.Equal(new long[] { 10500, 10000, 10600 }, RaceHistoryControl.LapsToDraw(slowStart));
    }

    [Fact]
    public void Two_drivers_best_on_the_same_lap_are_told_the_same_lap_number_whatever_their_start()
    {
        var quickStart = Driver(10000, 1400, 10500, 10000, 10600);
        var slowStart = Driver(10000, 12000, 10500, 10000, 10600);

        var quickTip = RaceHistoryControl.LapTraceTip(quickStart);
        var slowTip = RaceHistoryControl.LapTraceTip(slowStart);

        // Counted from the first crossing, as Lap Detail's "L3" does.
        Assert.Equal("3 laps · best 10.000s on lap 3", quickTip);
        Assert.Equal(quickTip, slowTip);
    }

    [Fact]
    public void A_credited_start_drops_both_start_slots_from_the_lap_line()
    {
        var credited = new SessionResultEntry
        {
            Transponder = "1",
            FastLapMs = 10000,
            StartLapCredited = true,
            LapTimesMs = new List<long> { 0, 4000, 10500, 10000 },
        };

        Assert.Equal(new long[] { 10500, 10000 }, RaceHistoryControl.LapsToDraw(credited));
        Assert.Equal("2 laps · best 10.000s on lap 4", RaceHistoryControl.LapTraceTip(credited));
    }

    [Fact]
    public void A_qualifying_start_keeps_both_real_laps_and_no_laps_gives_no_hover()
    {
        var qualifier = Driver(10000, 0, 10500, 10000);

        Assert.Equal(new long[] { 10500, 10000 }, RaceHistoryControl.LapsToDraw(qualifier));
        Assert.Null(RaceHistoryControl.LapTraceTip(Driver(0, 1400)));
    }
}
