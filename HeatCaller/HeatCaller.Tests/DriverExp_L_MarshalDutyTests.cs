namespace HeatCaller.Tests;

/// <summary>
/// Driver experience L (22 Oct 2026, item 2): MarshalAssigner.DutiesForDay,
/// the marshal rota a driver's phone reads - the same people the grid call
/// names, on the same corners, kept to tonight's race day.
/// </summary>
public sealed class DriverExp_L_MarshalDutyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class Laptop
    {
        public readonly RoundDefinitionStore Rounds = RoundDefinitionStore.Load();
        public readonly RaceEventStore Events = RaceEventStore.Load();
        public readonly TrackLayoutStore Tracks = TrackLayoutStore.Load();
        public readonly ProfileStore Profiles = ProfileStore.Load();

        public Laptop()
        {
            Tracks.Upsert(new TrackLayout
            {
                Name = "Club Oval",
                MarshalPostCount = 2,
                MarshalPostNames = new List<string> { "Hairpin", "Sweeper" },
            });
        }

        public RoundDefinition Heat(string label, params string[] grid)
        {
            var round = new RoundDefinition
            {
                ClassName = "Stock Buggy",
                RoundLabel = label,
                TrackLayoutName = "Club Oval",
                AssignedTransponders = grid.ToList(),
            };

            Rounds.Add(round);
            return round;
        }

        public RaceEvent Day(string name, params RoundDefinition[] rounds)
        {
            var day = new RaceEvent { Name = name, RoundIds = rounds.Select(r => r.Id).ToList() };
            Events.Upsert(day);
            return day;
        }

        public IReadOnlyList<MarshalAssigner.MarshalDuty> Duties() =>
            MarshalAssigner.DutiesForDay(Rounds, Events, Tracks, Profiles);
    }

    [Fact]
    public void The_heat_before_marshals_the_next_on_the_tracks_named_corners()
    {
        var laptop = new Laptop();
        var heat1 = laptop.Heat("Heat 1", "A", "B", "C");
        var heat2 = laptop.Heat("Heat 2", "D", "E");

        var duties = laptop.Duties();

        // The first heat of the day has nobody to marshal it.
        Assert.DoesNotContain(duties, d => d.RoundId == heat1.Id);

        var forHeat2 = duties.Where(d => d.RoundId == heat2.Id).ToList();
        Assert.Equal(2, forHeat2.Count);
        Assert.Equal(new MarshalAssigner.MarshalDuty(heat2.Id, "Heat 2 (Stock Buggy)", "A", "Hairpin"), forHeat2[0]);
        Assert.Equal(new MarshalAssigner.MarshalDuty(heat2.Id, "Heat 2 (Stock Buggy)", "B", "Sweeper"), forHeat2[1]);
    }

    [Fact]
    public void Nobody_marshals_a_race_they_are_driving_in()
    {
        var laptop = new Laptop();
        laptop.Heat("Heat 1", "A", "B", "C");
        var heat2 = laptop.Heat("Heat 2", "A", "D");

        var marshals = laptop.Duties().Where(d => d.RoundId == heat2.Id).Select(d => d.Transponder).ToList();

        Assert.Equal(new[] { "B", "C" }, marshals);
        Assert.DoesNotContain(laptop.Duties(), d => d.RoundId == heat2.Id && heat2.AssignedTransponders.Contains(d.Transponder));
    }

    [Fact]
    public void It_matches_the_rota_the_grid_call_reads()
    {
        var laptop = new Laptop();
        laptop.Heat("Heat 1", "A", "B", "C");
        var heat2 = laptop.Heat("Heat 2", "D", "E", "F");
        var heat3 = laptop.Heat("Heat 3", "A", "G");

        foreach (var round in new[] { heat2, heat3 })
        {
            // What AnnouncementService.SpokenMarshalsFor works out, through
            // the same two steps it now shares.
            var classRounds = MarshalAssigner.ClassRoundsFor(round, laptop.Rounds, laptop.Events);
            var expected = MarshalAssigner.Assign(classRounds, MarshalAssigner.LayoutFor(classRounds, laptop.Tracks), laptop.Profiles)
                .Single(a => a.Round.Id == round.Id)
                .Posts.Where(p => p.IsFilled)
                .Select(p => (p.Transponder, p.Name))
                .ToList();

            var duties = laptop.Duties()
                .Where(d => d.RoundId == round.Id)
                .Select(d => ((string?)d.Transponder, d.PostName))
                .ToList();

            Assert.Equal(expected, duties);
        }
    }

    [Fact]
    public void Last_weeks_heats_do_not_marshal_tonights()
    {
        var laptop = new Laptop();
        var lastWeek = laptop.Heat("Heat 9", "X", "Y");
        var heat1 = laptop.Heat("Heat 1", "A", "B");
        var heat2 = laptop.Heat("Heat 2", "C", "D");

        laptop.Day("Last week", lastWeek);
        var tonight = laptop.Day("Tonight", heat1, heat2);
        laptop.Events.SetActive(tonight.Id);

        var duties = laptop.Duties();

        // Heat 1 is tonight's first: not "marshalled" by last week's heat.
        Assert.DoesNotContain(duties, d => d.RoundId == heat1.Id);
        Assert.DoesNotContain(duties, d => d.Transponder is "X" or "Y");
        Assert.Equal(new[] { "A", "B" }, duties.Where(d => d.RoundId == heat2.Id).Select(d => d.Transponder).ToList());

        // And only tonight's rounds are given at all.
        Assert.DoesNotContain(duties, d => d.RoundId == lastWeek.Id);
    }

    [Fact]
    public void A_track_with_no_posts_gives_no_duties()
    {
        var laptop = new Laptop();
        var heat1 = laptop.Heat("Heat 1", "A", "B");
        var heat2 = laptop.Heat("Heat 2", "C", "D");
        heat1.TrackLayoutName = "Car park";
        heat2.TrackLayoutName = "Car park";

        Assert.Empty(laptop.Duties());
    }
}
