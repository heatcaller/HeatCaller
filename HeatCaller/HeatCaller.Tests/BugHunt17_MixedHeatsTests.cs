namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), mixed-class heats. The announcer's class map
/// was built without profiles, so a driver put in a shared heat by hand was
/// called in its main class while the saved result put them in their own.
/// And renaming a class left the old name stamped on a shared heat's saved
/// results, so the renamed class's championship scored the heat as nothing.
/// </summary>
public sealed class BugHunt17_MixedHeatsTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    public void Dispose() => _data.Dispose();

    private static SessionResultEntry Row(int position, string transponder, string? className) =>
        new() { Position = position, Transponder = transponder, DriverName = transponder, ClassName = className };

    [Fact]
    public void The_announcers_map_puts_a_driver_added_by_hand_in_their_profiles_class()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "ALEX", DriverName = "Alex", ClassName = "Mod" });

        // Alex was added through the driver editor: on the roster, not in the heat's own record.
        var round = new RoundDefinition
        {
            ClassName = "Stock",
            SharedClasses = new() { "Mod" },
            AssignedTransponders = new() { "S1", "M1", "ALEX" },
            SharedClassOf = new(StringComparer.OrdinalIgnoreCase) { ["S1"] = "Stock", ["M1"] = "Mod" },
        };

        var map = MixedHeats.ClassMapFor(round, _profiles.AnyThread)!;

        Assert.Equal("Mod", map["ALEX"]);
        Assert.Equal("Stock", map["S1"]);
        Assert.Equal("Mod", map["M1"]);

        // The same class the saved result is stamped with.
        Assert.Equal(MixedHeats.ClassOfDriver(round, "ALEX", _profiles), map["ALEX"]);

        // So Alex winning overall is Mod's winner, and the Stock winner is still named.
        Assert.Equal("Alex wins Mod, Sam wins Stock.",
            MixedHeats.ClassWinnersLine(new[] { "ALEX", "S1", "M1" }, map, t => t == "ALEX" ? "Alex" : t == "S1" ? "Sam" : t));

        Assert.Null(MixedHeats.ClassMapFor(new RoundDefinition { ClassName = "Stock" }, _profiles.AnyThread));
    }

    /// <summary>
    /// The announcer reads the map off the UI thread while check-in can be
    /// ticking a class for a driver, which edits the profile's list of other
    /// classes in place. Walked with foreach, that threw "Collection was
    /// modified" into the position report.
    /// </summary>
    [Fact]
    public async Task The_announcers_map_can_be_built_while_a_drivers_classes_are_being_edited()
    {
        var alex = new DriverProfile { Transponder = "ALEX", DriverName = "Alex", ClassName = "Buggy", Classes = new() { "Truck", "Mod" } };
        _profiles.Upsert(alex);

        var round = new RoundDefinition
        {
            ClassName = "Stock",
            SharedClasses = new() { "Mod" },
            AssignedTransponders = new() { "S1", "ALEX" },
            SharedClassOf = new(StringComparer.OrdinalIgnoreCase) { ["S1"] = "Stock" },
        };

        // Their other classes still decide, in order, when the main class is not one of the heat's.
        Assert.Equal("Mod", MixedHeats.ClassMapFor(round, _profiles.AnyThread)!["ALEX"]);

        // The profile the any-thread view hands out, edited the way check-in edits it.
        var live = _profiles.AnyThread.Get("ALEX")!;
        using var stop = new CancellationTokenSource();

        var editing = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                live.Classes.Add("Truggy");
                live.Classes.RemoveAll(c => c == "Truggy");
            }
        });

        try
        {
            for (var i = 0; i < 20000; i++)
            {
                // Never a throw, and always one of the heat's classes.
                Assert.Contains(MixedHeats.ClassMapFor(round, _profiles.AnyThread)!["ALEX"], new[] { "Stock", "Mod" });
            }
        }
        finally
        {
            stop.Cancel();
            await editing;
        }

        Assert.Contains("for (var i = 0; others is not null && i < others.Count; i++)", Source("MixedHeats.cs"));
        Assert.DoesNotContain("foreach (var other in profile.Classes", Source("MixedHeats.cs"));
    }

    [Fact]
    public void The_main_form_hands_the_announcer_the_any_thread_profiles()
    {
        Assert.Contains("MixedHeats.ClassMapFor(_activeRound, _profiles.AnyThread)", Source("MainForm.cs"));
        Assert.Contains("MixedHeats.RenameStamps(s.Results, _rounds.Get(roundId), oldName, newName)", Source("MainForm.cs"));
    }

    [Fact]
    public void A_renamed_class_takes_its_stamps_on_a_shared_heats_result_with_it()
    {
        var results = new List<SessionResultEntry> { Row(1, "M1", "Mod"), Row(2, "S1", "Stock"), Row(3, "M2", "Mod") };

        // The round as the rename leaves it: Mod is Modified now.
        var round = new RoundDefinition
        {
            ClassName = "Stock",
            SharedClasses = new() { "Modified" },
            AssignedTransponders = new() { "M1", "S1", "M2" },
            SharedClassOf = new(StringComparer.OrdinalIgnoreCase) { ["M1"] = "Modified", ["S1"] = "Stock", ["M2"] = "Modified" },
        };

        // Before: the Modified championship finds nobody in the heat.
        Assert.Empty(Assert.Single(MixedHeats.ScoringFields(results, "Modified")));

        Assert.True(MixedHeats.RenameStamps(results, round, "Mod", "Modified"));

        Assert.Equal(new[] { "Modified", "Stock", "Modified" }, results.Select(r => r.ClassName));
        Assert.Equal(new[] { "M1", "M2" }, Assert.Single(MixedHeats.ScoringFields(results, "Modified")).Select(r => r.Transponder));
        Assert.Equal(new[] { "S1" }, Assert.Single(MixedHeats.ScoringFields(results, "Stock")).Select(r => r.Transponder));

        // Nothing more to move.
        Assert.False(MixedHeats.RenameStamps(results, round, "Mod", "Modified"));

        // The undo: the round is back to Mod, and the same call the other way moves the stamps back.
        round.SharedClasses = new() { "Mod" };
        Assert.True(MixedHeats.RenameStamps(results, round, "Modified", "Mod"));
        Assert.Equal(new[] { "Mod", "Stock", "Mod" }, results.Select(r => r.ClassName));
    }

    [Fact]
    public void Stamps_are_left_alone_where_the_round_was_not_renamed()
    {
        // A heat that still runs the old name (the rename's undo leaves alone what already carried the new one).
        var results = new List<SessionResultEntry> { Row(1, "M1", "Mod"), Row(2, "S1", "Stock") };
        var still = new RoundDefinition { ClassName = "Stock", SharedClasses = new() { "Mod" } };

        Assert.False(MixedHeats.RenameStamps(results, still, "Mod", "Modified"));
        Assert.False(MixedHeats.RenameStamps(results, still, "Buggy", "Mod"));
        Assert.False(MixedHeats.RenameStamps(results, null, "Mod", "Modified"));
        Assert.Equal(new[] { "Mod", "Stock" }, results.Select(r => r.ClassName));
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
