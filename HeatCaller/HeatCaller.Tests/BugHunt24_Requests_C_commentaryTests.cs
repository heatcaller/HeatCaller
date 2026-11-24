namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), follow-up requests for the commentary layer:
/// the pieces the first round's fixers needed in files they did not own -
/// the lead change tally put back, a battle line binned unheard giving back
/// its fact, cooldown and promise, the nights on file behind "first ever",
/// a title level at the top after the last round, the Sailor-mouth dial in
/// every prompt, the rivalries read from any thread, and namesakes in the
/// achievements read.
/// </summary>
public sealed class BugHunt24_Requests_C_commentaryTests : IDisposable
{
    /// <summary>Declared first: the stores load from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Stock = "Box Stock";

    private static string Id(string name) => name.ToLowerInvariant();

    // ------------------------------------------------ the lead change tally

    [Fact]
    public void BugHunt24_A_lead_change_tally_put_back_carries_on_from_where_it_was()
    {
        var leads = new LeadTracker();

        leads.Observe("A");
        leads.Observe("B");
        leads.Observe("A");

        var (changes, timesLed) = leads.Snapshot();

        Assert.Equal(2, changes);
        Assert.Equal(2, timesLed["A"]);
        Assert.Equal(1, timesLed["B"]);

        // An undone Stage, or a crash: the tracker starts again from nothing.
        leads.Reset();
        leads.Restore(changes, timesLed);

        Assert.Equal(2, leads.Changes);
        Assert.Equal(2, leads.Leaders);

        // The first car seen in front after it is not a change.
        Assert.Null(leads.Observe("B"));

        var next = leads.Observe("A");

        Assert.NotNull(next);
        Assert.Equal(3, next!.Number);
        Assert.Equal(2, next.Leaders);
        Assert.Equal(3, next.TimesLed);
    }

    [Fact]
    public void BugHunt24_A_lead_change_snapshot_is_a_copy()
    {
        var leads = new LeadTracker();

        leads.Observe("A");
        leads.Observe("B");

        var (_, timesLed) = leads.Snapshot();

        leads.Observe("C");

        Assert.Equal(2, timesLed.Count);
        Assert.False(timesLed.ContainsKey("C"));
    }

    // ------------------------------------------------ a battle line nobody heard

    [Fact]
    public void BugHunt24_A_forgotten_fact_is_no_longer_spent()
    {
        var said = new SaidTonight();

        said.Mark("battle:heat-1:a+b");
        Assert.True(said.Spent("battle:heat-1:a+b"));

        said.Forget("battle:heat-1:a+b");

        Assert.False(said.Spent("battle:heat-1:a+b"));
    }

    [Fact]
    public void BugHunt24_A_battle_line_binned_unheard_lifts_the_pairs_cooldown()
    {
        var now = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Local);
        var watch = new BattleWatch(() => now);

        for (var tick = 0; tick < 40; tick++)
        {
            watch.Observe(new[] { new PairGap("A", "B", 1, 0.40, 0) });
            now = now.AddSeconds(0.5);
        }

        var battle = watch.Pick();

        Assert.NotNull(battle);

        watch.Said(battle!);
        Assert.Null(watch.Pick());

        // Either order of the two.
        watch.Unsaid("B", "A");

