namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), CommentaryService: the Team Principal's
/// interviews are written on the thread pool, so they read the drivers from
/// ProfileStore's any-thread index, never the live dictionary the UI thread
/// edits when a walk-up or a new driver is saved.
/// </summary>
public sealed class BugHunt24_CommentaryService_2Tests : IDisposable
{
    /// <summary>Declared first: ProfileStore.Load reads from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A model that hands back one answer and keeps what it was asked.</summary>
    private sealed class Scribe : ICommentaryBrain
    {
        public string Facts { get; private set; } = string.Empty;

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
            Facts = userPrompt;
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>("HOST: Ready?\nTEAM PRINCIPAL: Ready.");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    /// <summary>
    /// Two team-mates saved properly, then taken out of the store's LIVE
    /// dictionary only - the state a pool-thread read can catch while the UI
    /// thread is in the middle of saving a new driver. The any-thread index,
    /// rebuilt at the last finished save, still has them both.
    /// </summary>
    private static ProfileStore StoreCaughtMidSave()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "8001", DriverName = "Ann", Team = "Red Rockets", CarModel = "B7" });
        profiles.Upsert(new DriverProfile { Transponder = "8002", DriverName = "Bob", Team = "Red Rockets" });

        var live = (Dictionary<string, DriverProfile>)typeof(ProfileStore)
            .GetField("_profiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(profiles)!;
        live.Remove("8001");
        live.Remove("8002");

        return profiles;
    }

    private static (CommentaryService Desk, Scribe Model) Desk(ProfileStore profiles)
    {
        var model = new Scribe();
        var desk = new CommentaryService(profiles, new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        return (desk, model);
    }

    [Fact]
    public async Task The_principal_written_ahead_names_the_driver_and_team_from_the_any_thread_index()
    {
        var (desk, model) = Desk(StoreCaughtMidSave());

        var round = new RoundDefinition
        {
            RoundLabel = "A-Main",
            ClassName = "Box Stock",
            Kind = RoundKind.Main,
            MainTier = 0,
            AssignedTransponders = new List<string> { "8001", "8002" },
        };

        await Task.Run(() => desk.BuildPrincipalBeforeAsync(round, "8001", "Pat", null, null));

        Assert.Contains("Driver: Ann", model.Facts);
        Assert.Contains("Driver's team: Red Rockets", model.Facts);
        Assert.Contains("Driver's car: B7", model.Facts);
        Assert.Contains("The team's other drivers in this race: Bob", model.Facts);
        Assert.DoesNotContain("Unnamed", model.Facts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_principal_after_the_race_names_the_winner_and_team_from_the_any_thread_index()
    {
        var (desk, model) = Desk(StoreCaughtMidSave());

        var session = new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 10, 19, 10, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "8001", DriverName = "Ann", Position = 1, Laps = 12, ElapsedMs = 300_000 },
                new() { Transponder = "8002", DriverName = "Bob", Position = 2, Laps = 12, ElapsedMs = 300_400 },
            },
        };

        await Task.Run(() => desk.BuildPrincipalAfterAsync(session, "Pat", null, null));

        Assert.Contains("Winner: Ann", model.Facts);
        Assert.Contains("Winner's car: B7", model.Facts);
        Assert.Contains("Winner's team: Red Rockets", model.Facts);
        Assert.Contains("The team's other drivers in this race: Bob (P2)", model.Facts);
        Assert.DoesNotContain("Unnamed", model.Facts, StringComparison.OrdinalIgnoreCase);
    }
}
