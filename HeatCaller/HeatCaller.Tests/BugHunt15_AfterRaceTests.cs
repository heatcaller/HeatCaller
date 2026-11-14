namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026): the after-race Team Principal and Technical
/// Lead took raw position 1 as the winner even when nobody finished (B4), and
/// the race recording put every crossing of a held car late on a SharedGreen
/// (handicap) start, and every qualifier's early (B36).
/// </summary>
public sealed class BugHunt15_AfterRaceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    private static SessionResultEntry Row(int position, string transponder, int laps, string? outcome = null) => new()
    {
        Transponder = transponder,
        DriverName = $"Driver {transponder}",
        Position = position,
        Laps = laps,
        ElapsedMs = 300_000 + (position * 450),
        FastLapMs = 24_000 + (position * 200),
        Outcome = outcome ?? string.Empty,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        var race = new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 11, 9, 19, 10, 0, DateTimeKind.Utc),
        };

        race.Results.AddRange(rows);
        return race;
    }

    private sealed class Brain : ICommentaryBrain
    {
        public int Calls { get; private set; }

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
            Calls++;
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>("HOST: Pat, what a night.\nTEAM PRINCIPAL: We are thrilled.\nHOST: And next?\nTEAM PRINCIPAL: More of the same.");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Brain Model) Desk()
    {
        var model = new Brain();
        var desk = new CommentaryService(
            ProfileStore.Load(),
            new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        return (desk, model);
    }

    // --- B4: no finisher, no winner to interview --------------------------------------------

    [Fact]
    public void The_winner_is_position_one_only_when_it_finished()
    {
        Assert.Equal("A", CommentaryService.ClassifiedWinner(Race(Row(2, "B", 12), Row(1, "A", 12)))?.Transponder);
        Assert.Null(CommentaryService.ClassifiedWinner(Race(Row(1, "C", 11, outcome: "DQ"))));
        Assert.Null(CommentaryService.ClassifiedWinner(Race(Row(1, "A", 0), Row(2, "B", 0))));
        Assert.Null(CommentaryService.ClassifiedWinner(Race(Row(1, "A", 4, outcome: "DNF"), Row(2, "B", 2, outcome: "DQ"))));
        Assert.Null(CommentaryService.ClassifiedWinner(Race()));
    }

    [Fact]
    public async Task No_principal_or_technical_lead_interview_when_nobody_finished()
    {
        var (desk, model) = Desk();
        var struckOff = Race(Row(1, "C", 11, outcome: "DQ"));

        Assert.Null(await desk.BuildPrincipalAfterAsync(struckOff, "Pat", null, null));
        Assert.Null(await desk.BuildMechanicInterviewAsync(struckOff, "Guiseppi", Array.Empty<string>(), null));
        Assert.Null(await desk.BuildPrincipalAfterAsync(Race(Row(1, "A", 0)), "Pat", null, null));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void The_principal_the_booth_brief_the_diary_and_the_mechanic_take_the_classified_winner()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.DoesNotContain("record.Results.OrderBy(r => r.Position).First()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("record.Results.OrderBy(r => r.Position).FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("var boothWinner = CommentaryService.ClassifiedWinner(record)?.Transponder;", source, StringComparison.Ordinal);
        Assert.Contains("&& CommentaryService.ClassifiedWinner(record) is not null;", source, StringComparison.Ordinal);

        // In PrincipalAfter, before the teams-only check, so it holds either way.
        var principalAfter = source.IndexOf("private bool PrincipalAfter(", StringComparison.Ordinal);
        var noWinner = source.IndexOf("nobody finished it.", principalAfter, StringComparison.Ordinal);
        var teamsOnly = source.IndexOf("if (_settings.PrincipalTeamsOnly)", principalAfter, StringComparison.Ordinal);

        Assert.True(principalAfter > 0 && noWinner > principalAfter && noWinner < teamsOnly);
    }

    // --- B36: each car's real clock zero for the recording ----------------------------------

    private static RacerStanding Car(string id, long releaseOffsetMs = 0, long qualifyingZeroMs = -1) => new()
    {
        TransponderId = id,
        ReleaseOffsetMs = releaseOffsetMs,
        QualifyingZeroMs = qualifyingZeroMs,
    };

    [Fact]
    public void A_held_car_on_one_clock_from_the_green_has_no_offset()
    {
        // The scratch driver held 30 s on a handicap (SharedGreen) start: the
        // first lap already runs from the green.
        Assert.Equal(0, RaceControlControl.ClockZeroSinceGreenMs(Car("1234", releaseOffsetMs: 30_000), RaceStartType.StaggeredHandicap, StaggerClock.SharedGreen));
    }

    [Fact]
    public void A_held_car_on_its_own_clock_is_moved_on_by_its_release()
    {
        Assert.Equal(30_000, RaceControlControl.ClockZeroSinceGreenMs(Car("1234", releaseOffsetMs: 30_000), RaceStartType.StaggeredGrid, StaggerClock.OwnRelease));

        // A ghost reports from the green.
        Assert.Equal(0, RaceControlControl.ClockZeroSinceGreenMs(Car(Ghosts.Prefix + "1234", releaseOffsetMs: 30_000), RaceStartType.StaggeredGrid, StaggerClock.OwnRelease));
    }

    [Fact]
    public void A_qualifier_is_moved_on_by_its_first_crossing()
    {
        Assert.Equal(5_100, RaceControlControl.ClockZeroSinceGreenMs(Car("1234", qualifyingZeroMs: 5_100), RaceStartType.Qualifier, StaggerClock.OwnRelease));
        Assert.Equal(0, RaceControlControl.ClockZeroSinceGreenMs(Car("1234"), RaceStartType.Qualifier, StaggerClock.OwnRelease));
    }

    [Fact]
    public void The_recording_is_handed_each_cars_clock_zero_not_its_release_offset()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains("s => ClockZeroSinceGreenMs(s, startType, staggerClock),", source, StringComparison.Ordinal);
        Assert.DoesNotContain("s => s.ReleaseOffsetMs,", source, StringComparison.Ordinal);
    }
}