        Assert.NotNull(watch.Pick());
    }

    private const string Carl = "111";
    private const string Dave = "222";
    private const string Sam = "333";

    private static string? NameFor(string transponder) => transponder switch
    {
        Carl => "Carl Dodd",
        Dave => "Dave Bellamy",
        Sam => "Sam Teague",
        _ => null,
    };

    private static PackedFacts Pack(params string[] themes) =>
        new("some fact or other", new[] { "fact:1" }, 0) { Themes = themes };

    [Fact]
    public void BugHunt24_A_battle_line_binned_unheard_takes_its_promise_back_without_a_lapse()
    {
        var ledger = new NightPromises();
        var at = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Local);

        ledger.Note(SegmentKind.BattleWatch, Pack(FactTheme.Rivalry(Carl, Dave)), NameFor, bothVoices: false, at);
        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Rivalry(Carl, Sam)), NameFor, bothVoices: false, at);

        Assert.Equal(2, ledger.Held);

        ledger.Withdraw(Dave, Carl);

        // Only the battle watch's promise, and not counted as lapsed.
        Assert.Equal(1, ledger.Held);
        Assert.Equal(0, ledger.Lapsed);

        // A form guide about a pair is not a battle watch's to take back.
        ledger.Withdraw(Carl, Sam);

        Assert.Equal(1, ledger.Held);
    }

    [Fact]
    public void BugHunt24_The_director_gives_back_everything_a_binned_battle_line_spent()
    {
        var body = MethodBody(
            File.ReadAllText(Path.Combine(SourceFolder(), "CommentaryDirector.cs")).Replace("\r\n", "\n"),
            "public void BattleUnheard(string aheadId, string behindId)",
            "/// <summary>The flag is out.");

        Assert.Contains("BattleUnheard();", body, StringComparison.Ordinal);
        Assert.Contains("Said.Forget($\"battle:{_raceKey}:{CommentaryFacts.Pair(aheadId, behindId)}\");", body, StringComparison.Ordinal);
        Assert.Contains("Battles.Unsaid(aheadId, behindId);", body, StringComparison.Ordinal);
        Assert.Contains("Promises.Withdraw(aheadId, behindId);", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------ the nights on file

    private static SessionRecord Raced(string id, DateTime startedLocal, RaceSessionType type = RaceSessionType.Race) => new()
    {
        Id = id,
        SessionType = type,
        Label = "A-Main",
        StartedAtUtc = startedLocal.ToUniversalTime(),
        EndedAtUtc = startedLocal.AddMinutes(8).ToUniversalTime(),
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = "sam", DriverName = "Sam", Position = 1, Laps = 12 },
        },
    };

    [Fact]
    public void BugHunt24_Nights_on_file_counts_race_nights_before_tonight_six_to_six()
    {
        var tonight = new DateTime(2026, 11, 21, 20, 0, 0, DateTimeKind.Local);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Raced("a", new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Local)),

            // Two in the morning is still the night of the 14th.
            Raced("b", new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Local)),
            Raced("c", new DateTime(2026, 11, 18, 19, 0, 0, DateTimeKind.Local)),

            // Tonight's own race is not "before tonight".
            Raced("d", new DateTime(2026, 11, 21, 19, 0, 0, DateTimeKind.Local)),

            // Practice is not a race night.
            Raced("e", new DateTime(2026, 11, 10, 19, 0, 0, DateTimeKind.Local), RaceSessionType.Practice),
        });

        Assert.Equal(2, CommentaryReaders.NightsOnFile(null, history, RoundDefinitionStore.Load(), tonight));
    }

    [Fact]
    public void BugHunt24_The_intro_recap_and_grid_history_pass_the_nights_on_file_on()
    {
        var service = File.ReadAllText(Path.Combine(SourceFolder(), "CommentaryService.cs"));
        var director = File.ReadAllText(Path.Combine(SourceFolder(), "CommentaryDirector.cs"));

        Assert.Contains("nightsOnFile: form?.NightsOnFile", service, StringComparison.Ordinal);
        Assert.Contains("nightsOnFile: history.NightsOnFile", service, StringComparison.Ordinal);
        Assert.Contains("nightsOnFile: GridNightsOnFile?.Invoke()", director, StringComparison.Ordinal);
    }

    // ------------------------------------------------ the championship, level at the top

    private sealed record Club(
        SeasonStore Seasons,
        RoundDefinitionStore Rounds,
        RaceEventStore Days,
        SessionHistoryStore History,
        ProfileStore Profiles,
        RoundDefinition Tonight,
        IReadOnlyList<SessionRecord> Past)
    {
        public IReadOnlyList<ChampionshipStanding> After(params string[] order) =>
            CommentaryReaders.ChampionshipAfter(Results(order), Tonight, Seasons, Rounds, Days, History, Profiles);
    }

    private static List<SessionResultEntry> Results(IEnumerable<string> order) =>
        order
            .Select((name, place) => new SessionResultEntry
            {
                Transponder = Id(name),
                DriverName = name,
                Position = place + 1,
                Laps = 12,
            })
            .ToList();

    /// <summary>
    /// Bill, Sam, Carl and Dot over the given past rounds, one race day
    /// each, on the 10-down-to-1 ladder, and tonight's A-main on a day of
    /// its own - the setup KnownIssues4Tests uses, with the orders given.
    /// </summary>
    private static Club Setup(int planned, params string[][] pastOrders)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in new[] { "Bill", "Sam", "Carl", "Dot" })
        {
            profiles.Upsert(new DriverProfile { Transponder = Id(name), DriverName = name });
        }

        var rounds = RoundDefinitionStore.Load();
        var events = RaceEventStore.Load();
        var races = new List<SessionRecord>();
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < pastOrders.Length; i++)
        {
            var past = new RoundDefinition { ClassName = Stock, RoundLabel = "A-Main", Kind = RoundKind.Main };
            var race = new SessionRecord
            {
                Id = "r" + i,
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                RoundId = past.Id,
                StartedAtUtc = when.AddDays(7 * i),
                EndedAtUtc = when.AddDays(7 * i).AddMinutes(8),
                Results = Results(pastOrders[i]),
            };

            past.LinkedSessionId = race.Id;
            rounds.Add(past);
            races.Add(race);

            events.Upsert(new RaceEvent
            {
                Name = "Round " + (i + 1),
                StartedUtc = when.AddDays(7 * i),
                FinishedUtc = when.AddDays(7 * i).AddHours(4),
                RoundIds = new List<string> { past.Id },
                ClassNames = new List<string> { Stock },
            });
        }

        var tonight = new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            AssignedTransponders = new[] { "Bill", "Sam", "Carl", "Dot" }.Select(Id).ToList(),
        };

        rounds.Add(tonight);

        events.Upsert(new RaceEvent
        {
            Name = "Tonight",
            StartedUtc = when.AddDays(7 * pastOrders.Length),
            RoundIds = new List<string> { tonight.Id },
            ClassNames = new List<string> { Stock },
        });

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = Stock,
            AutoInclude = true,
            MainsOnly = true,
            PlannedRounds = planned,
            PointsScheme = Enumerable.Range(1, 10)
                .Select(p => new PointsSchemeEntry { Position = p, Points = 11 - p })
                .ToList(),
        });

        return new Club(seasons, rounds, events, SessionHistoryStore.FromSessions(races), profiles, tonight, races);
    }

    private static readonly string[] BillSamCarlDot = { "Bill", "Sam", "Carl", "Dot" };
    private static readonly string[] SamBillCarlDot = { "Sam", "Bill", "Carl", "Dot" };
    private static readonly string[] SamCarlBillDot = { "Sam", "Carl", "Bill", "Dot" };

    [Fact]
    public void BugHunt24_After_the_last_round_ends_level_nobody_is_said_to_still_be_able_to_win_it()
    {
        // Bill 38, Sam 37 after four rounds; Sam wins round five of five and
        // Bill is second: both on 47, Carl and Dot below.
        var club = Setup(5, BillSamCarlDot, BillSamCarlDot, BillSamCarlDot, SamCarlBillDot);
        var table = club.After(SamBillCarlDot);

        Assert.Equal(47, table[0].Points);
        Assert.Equal(47, table[1].Points);
        Assert.All(table, row => Assert.True(row.StillAlive));

        var said = CommentaryReportFacts.ChampionshipCheck(table, Stock, "heat-1");

        Assert.Contains(said, block => block.Key.StartsWith("champlead", StringComparison.Ordinal)
                                       && block.Text.Contains("level at the top", StringComparison.Ordinal));
        Assert.DoesNotContain(said, block => block.Key.StartsWith("champalive", StringComparison.Ordinal)
                                          || block.Key.StartsWith("champdecided", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_A_clear_leader_after_the_last_round_still_cannot_be_caught()
    {
        // The same club, but Bill wins round five: 48 to Sam's 46.
        var club = Setup(5, BillSamCarlDot, BillSamCarlDot, BillSamCarlDot, SamCarlBillDot);
        var table = club.After(BillSamCarlDot);

        Assert.Equal("Bill", table[0].Name);
        Assert.True(table[0].StillAlive);
        Assert.All(table.Skip(1), row => Assert.False(row.StillAlive));

        var said = CommentaryReportFacts.ChampionshipCheck(table, Stock, "heat-1");

        Assert.Contains(said, block => block.Text.Contains("nobody can catch Bill", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_A_grid_leader_level_with_somebody_not_on_the_grid_is_marked_level_before_the_race()
    {
        // Bill and Sam swap the win every round: 38 each. Only Bill is on
        // this grid.
        var club = Setup(6, BillSamCarlDot, SamBillCarlDot, BillSamCarlDot, SamBillCarlDot);

        var stake = CommentaryReaders.ChampionshipBefore(
            club.Tonight, new[] { Id("Bill") }, club.Seasons, club.Rounds, club.Days, club.History, club.Profiles);

        Assert.NotNull(stake);
        Assert.True(stake!.OnThisGrid.Single().LevelAtTopBefore);
    }

    [Fact]
    public void BugHunt24_A_grid_leader_alone_at_the_top_is_not_marked_level()
    {
        var club = Setup(6, BillSamCarlDot, BillSamCarlDot, BillSamCarlDot, BillSamCarlDot);

        var stake = CommentaryReaders.ChampionshipBefore(
            club.Tonight, new[] { Id("Bill") }, club.Seasons, club.Rounds, club.Days, club.History, club.Profiles);

        Assert.NotNull(stake);
        Assert.False(stake!.OnThisGrid.Single().LevelAtTopBefore);
    }

    [Fact]
    public void BugHunt24_The_championship_swing_says_when_the_top_is_level()
    {
        var level = Setup(6, BillSamCarlDot, SamBillCarlDot, BillSamCarlDot, SamBillCarlDot);
        var swing = Championship.SwingFor(level.Past[^1], level.Seasons, level.Rounds, level.Days, level.History, level.Profiles);

        Assert.NotNull(swing);
        Assert.True(swing!.LevelAtTop);
    }

    [Fact]
    public void BugHunt24_The_championship_swing_does_not_say_level_for_a_leader_alone()
    {
        var clear = Setup(6, BillSamCarlDot, BillSamCarlDot, BillSamCarlDot, BillSamCarlDot);
        var alone = Championship.SwingFor(clear.Past[^1], clear.Seasons, clear.Rounds, clear.Days, clear.History, clear.Profiles);

        Assert.NotNull(alone);
        Assert.False(alone!.LevelAtTop);
    }

    // ------------------------------------------------ the swear dial in every prompt

    private sealed class Scribe : ICommentaryBrain
    {
        public string SystemPrompt { get; private set; } = string.Empty;

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
            SystemPrompt = systemPrompt;
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>("Sam is four tenths up on Bill.");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    [Fact]
    public async Task BugHunt24_Sailor_mouth_at_dial_zero_writes_a_segment_without_the_f_bomb_coaching()
    {
        var model = new Scribe();
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 60,
            CommentaryToneStyle = CommentaryTone.SailorMouth,
            AllowSailorMouth = true,
            SailorMouthSwears = 0,
        })
        {
            Client = model,
            VoiceSupportsAudioTagsProvider = () => true,
        };

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, new PackedFacts("Sam leads Bill by 0.4s on lap 12.", Array.Empty<string>(), 0));

        Assert.Contains("SWEARING: none at all", model.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("the f-word is your go-to", model.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("no swear words", model.SystemPrompt, StringComparison.Ordinal);
    }

    // ------------------------------------------------ the rivalries, from any thread

    [Fact]
    public void BugHunt24_Rivalries_read_before_a_change_are_left_as_they_were()
    {
        var store = RivalryStore.Load();

        store.Upsert(new RivalryLine { DriverA = "Sam", DriverB = "Bill" });

        var before = store.All;

        store.Upsert(new RivalryLine { DriverA = "Carl", DriverB = "Dot" });

        Assert.Single(before);
        Assert.Equal(2, store.All.Count);

        store.Delete(store.All[0].Id);

        Assert.Single(store.All);
        Assert.Equal("Carl", store.All[0].DriverA);
    }

    // ------------------------------------------------ namesakes in the achievements read

    [Fact]
    public void BugHunt24_An_achievements_read_with_two_drivers_of_one_name_is_turned_down_cleanly()
    {
        var picks = new[]
        {
            new HonoursPick("Sam Jones", "101", new[] { "Hat-Trick" }, Array.Empty<AchievementUnlock>()),
            new HonoursPick("Sam Jones", "202", new[] { "Clean Sweep" }, Array.Empty<AchievementUnlock>()),
        };

        var notes = new List<string>();

        var read = HonoursWriting.Check("Sam Jones takes Hat-Trick and Sam Jones takes Clean Sweep.", picks, null, "Hat-Trick. Clean Sweep.", notes);

        Assert.Null(read);
        Assert.Contains(notes, note => note.Contains("share a name", StringComparison.Ordinal));
    }

    // ------------------------------------------------ helpers

    private static string SourceFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
    }

    private static string MethodBody(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);

        Assert.True(from >= 0, $"Could not find {start}");

        var to = source.IndexOf(end, from, StringComparison.Ordinal);

        Assert.True(to > from, $"Could not find {end} after {start}");

        return source[from..to];
    }
}
