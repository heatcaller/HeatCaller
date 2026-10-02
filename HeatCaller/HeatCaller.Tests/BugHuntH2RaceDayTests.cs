namespace HeatCaller.Tests;

/// <summary>
/// The Setup tab's save while something else changes the same day.
///
/// The save used to copy every field on the page onto the day. A phone
/// change that landed while the director was typing a note (classes, heat
/// size) was told "Saved." and then put back by the note's save. Only the
/// fields changed on the page since the edit run began are written now.
/// </summary>
public sealed class BugHuntH2RaceDayTests
{
    [Fact]
    public void A_note_typed_on_the_laptop_does_not_undo_a_phone_change_to_classes_and_heat_size()
    {
        var baseline = new RaceEvent
        {
            Name = "Tuesday",
            Notes = string.Empty,
            ClassNames = new List<string> { "Stock", "Mod" },
            DriversPerHeat = 8,
        };

        // The phone's change, made to the live day while the note was typed.
        var live = Copy(baseline);
        live.ClassNames = new List<string> { "Stock", "Mod", "GT" };
        live.DriversPerHeat = 6;

        // What the page shows: the baseline plus the note.
        var onScreen = Copy(baseline);
        onScreen.Notes = "Track wet, start 15 minutes late";

        RaceDayEventControl.MergeChangedFields(live, baseline, onScreen);

        Assert.Equal("Track wet, start 15 minutes late", live.Notes);
        Assert.Equal(new[] { "Stock", "Mod", "GT" }, live.ClassNames);
        Assert.Equal(6, live.DriversPerHeat);
        Assert.Equal("Tuesday", live.Name);
    }

    [Fact]
    public void A_field_changed_on_the_laptop_is_still_written()
    {
        var baseline = new RaceEvent { Name = "Tuesday", ClassNames = new List<string> { "Stock" }, DriversPerHeat = 8 };
        var live = Copy(baseline);
        var onScreen = Copy(baseline);
        onScreen.Name = "Tuesday Club Night";
        onScreen.ClassNames = new List<string> { "Stock", "Mod" };
        onScreen.DriversPerHeat = 10;

        RaceDayEventControl.MergeChangedFields(live, baseline, onScreen);

        Assert.Equal("Tuesday Club Night", live.Name);
        Assert.Equal(new[] { "Stock", "Mod" }, live.ClassNames);
        Assert.Equal(10, live.DriversPerHeat);
    }

    [Fact]
    public void The_club_default_shown_on_the_page_does_not_overwrite_an_explicit_auto_advance_choice()
    {
        var baseline = new RaceEvent { AutoAdvanceRounds = null };

        var live = Copy(baseline);
        live.AutoAdvanceRounds = false;

        var onScreen = Copy(baseline);
        onScreen.AutoAdvanceRounds = true;

        RaceDayEventControl.MergeChangedFields(live, baseline, onScreen);

        Assert.False(live.AutoAdvanceRounds);
    }

    private static RaceEvent Copy(RaceEvent raceEvent) =>
        System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(System.Text.Json.JsonSerializer.Serialize(raceEvent))!;
}
