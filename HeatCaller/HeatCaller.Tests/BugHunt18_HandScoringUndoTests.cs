namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18 (14 Nov 2026): Undo last tap on the hand-scoring grids.
///
/// The desk grid's Undo took a second, real lap off a car whose tap had
/// already been taken back by the app's Undo or a phone's -1; the /manage
/// page's Undo reached back into the last race's taps.
/// </summary>
public sealed class BugHunt18_HandScoringUndoTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static HandScoring.Car Car(string transponder, int byHand) =>
        new(transponder, transponder, Laps: 5 + byHand, ByHand: byHand, StartPosition: 0);

    [Fact]
    public void A_tap_taken_back_somewhere_else_is_not_left_for_undo_to_take_again()
    {
        // A tapped, then B, then A again; the main window's Undo took A's
        // last lap back, so A has one lap by hand left.
        var taps = new List<string> { "A", "B", "A" };

        HandScoring.DropTapsNoLongerOnTheBoard(taps, new[] { Car("A", 1), Car("B", 1) });

        Assert.Equal(new[] { "A", "B" }, taps);
    }

    [Fact]
    public void Every_tap_still_on_the_board_is_kept()
    {
        var taps = new List<string> { "A", "B", "A" };

        // A phone counted another lap for B by hand: more laps than taps is fine.
        HandScoring.DropTapsNoLongerOnTheBoard(taps, new[] { Car("A", 2), Car("b", 3) });

        Assert.Equal(new[] { "A", "B", "A" }, taps);
    }

    [Fact]
    public void A_car_with_no_laps_by_hand_left_has_no_taps_to_undo()
    {
        var taps = new List<string> { "A", "B", "A" };

        HandScoring.DropTapsNoLongerOnTheBoard(taps, new[] { Car("a", 0), Car("B", 1) });

        Assert.Equal(new[] { "B" }, taps);
    }

    [Fact]
    public void The_phone_page_forgets_its_taps_when_the_race_changes_or_hand_scoring_stops()
    {
        var page = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load()).BuildManagePage();

        var from = page.IndexOf("function refreshHand(", StringComparison.Ordinal);
        var to = page.IndexOf("function handLap(", from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "No refreshHand in the manage page.");
        var refreshHand = page[from..to];

        var clear = refreshHand.IndexOf("(!d.data.racing || !d.data.on || d.data.raceGeneration !== handRace)) { handTaps = []; }", StringComparison.Ordinal);
        var remember = refreshHand.IndexOf("handRace = d.data.raceGeneration;", StringComparison.Ordinal);
        var hide = refreshHand.IndexOf("box.style.display = 'none'; return;", StringComparison.Ordinal);

        Assert.True(clear >= 0, "refreshHand no longer clears the taps for another race.");
        Assert.True(remember > clear, "The race is remembered before the taps are checked against it.");
        Assert.True(hide > remember, "The taps are only checked while a race runs.");
    }
}
