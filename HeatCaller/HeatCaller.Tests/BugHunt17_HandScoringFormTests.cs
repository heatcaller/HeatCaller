using System.Collections;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the desk's hand-scoring grid. Two things: the window went
/// dead for good once the mode lapsed, so switching it on again for the
/// next race left it greyed out on the last race's cars; and a right-click
/// did not take the tap off the list Undo last tap works from, so an Undo
/// after it took a second lap off the car.
/// </summary>
public sealed class BugHunt17_HandScoringFormTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Call(HandScoringForm form, string method, params object[] args) =>
        typeof(HandScoringForm).GetMethod(method, Private)!.Invoke(form, args);

    private static T Field<T>(HandScoringForm form, string name) =>
        (T)typeof(HandScoringForm).GetField(name, Private)!.GetValue(form)!;

    private static List<Control> Buttons(HandScoringForm form) =>
        Field<IDictionary>(form, "_buttons").Values.Cast<Control>().ToList();

    private static HandScoring.Car Car(string transponder) => new(transponder, "Driver " + transponder, 3, 0, 1);

    [Fact]
    public void Undo_after_a_right_click_does_not_take_a_second_lap_off()
    {
        var laps = StaThread.Run(() =>
        {
            var laps = new List<string>();

            // The board counts the laps tapped, as the race does: a car
            // can't have more taps to take back than laps by hand (bug hunt 18).
            var byHand = new Dictionary<string, int> { ["A"] = 0, ["B"] = 0 };

            using var form = new HandScoringForm(
                cars: () => new[] { Car("A") with { ByHand = byHand["A"] }, Car("B") with { ByHand = byHand["B"] } },
                lap: (t, delta) =>
                {
                    laps.Add($"{t}{delta:+0;-0}");
                    byHand[t] += delta;
                    return null;
                },
                isOn: () => true,
                stop: () => { });

            Call(form, "RefreshCars");

            Call(form, "Tap", "A", 1, false);
            Call(form, "Tap", "B", 1, false);
            Call(form, "Tap", "B", -1, false); // the right-click
            Call(form, "UndoLastTap");         // the tap on A
            Call(form, "UndoLastTap");         // nothing left

            return laps;
        }, TimeSpan.FromSeconds(30), () => "tapping the hand-scoring grid");

        Assert.Equal(new[] { "A+1", "B+1", "B-1", "A-1" }, laps);
    }

    [Fact]
    public void The_grid_comes_back_when_the_mode_is_switched_on_again()
    {
        var (deadEnabled, timerRunning, liveCars, liveEnabled, undo) = StaThread.Run(() =>
        {
            var on = true;
            IReadOnlyList<HandScoring.Car> cars = new[] { Car("A"), Car("B") };

            using var form = new HandScoringForm(
                cars: () => cars,
                lap: (_, _) => null,
                isOn: () => on,
                stop: () => { });

            // As Shown does, without putting a maximised window up.
            var timer = Field<System.Windows.Forms.Timer>(form, "_refresh");
            Call(form, "RefreshCars");
            timer.Start();

            Call(form, "Tap", "A", 1, false);

            // The race ends with the window left open.
            on = false;
            Call(form, "RefreshCars");

            var deadEnabled = Buttons(form).Any(b => b.Enabled);
            var timerRunning = timer.Enabled;

            // The next race, the decoder still down: Score by Hand again.
            cars = new[] { Car("C"), Car("D"), Car("E") };
            on = true;
            Call(form, "RefreshCars");

            var live = Buttons(form);

            return (
                deadEnabled,
                timerRunning,
                string.Join(",", Field<IDictionary>(form, "_buttons").Keys.Cast<string>()),
                live.All(b => b.Enabled),
                Field<Button>(form, "_btnUndo").Enabled);
        }, TimeSpan.FromSeconds(30), () => "bringing the hand-scoring grid back");

        Assert.False(deadEnabled);
        Assert.True(timerRunning);
        Assert.Equal("C,D,E", liveCars);
        Assert.True(liveEnabled);

        // The last race's tap is not this race's to undo.
        Assert.False(undo);
    }

    [Fact]
    public void The_same_cars_get_working_buttons_when_the_race_comes_back()
    {
        var enabled = StaThread.Run(() =>
        {
            var on = true;

            using var form = new HandScoringForm(
                cars: () => new[] { Car("A"), Car("B") },
                lap: (_, _) => null,
                isOn: () => on,
                stop: () => { });

            Call(form, "RefreshCars");

            on = false;
            Call(form, "RefreshCars");

            on = true;
            Call(form, "RefreshCars");

            return Buttons(form).Count == 2 && Buttons(form).All(b => b.Enabled);
        }, TimeSpan.FromSeconds(30), () => "bringing the hand-scoring grid back for the same cars");

        Assert.True(enabled);
    }
}
