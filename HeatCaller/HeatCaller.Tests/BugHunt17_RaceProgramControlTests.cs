namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the program page:
///  - Building the heats again threw when a heat two classes share was
///    still to run, because it was counted once for each class.
///  - Undo of a late entry wrote the old rosters back whatever had happened
///    since, so it took the driver out of a heat they had just raced in.
///  - Move Drivers Between Heats said a class sharing all its heats "has no
///    heats yet" and offered to build tonight's.
/// </summary>
public sealed class BugHunt17_RaceProgramControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Building_again_over_a_shared_heat_still_to_run_replaces_it_once() => OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock", "Mod");
        night.ShareHeatsUnder(4);
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");
        night.Driver("3", "Hugh Turn", "Mod");
        night.Driver("4", "Rhoda Rage", "Mod");

        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        var first = night.DayRounds();
        Assert.Contains(first, MixedHeats.IsShared);

        // The question counts each shared heat once...
        Assert.Contains($"{first.Count} qualifiers", night.Program.PlanQualifyingBuild().Message);

        // ...and the build goes through, with as many heats as before.
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);
        Assert.Equal(first.Count, night.DayRounds().Count);
    });

    [Fact]
    public void Undoing_a_late_entry_leaves_the_driver_in_a_heat_run_since() => OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        night.Driver("9", "Axel Grease", "Stock");
        Assert.True(night.Program.AddLateEntryFor("Stock", "9", string.Empty).Ok);

        var heats = night.DayRounds().Where(r => r.AssignedTransponders.Contains("9")).ToList();
        Assert.True(heats.Count > 1);

        // Their first heat is run and saved - which adds nothing to the undo stack.
        var run = heats[0];
        run.LinkedSessionId = "session-1";
        night.Rounds.Update(run);

        Assert.NotNull(night.Undo.Undo());

        Assert.Contains("9", night.Rounds.Get(run.Id)!.AssignedTransponders);
        Assert.All(heats.Skip(1), h => Assert.DoesNotContain("9", night.Rounds.Get(h.Id)!.AssignedTransponders));
    });

    [Fact]
    public void Undoing_a_late_entry_is_refused_when_every_heat_has_been_run_since() => OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        night.Driver("9", "Axel Grease", "Stock");
        Assert.True(night.Program.AddLateEntryFor("Stock", "9", string.Empty).Ok);

        foreach (var heat in night.DayRounds())
        {
            heat.LinkedSessionId = "session-" + heat.Id;
            night.Rounds.Update(heat);
        }

        string? refusal = null;
        night.Undo.UndoFailed += (_, ex) => refusal = ex.Message;

        Assert.Null(night.Undo.Undo());
        Assert.Contains("Nothing was changed", refusal);
        Assert.All(night.DayRounds(), h => Assert.Contains("9", h.AssignedTransponders));
    });

    [Fact]
    public void Undoing_a_late_entry_waits_while_one_of_its_heats_is_on_track() => OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        night.Driver("9", "Axel Grease", "Stock");
        Assert.True(night.Program.AddLateEntryFor("Stock", "9", string.Empty).Ok);

        var onTrack = night.DayRounds().First(r => r.AssignedTransponders.Contains("9"));
        night.Program.RoundOnTrackId = () => onTrack.Id;

        Assert.Null(night.Undo.Undo());
        Assert.All(night.DayRounds(), h => Assert.Contains("9", h.AssignedTransponders));

        // Still there to undo once the race is saved.
        Assert.StartsWith("Late entry", night.Undo.Description);
    });

    [Fact]
    public void A_heat_edited_since_is_not_one_a_late_entry_undo_may_put_back()
    {
        var kept = new RoundDefinition { RoundLabel = "Q1 Heat 1", AssignedTransponders = new() { "a", "late" } };
        var edited = new RoundDefinition { RoundLabel = "Q2 Heat 1", AssignedTransponders = new() { "a", "late", "b" } };
        var rounds = new[] { kept, edited }.ToDictionary(r => r.Id);

        var restores = new List<RaceProgramControl.RosterRestore>
        {
            new(kept.Id, null, new() { "a", "late" }, new() { "a" }),
            new(edited.Id, null, new() { "a", "late" }, new() { "a" }),
            new("deleted", null, new() { "late" }, new()),
        };

        var free = RaceProgramControl.LateEntryRestorable(restores, id => rounds.GetValueOrDefault(id));

        Assert.Equal(kept.Id, Assert.Single(free).RoundId);
    }

    [Fact]
    public void A_class_sharing_all_its_heats_is_not_told_it_has_none() => OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock", "Mod");
        night.ShareHeatsUnder(4);
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");
        night.Driver("3", "Hugh Turn", "Mod");
        night.Driver("4", "Rhoda Rage", "Mod");

        // Nothing built: no note, so the offer to build still stands.
        Assert.Null(night.Program.SharedHeatsOnlyNote("Mod"));

        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        var heats = night.DayRounds();
        Assert.All(heats, h => Assert.True(MixedHeats.IsShared(h)));

        var host = heats[0].ClassName;
        var folded = host == "Stock" ? "Mod" : "Stock";

        Assert.Null(night.Program.SharedHeatsOnlyNote(host));
        Assert.Contains($"shared with \"{host}\"", night.Program.SharedHeatsOnlyNote(folded));
    });

    internal static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 17, program page");
}

/// <summary>A race day with the given classes and the program page on it.</summary>
internal sealed class BugHunt17Night : IDisposable
{
    public readonly AppSettings Settings = new();
    public readonly UndoService Undo = new();
    public readonly RaceEventStore Events = RaceEventStore.Load();
    public readonly RaceClassStore Classes = RaceClassStore.Load();
    public readonly ProfileStore Profiles = ProfileStore.Load();
    public readonly RoundDefinitionStore Rounds = RoundDefinitionStore.Load();
    public readonly SessionHistoryStore History = SessionHistoryStore.Load();
    public readonly CheckInStore CheckIns = CheckInStore.Load();
    public readonly TechInspectionStore Tech = TechInspectionStore.Load();
    public readonly DayScheduleStore Schedule = DayScheduleStore.Load();
    public readonly TrackLayoutStore Tracks = TrackLayoutStore.Load();
    public readonly RaceProgramControl Program;

    public BugHunt17Night(params string[] classNames)
    {
        EventSpine.Bind(Events, CheckIns, Tech, Schedule, Rounds);

        foreach (var name in classNames)
        {
            Classes.Upsert(new RaceClass { Name = name });
        }

        EventSpine.StartEvent(Events, "Tonight", classNames: classNames, qualifyingRounds: 3);

        Program = new RaceProgramControl(Classes, Profiles, Rounds, Tracks, History, CheckIns, Settings, Undo, Events, Schedule);
    }

    /// <summary>A driver in the class, checked in.</summary>
    public void Driver(string transponder, string name, string className)
    {
        Profiles.Upsert(new DriverProfile { Transponder = transponder, DriverName = name, ClassName = className });
        CheckIns.SetCheckedIn(transponder, true);
    }

    public void ShareHeatsUnder(int drivers)
    {
        var day = Events.Active!;
        day.ShareHeatsUnder = drivers;
        Events.Upsert(day);
    }

    public List<RoundDefinition> DayRounds()
    {
        var ids = Events.Active!.RoundIds.ToHashSet(StringComparer.Ordinal);
        return Rounds.All.Where(r => ids.Contains(r.Id)).ToList();
    }

    public void Dispose() => Program.Dispose();
}
