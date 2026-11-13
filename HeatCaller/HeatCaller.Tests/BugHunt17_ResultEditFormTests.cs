using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): a Laps entry Edit Result could not read ("12*",
/// as a hand-counted lap count is printed elsewhere) stayed in the cell with
/// Added Laps unmoved and nothing said, so the correction could be saved
/// without it. It is now an error like any other cell's, and Save waits.
/// </summary>
public sealed class BugHunt17_ResultEditFormTests
{
    private static SessionRecord Session() => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Stock - A-Main",
        Results =
        {
            new SessionResultEntry { Position = 1, Transponder = "1111111", DriverName = "Max Power", Laps = 12, ElapsedMs = 150_000, LapTimesMs = Enumerable.Repeat(12_500L, 12).ToList() },
            new SessionResultEntry { Position = 2, Transponder = "2222222", DriverName = "Sam Price", Laps = 11, ElapsedMs = 151_000, LapTimesMs = Enumerable.Repeat(13_700L, 11).ToList() },
        },
    };

    private static T Field<T>(ResultEditForm form, string name) =>
        (T)typeof(ResultEditForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static string? ErrorOf(ResultEditForm form)
    {
        var args = new object?[] { null };
        typeof(ResultEditForm).GetMethod("ReadAdjustments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
        return (string?)args[0];
    }

    [Fact]
    public void A_laps_entry_that_cannot_be_read_is_said_and_holds_save()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new ResultEditForm(Session());
            var grid = Field<DataGridView>(form, "_editGrid");
            var before = ErrorOf(form);

            Field<TextBox>(form, "_txtReason").Text = "Hand count";
            grid.Rows[0].Cells["addedLaps"].Value = 1;
            grid.Rows[1].Cells["laps"].Value = "12*";

            var error = ErrorOf(form);
            var summary = Field<Label>(form, "_lblSummary").Text;
            var canSave = Field<Button>(form, "_btnSave").Enabled;

            grid.Rows[1].Cells["laps"].Value = " 12 ";

            return (before, error, summary, canSave, after: ErrorOf(form));
        }, TimeSpan.FromSeconds(30), () => "opening Edit Result");

        Assert.Null(seen.before);
        Assert.Equal("Sam Price: laps has to be a whole number, 0 or more.", seen.error);
        Assert.Equal(seen.error, seen.summary);
        Assert.False(seen.canSave);
        Assert.Null(seen.after);
    }
}
