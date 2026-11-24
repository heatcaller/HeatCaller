namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, finding 16: Export this driver's data says it is everything
/// the laptop holds about them, but it left out seven things Forget clears
/// as theirs - what was said about them on air, their lap beep, personal-best
/// resets, carried championship points, marshal no-shows, ghost cars chasing
/// them, and race notes or other drivers' correction reasons that name them.
///
/// Real stores in a throwaway data folder; no window is shown.
/// </summary>
public sealed class BugHunt24_DriverDataExportTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Mia = "1234567";
    private const string Sam = "5555555";

    private static readonly string[] Headings =
    {
        "<h2>Said about them on air</h2>",
        "<h2>Lap beep</h2>",
        "<h2>Personal best resets</h2>",
        "<h2>Championship points carried in</h2>",
        "<h2>Missed marshalling</h2>",
        "<h2>Ghost cars chasing them</h2>",
        "<h2>Race notes that name them</h2>",
    };

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();
        public SessionHistoryStore History { get; } = SessionHistoryStore.Load();
        public PrincipalDiary Diary { get; } = PrincipalDiary.Load();
        public LapBeepStore LapBeeps { get; } = LapBeepStore.Load();
        public PersonalBestResetStore PbResets { get; } = PersonalBestResetStore.Load();
        public SeasonStore Seasons { get; } = SeasonStore.Load();
        public RoundDefinitionStore Rounds { get; } = RoundDefinitionStore.Load();
        public GhostStore Ghosts { get; } = GhostStore.Load();

        public DriverDataSources Sources => new()
        {
            Profiles = Profiles,
            History = History,
            Diary = () => Diary,
            LapBeeps = LapBeeps,
            PersonalBestResets = PbResets,
            Seasons = Seasons,
            Rounds = Rounds,
            Ghosts = Ghosts,
        };
    }

    private static World Arrange()
    {
        var w = new World();

        w.Profiles.Upsert(new DriverProfile { Transponder = Mia, DriverName = "Mia Kent", AnnouncerName = "Mia" });
        w.Profiles.Upsert(new DriverProfile { Transponder = Sam, DriverName = "Sam Roe" });

        w.History.Add(new SessionRecord
        {
            Label = "Junior A Main",
            StartedAtUtc = new DateTime(2026, 11, 1, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 11, 1, 19, 6, 0, DateTimeKind.Utc),
            Notes = "Great drive from Mia tonight",
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = Sam, DriverName = "Sam Roe", Laps = 20 },
                new() { Position = 2, Transponder = Mia, DriverName = "Mia Kent", Laps = 19 },
            },
            Edits = new List<ResultEdit>
            {
                new() { Transponder = Sam, DriverName = "Sam Roe", Change = "time penalty 0s -> 5s", Reason = "Pushed off by Mia Kent at the hairpin" },
            },
        });

        w.Diary.Keep(Mia, "Mia Kent", "Dad", "Junior A Main", "We will fix the gearing by next week", new DateTime(2026, 11, 1, 19, 10, 0, DateTimeKind.Utc));
        w.Diary.Keep(Sam, "Sam Roe", "Uncle", "Junior A Main", "Sam's crew say nothing changes", new DateTime(2026, 11, 1, 19, 12, 0, DateTimeKind.Utc));

        w.LapBeeps.Upsert(new DriverLapBeep { Transponder = Mia, ToneIndex = 6 });

        w.PbResets.SetCutoff("Mia Kent", "Club Oval", new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

        w.Seasons.Upsert(new Season
        {
            Name = "Winter Juniors",
            CarriedPoints = new List<CarriedPointsEntry> { new() { DriverName = "Mia Kent", Points = 37 } },
        });

        w.Rounds.Add(new RoundDefinition
        {
            ClassName = "Junior",
            RoundLabel = "Heat 2",
            MarshalNoShows = new List<string> { "Mia Kent (Turn 3)" },
            NoShowMarshals = new List<MarshalNoShow> { new() { Who = "Mia Kent (Turn 3)", Transponder = Mia } },
        });

        w.Ghosts.Current.Ghosts.Clear();
        w.Ghosts.Current.Ghosts.Add(new GhostCar { Name = "Chase the kid", PaceSource = GhostPaceSource.DriverPersonalBest, SourceTransponder = Mia });
        w.Ghosts.Current.Ghosts.Add(new GhostCar { Name = "Roe rocket", PaceSource = GhostPaceSource.DriverPersonalBest, SourceTransponder = Sam });
        w.Ghosts.Save();

        return w;
    }

    [Fact]
    public void Export_shows_every_store_Forget_clears()
    {
        var w = Arrange();
        var mia = w.Profiles.Get(Mia)!;

        var html = DriverDataExport.Html(mia, w.Sources, "Acme RC", new DateTime(2026, 11, 15, 10, 0, 0));

        foreach (var heading in Headings)
        {
            Assert.Contains(heading, html);
        }

        Assert.Contains("We will fix the gearing by next week", html);
        Assert.Contains("Triple tick", html);
        Assert.Contains("Club Oval", html);
        Assert.Contains("Winter Juniors", html);
        Assert.Contains("37", html);
        Assert.Contains("Mia Kent (Turn 3)", html);
        Assert.Contains("Chase the kid", html);
        Assert.Contains("Great drive from Mia tonight", html);
        Assert.Contains("Pushed off by Mia Kent at the hairpin", html);
    }

    [Fact]
    public void Export_leaves_out_other_drivers_diary_lines_and_ghosts()
    {
        var w = Arrange();
        var mia = w.Profiles.Get(Mia)!;

        var html = DriverDataExport.Html(mia, w.Sources, "Acme RC", new DateTime(2026, 11, 15, 10, 0, 0));

        Assert.DoesNotContain("Sam's crew say nothing changes", html);
        Assert.DoesNotContain("Roe rocket", html);
    }

    [Fact]
    public void After_Forget_the_export_holds_none_of_those_things()
    {
        var w = Arrange();
        var mia = w.Profiles.Get(Mia)!;

        DriverForget.Forget(mia, w.Sources);
        var html = DriverDataExport.Html(mia, w.Sources, "Acme RC", new DateTime(2026, 11, 15, 10, 0, 0));

        // A marshal no-show keeps its transponder after Forget, as a result
        // row does (only the name goes), so that one still lists - renamed.
        foreach (var heading in Headings.Where(h => !h.Contains("Missed marshalling", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(heading, html);
        }

        Assert.DoesNotContain("We will fix the gearing by next week", html);
        Assert.DoesNotContain("Mia Kent (Turn 3)", html);
    }
}
