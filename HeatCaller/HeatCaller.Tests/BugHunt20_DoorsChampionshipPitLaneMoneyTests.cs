namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20 (15 Nov 2026): a bulk check-in stamped doors open hours
/// early; the championship check named the round picked next instead of the
/// filed race's class; a new fastest lap threw away the pit lane's chat,
/// which names no lap; and a night with no classes ticked tracked no money
/// for a club that prices only its classes.
/// </summary>
public sealed class BugHunt20_DoorsChampionshipPitLaneMoneyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // Doors open
    // ------------------------------------------------------------------

    [Fact]
    public void A_bulk_tick_or_a_tick_of_drivers_already_in_does_not_open_the_doors()
    {
        var checkIns = CheckInStore.Load();
        checkIns.CurrentEventId = "day-1";

        var raised = 0;
        checkIns.CheckedIn += _ => raised++;

        // The 2pm import with "check everyone in" ticked.
        checkIns.CheckInAll(new[] { "S01", "S02" }, opensDoors: false);
        Assert.Equal(0, raised);
        Assert.True(checkIns.IsCheckedIn("S01"));

        // Drivers already in, ticked again.
        checkIns.CheckInAll(new[] { "S01", "S02" });
        Assert.Equal(0, raised);

        // The first driver who really came in.
        checkIns.CheckInAll(new[] { "S03" });
        Assert.Equal(1, raised);
    }

    // ------------------------------------------------------------------
    // The championship check's class
    // ------------------------------------------------------------------

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 8, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public string LastPrompt { get; private set; } = string.Empty;

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            LastPrompt = systemPrompt + "\n" + userPrompt;
            _clock.Advance(TimeSpan.FromSeconds(2));
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult<string?>("And that is the story so far!");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private readonly Clock _clock = new();

    private (CommentaryDirector Director, Brain Model) Launch()
    {
        var settings = new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 40 };
        var model = new Brain(_clock);
        var commentary = new CommentaryService(ProfileStore.Load(), settings) { Client = model };
        var director = new CommentaryDirector(commentary, settings, _clock.Read);

        director.NewMeeting("day-1");
        director.MonthlyUsage(0, 400);
        director.NightShape(10, 30);

        director.Championship = () => new[]
        {
            new ChampionshipStanding("A", "Dot", PositionNow: 1, PositionBefore: 2, Points: 88, BehindLeader: 0),
            new ChampionshipStanding("B", "Bill", PositionNow: 2, PositionBefore: 1, Points: 85, BehindLeader: 3),
        };

        return (director, model);
    }

    [Fact]
    public async Task The_championship_check_keeps_the_last_races_class_when_the_next_round_is_picked()
    {
        var (director, model) = Launch();

        director.RaceStarted("mod-heat-3", "Mod");
        director.RaceFinished();

        // The Stock A-main is picked; the table is still the Mod heat's.
        director.ClassBetweenRaces("Stock");

        var said = await director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.ChampionshipCheck, said!.Kind);
        Assert.Contains("Mod championship", model.LastPrompt);
        Assert.DoesNotContain("Stock championship", model.LastPrompt);
    }

    [Fact]
    public async Task The_championship_check_names_the_filed_races_class_when_it_is_told()
    {
        var (director, model) = Launch();

        director.RaceStarted("stock-heat-1", "Stock");
        director.RaceFinished();
        director.ClassBetweenRaces("Stock");

        // The last race filed was a Mod one.
        director.ChampionshipClass = () => "Mod";

        var said = await director.NextAsync();

        Assert.NotNull(said);
        Assert.Contains("Mod championship", model.LastPrompt);
        Assert.DoesNotContain("Stock championship", model.LastPrompt);
    }

    // ------------------------------------------------------------------
    // The pit lane and a quicker lap
    // ------------------------------------------------------------------

    [Fact]
    public void A_quicker_lap_leaves_the_pit_lane_standing_but_not_a_pace_note()
    {
        Assert.False(CommentaryDirector.LapTimesMatterFor(SegmentKind.PitLaneLive));
        Assert.True(CommentaryDirector.LapTimesMatterFor(SegmentKind.PaceNote));

        var pitLane = new RaceFactsStamp(1, 3, 1, 5, 100_000, LeaderLapsAllowed: null, LeadMatters: false, LapTimesMatter: false);

        Assert.Null(AnnouncementService.WhyNoLongerTrue(pitLane, pitLane with { FastestLaps = 4, LapsTakenOff = 1 }));

        var pace = new RaceFactsStamp(1, 3, 1, 5, 100_000, LeaderLapsAllowed: null, LeadMatters: false);

        Assert.Equal("a quicker lap has been set since it was written",
            AnnouncementService.WhyNoLongerTrue(pace, pace with { FastestLaps = 4 }));
    }

    // ------------------------------------------------------------------
    // Money with no classes ticked
    // ------------------------------------------------------------------

    [Fact]
    public void A_night_with_no_classes_ticked_charges_when_a_club_class_has_its_own_fee()
    {
        var club = new[] { new RaceClass { Name = "Mod", EntryFee = 20m }, new RaceClass { Name = "Stock" } };
        var classes = DeskMoney.ClassLookup(club);
        var day = new RaceEvent();

        Assert.True(DeskMoney.Charges(day, classes, club));
        Assert.Equal(20m, CheckInControl.FeeFor(new DriverProfile { Transponder = "T1", ClassName = "Mod" }, day, classes));

        var unpriced = new[] { new RaceClass { Name = "Stock" } };
        Assert.False(DeskMoney.Charges(day, DeskMoney.ClassLookup(unpriced), unpriced));

        // Ticked classes still decide it when there are some.
        Assert.False(DeskMoney.Charges(new RaceEvent { ClassNames = new List<string> { "Stock" } }, classes, club));
    }
}
