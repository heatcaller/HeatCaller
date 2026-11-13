namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, late check-ins:
///  - One added by the timer while Move Drivers Between Heats was open was
///    wiped by the window's Save and never offered again. They now wait
///    until it closes.
///  - Undo of "not tonight" cleared the declined list but left the driver
///    noted as known, so Next Steps never asked about them again.
/// </summary>
public sealed class BugHunt17_RaceProgramControl_LateArrivalsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_late_check_in_waits_while_a_roster_window_is_open() => BugHunt17_RaceProgramControlTests.OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        night.Driver("4", "Rhoda Rage", "Stock");

        night.Program.RosterEditOpen = true;
        Assert.Null(night.Program.AbsorbLateArrivals());
        Assert.DoesNotContain(night.DayRounds(), r => r.AssignedTransponders.Contains("4"));

        // Closed: picked up on the next tick, not forgotten.
        night.Program.RosterEditOpen = false;
        Assert.NotNull(night.Program.AbsorbLateArrivals());
        Assert.Contains(night.DayRounds(), r => r.AssignedTransponders.Contains("4"));
    });

    [Fact]
    public void Undoing_not_tonight_brings_the_question_back() => BugHunt17_RaceProgramControlTests.OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        var day = night.Events.Active!;
        day.HeatsPostedUtc = DateTime.UtcNow;
        night.Events.Upsert(day);

        night.Driver("5", "Axel Grease", "Stock");
        Assert.Equal(ProgramStage.LateArrivals, night.Program.NextStep().Stage);

        // Unticked: not tonight.
        night.Program.AnswerLateArrivals(night.Program.LateArrivalsTonight(), _ => false, day);
        Assert.NotEqual(ProgramStage.LateArrivals, night.Program.NextStep().Stage);
        Assert.NotEmpty(night.Events.Active!.LateEntriesDeclined);

        Assert.Equal("Not tonight: late check-ins", night.Undo.Undo());

        Assert.Empty(night.Events.Active!.LateEntriesDeclined);
        Assert.Equal(ProgramStage.LateArrivals, night.Program.NextStep().Stage);
        Assert.Equal(new[] { new LateArrivals.Arrival("Stock", "5") }, night.Program.LateArrivalsTonight());
    });
}
