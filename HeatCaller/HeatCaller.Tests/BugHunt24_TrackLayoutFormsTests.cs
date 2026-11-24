namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Track Layouts (#56): renaming tonight's layout - from Edit
/// Details or the Track Designer - only renamed the layout. The open race
/// day and its heats still named the old name, so the layout lost its
/// RACING TONIGHT tag and the marshal rota for the rest of the night came
/// back empty (no layout of that name, so no posts).
/// </summary>
public sealed class BugHunt24_TrackLayoutFormsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed record Night(
        TrackLayoutStore Layouts,
        RaceEventStore Events,
        RoundDefinitionStore Rounds,
        RaceEvent Day,
        RoundDefinition Raced,
        RoundDefinition ToRun1,
        RoundDefinition ToRun2,
        RoundDefinition LastWeeks);

    private static Night OpenNightOn(string layoutName)
    {
        var layouts = TrackLayoutStore.Load();
        layouts.Upsert(new TrackLayout { Name = layoutName, MarshalPostCount = 3 });

        var rounds = RoundDefinitionStore.Load();
        var raced = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1", TrackLayoutName = layoutName, LinkedSessionId = "s1" };
        var toRun1 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q2 Heat 1", TrackLayoutName = layoutName };
        var toRun2 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q2 Heat 2", TrackLayoutName = layoutName };
        var lastWeeks = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q2 Heat 1", TrackLayoutName = layoutName };
        rounds.Add(raced);
        rounds.Add(toRun1);
        rounds.Add(toRun2);
        rounds.Add(lastWeeks);

        var events = RaceEventStore.Load();
        var day = new RaceEvent
        {
            Name = "Friday Club Night",
            TrackLayoutName = layoutName,
            RoundIds = new List<string> { raced.Id, toRun1.Id, toRun2.Id },
        };
        events.Upsert(day);
        events.SetActive(day.Id);

        return new Night(layouts, events, rounds, day, raced, toRun1, toRun2, lastWeeks);
    }

    [Fact]
    public void Renaming_tonights_layout_moves_the_open_day_and_its_unrun_heats()
    {
        var night = OpenNightOn("Cub Oval");
        var layout = night.Layouts.All.Single();
        layout.Name = "Club Oval";
        night.Layouts.Upsert(layout);

        var carry = TrackLayoutManagerControl.CarryRenameToTonight(night.Day, night.Events, night.Rounds, "Cub Oval", "Club Oval");

        Assert.True(carry.DayMoved);
        Assert.Equal("Club Oval", night.Events.Get(night.Day.Id)!.TrackLayoutName);
        Assert.Equal("Club Oval", night.Rounds.Get(night.ToRun1.Id)!.TrackLayoutName);
        Assert.Equal("Club Oval", night.Rounds.Get(night.ToRun2.Id)!.TrackLayoutName);

        // Raced heats keep the name they were raced under, and a round that
        // isn't tonight's is left alone.
        Assert.Equal("Cub Oval", night.Rounds.Get(night.Raced.Id)!.TrackLayoutName);
        Assert.Equal("Cub Oval", night.Rounds.Get(night.LastWeeks.Id)!.TrackLayoutName);

        // The heats left find the layout again, so their marshal posts do.
        var found = MarshalAssigner.LayoutFor(new[] { night.Rounds.Get(night.ToRun1.Id)! }, night.Layouts);
        Assert.NotNull(found);
        Assert.Equal(3, found!.MarshalPostCount);
    }

    [Fact]
    public void Undoing_the_rename_puts_the_day_and_heats_back()
    {
        var night = OpenNightOn("Cub Oval");

        var carry = TrackLayoutManagerControl.CarryRenameToTonight(night.Day, night.Events, night.Rounds, "Cub Oval", "Club Oval");
        TrackLayoutManagerControl.UndoRenameCarry(carry, night.Day.Id, night.Events, night.Rounds, "Cub Oval", "Club Oval");

        Assert.Equal("Cub Oval", night.Events.Get(night.Day.Id)!.TrackLayoutName);
        Assert.Equal("Cub Oval", night.Rounds.Get(night.ToRun1.Id)!.TrackLayoutName);
        Assert.Equal("Cub Oval", night.Rounds.Get(night.ToRun2.Id)!.TrackLayoutName);
    }

    [Fact]
    public void Undoing_the_rename_leaves_a_heat_raced_since_where_it_was_raced()
    {
        var night = OpenNightOn("Cub Oval");

        var carry = TrackLayoutManagerControl.CarryRenameToTonight(night.Day, night.Events, night.Rounds, "Cub Oval", "Club Oval");

        var racedSince = night.Rounds.Get(night.ToRun1.Id)!;
        racedSince.LinkedSessionId = "s2";
        night.Rounds.Update(racedSince);

        TrackLayoutManagerControl.UndoRenameCarry(carry, night.Day.Id, night.Events, night.Rounds, "Cub Oval", "Club Oval");

        Assert.Equal("Club Oval", night.Rounds.Get(night.ToRun1.Id)!.TrackLayoutName);
        Assert.Equal("Cub Oval", night.Rounds.Get(night.ToRun2.Id)!.TrackLayoutName);
    }

    [Fact]
    public void Renaming_another_layout_leaves_tonight_alone()
    {
        var night = OpenNightOn("Cub Oval");

        var carry = TrackLayoutManagerControl.CarryRenameToTonight(night.Day, night.Events, night.Rounds, "Home Track", "Home Track 2");

        Assert.False(carry.DayMoved);
        Assert.Empty(carry.RoundIds);
        Assert.Equal("Cub Oval", night.Events.Get(night.Day.Id)!.TrackLayoutName);
        Assert.Equal("Cub Oval", night.Rounds.Get(night.ToRun1.Id)!.TrackLayoutName);
    }

    [Fact]
    public void A_fix_to_the_names_capitals_carries_too()
    {
        var night = OpenNightOn("club oval");

        var carry = TrackLayoutManagerControl.CarryRenameToTonight(night.Day, night.Events, night.Rounds, "club oval", "Club Oval");

        Assert.True(carry.DayMoved);
        Assert.Equal("Club Oval", night.Events.Get(night.Day.Id)!.TrackLayoutName);
        Assert.Equal("Club Oval", night.Rounds.Get(night.ToRun1.Id)!.TrackLayoutName);
    }

    [Fact]
    public void With_no_day_open_nothing_moves()
    {
        var night = OpenNightOn("Cub Oval");

        var carry = TrackLayoutManagerControl.CarryRenameToTonight(null, night.Events, night.Rounds, "Cub Oval", "Club Oval");

        Assert.False(carry.DayMoved);
        Assert.Empty(carry.RoundIds);
        Assert.Equal("Cub Oval", night.Rounds.Get(night.ToRun1.Id)!.TrackLayoutName);
    }

    [Fact]
    public void Edit_details_and_the_designer_both_carry_a_rename_to_tonight()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "TrackLayoutForms.cs"));

        Assert.Contains("carry = CarryRenameToTonight(Tonight, _events, _rounds, before.Name, form.Result.Name);", source);
        Assert.Contains("carry = CarryRenameToTonight(Tonight, _events, _rounds, previousName, chosenName);", source);
        Assert.Contains("UndoRenameCarry(carry, dayId, events, rounds, before.Name, newName);", source);
        Assert.Contains("UndoRenameCarry(carry, dayId, events, rounds, previousName, chosenName);", source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No check.cmd above the test folder.");
    }
}
