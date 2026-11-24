namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), CommentaryService: the Sailor-mouth check when
/// a junior joins mid-write, a shouted possessive of a kept name, drivers
/// level on points at the top, the winner's thank-you spent by a recap that
/// never said it, the recap's kept names, and the Team Principal's teammates.
/// </summary>
public sealed class BugHunt24_CommentaryService_1Tests : IDisposable
{
    /// <summary>Declared first: ProfileStore.Load reads from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A model that hands back one answer and keeps what it was asked, and can run something as it writes.</summary>
    private sealed class Scribe : ICommentaryBrain
    {
        public string Facts { get; private set; } = string.Empty;

        public string SystemPrompt { get; private set; } = string.Empty;

        public string? Answer { get; set; } = "And that is a wrap on the A-Main!";

        public Action? WhileWriting { get; set; }

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
            Facts = userPrompt;
            WhileWriting?.Invoke();
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Scribe Model) Desk(ProfileStore? profiles = null, AppSettings? settings = null)
    {
        var model = new Scribe();

        var desk = new CommentaryService(
            profiles ?? ProfileStore.Load(),
            settings ?? new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        return (desk, model);
    }

    private static SessionResultEntry Finisher(string name, int position, int laps, long elapsedMs = 300_000) => new()
    {
        Transponder = name,
        DriverName = name,
        Position = position,
        Laps = laps,
        ElapsedMs = elapsedMs,
    };

    private static SessionRecord ARace(params SessionResultEntry[] results) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 10, 10, 19, 10, 0, DateTimeKind.Utc),
        Results = results.ToList(),
    };

    private static RoundDefinition AMain(params string[] roster) => new()
    {
        RoundLabel = "A-Main",
        ClassName = "Box Stock",
        Kind = RoundKind.Main,
        MainTier = 0,
        AssignedTransponders = roster.ToList(),
    };

    private static ChampionshipMove Move(string name, int position, int points, int behind) => new()
    {
        DriverName = name,
        Position = position,
        PreviousPosition = position,
        TotalPoints = points,
        PointsBehindLeader = behind,
        RacedThisTime = true,
    };

    // ------------------------------------------------ #144 (F096): Sailor-mouth and a junior mid-write

    private static (CommentaryService Desk, Scribe Model, List<string> OnTrack) SailorDesk()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "8001", DriverName = "Mia Kent", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "8002", DriverName = "Bill Travers" });

        var (desk, model) = Desk(profiles, new AppSettings
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 60,
            CommentaryToneStyle = CommentaryTone.SailorMouth,
            AllowSailorMouth = true,
            SailorMouthSwears = 2,
        });

        var onTrack = new List<string> { "8002" };
        desk.CurrentRaceTransponders = () => onTrack.ToList();
        model.Answer = "Bill is fucking clear of Ann.";

        return (desk, model, onTrack);
    }

    [Fact]
    public async Task A_junior_who_joins_the_race_while_a_line_is_being_written_still_gets_a_clean_line()
    {
        var (desk, model, onTrack) = SailorDesk();
        model.WhileWriting = () => onTrack.Add("8001");

        var line = await desk.BuildSegmentAsync(SegmentKind.BattleWatch, new PackedFacts("Bill Travers leads Ann Other on lap 12.", Array.Empty<string>(), 0));

        // Written as Sailor-mouth - the junior was not there yet...
        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);

        // ...but said with no swearing, as a junior is in the race now.
        Assert.DoesNotContain("fucking", line ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_sailor_mouth_line_with_no_junior_around_keeps_to_the_dial_as_before()
    {
        var (desk, _, _) = SailorDesk();

        var line = await desk.BuildSegmentAsync(SegmentKind.BattleWatch, new PackedFacts("Bill Travers leads Ann Other on lap 12.", Array.Empty<string>(), 0));

        Assert.Equal("Bill is fucking clear of Ann.", line);
    }

    // ------------------------------------------------ #145 (F097): "SAM'S" is "Sam's"

    [Fact]
    public void A_shouted_possessive_of_a_kept_name_comes_back_with_a_small_s()
    {
        var calmed = CommentaryService.CalmShouting("SAM'S GOT THE LEAD AND HE IS FLYING!", new[] { "Sam" }, out var shouted, out _);

        Assert.Equal("Sam's got the lead and he is flying!", calmed);
        Assert.True(shouted);

        Assert.Equal(
            "Sam’s pace is great",
            CommentaryService.CalmShouting("SAM’S PACE IS GREAT", new[] { "Sam" }, out _, out _));
    }

    [Fact]
    public void A_possessive_already_in_lower_case_and_a_second_kept_name_are_left_alone()
    {
        Assert.Equal("Sam's car is quick.", CommentaryService.CalmShouting("Sam's car is quick.", new[] { "Sam" }, out _, out _));

        // Another kept name inside the same word keeps its own spelling.
        Assert.Equal(
            "Sam-Bill battle is on",
            CommentaryService.CalmShouting("SAM-BILL BATTLE IS ON", new[] { "Sam", "Bill" }, out _, out _));
    }

    // ------------------------------------------------ #14 (F098): level on points is not leading

    [Fact]
    public void The_gap_words_say_level_for_a_tie_and_leads_only_for_a_driver_alone_on_top()
    {
        Assert.Equal("leads the championship", CommentaryService.ChampionshipGapWords(1, 0, levelAtTop: false));
        Assert.Equal("level on points at the top of the championship", CommentaryService.ChampionshipGapWords(1, 0, levelAtTop: true));
        Assert.Equal("level on points at the top of the championship", CommentaryService.ChampionshipGapWords(2, 0, levelAtTop: true));
        Assert.Equal("7 points behind the leader", CommentaryService.ChampionshipGapWords(2, 7, levelAtTop: true));
    }

    [Fact]
    public async Task A_recap_with_two_drivers_level_at_the_top_says_neither_leads()
    {
        var (desk, model) = Desk();
        var bill = Move("Bill", 1, 61, 0);

        await desk.BuildRaceSummaryAsync(
            ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450)),
            championship: new ChampionshipSwing
            {
                SeasonName = "Winter Series",
                Moves = new[] { bill, Move("Sam", 2, 61, 0) },
                Leader = bill,
                RoundsCounted = 2,
            });

        Assert.Contains("P1 Bill - 61 points, level on points at the top of the championship", model.Facts);
        Assert.Contains("P2 Sam - 61 points, level on points at the top of the championship", model.Facts);
        Assert.DoesNotContain("leads the championship", model.Facts);
    }

    [Fact]
    public async Task A_recap_level_with_a_leader_who_did_not_race_does_not_call_that_leader_the_leader()
    {
        var (desk, model) = Desk();

        await desk.BuildRaceSummaryAsync(
            ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450)),
            championship: new ChampionshipSwing
            {
                SeasonName = "Winter Series",
                Moves = new[] { Move("Sam", 2, 61, 0) },
                Leader = new ChampionshipMove
                {
                    DriverName = "Alex",
                    Position = 1,
                    PreviousPosition = 1,
                    TotalPoints = 61,
                    PointsBehindLeader = 0,
                    RacedThisTime = false,
                },
                RoundsCounted = 2,
            });

        Assert.Contains("P2 Sam - 61 points, level on points at the top of the championship", model.Facts);
        Assert.Contains("Level on points at the top with them, who did not race here: Alex", model.Facts);
        Assert.DoesNotContain("Championship leader, who did not race here", model.Facts);
        Assert.DoesNotContain("leads the championship", model.Facts);
    }

    [Fact]
    public async Task A_race_intro_with_two_drivers_level_at_the_top_says_neither_leads()
    {
        var (desk, model) = Desk();

        await desk.BuildRoundIntroAsync(
            AMain("Sam", "Bill"),
            stake: new ChampionshipAtStake(
                "Winter Series",
                2,
                "Bill",
                LeaderInThisRace: true,
                new[]
                {
                    new ChampionshipStanding("Bill", "Bill", 1, 1, 61, 0),
                    new ChampionshipStanding("Sam", "Sam", 2, 2, 61, 0),
                }));

        Assert.Contains("P1 Bill, in this race - 61 points, level on points at the top of the championship", model.Facts);
        Assert.Contains("P2 Sam, in this race - 61 points, level on points at the top of the championship", model.Facts);
        Assert.DoesNotContain("leads the championship", model.Facts);
    }

    // ------------------------------------------------ #146 (F099): a thank-you a recap never said goes back

    private const string Thanks = "my brother Jamie and Fast Eddie's hobby shop";

    private static (CommentaryService Desk, Scribe Model) CrewDesk()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam", CrewThanks = Thanks }, out _));

        return Desk(profiles, new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60, DriversPickCrew = true });
    }

    private static SessionRecord SamWins(string id) => new()
    {
        Id = id,
        Label = "Heat 1",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = new DateTime(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 15, 19, 10, 0, DateTimeKind.Utc),
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = "100", DriverName = "Sam", Position = 1, Laps = 12, ElapsedMs = 300_000 },
            new() { Transponder = "Bill", DriverName = "Bill", Position = 2, Laps = 12, ElapsedMs = 300_450 },
        },
    };

    private static bool PrincipalStillGetsIt(CommentaryService desk) =>
        desk.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "principal:race2").Length > 0;

    [Fact]
    public async Task A_recap_that_never_comes_back_hands_the_thank_you_back()
    {
        var (desk, model) = CrewDesk();
        model.Answer = null;

        await desk.BuildRaceSummaryAsync(SamWins("race1"));

        Assert.Contains("Who Sam would like to thank", model.Facts);
        Assert.True(PrincipalStillGetsIt(desk));
    }

    [Fact]
    public async Task A_recap_that_leaves_the_thank_you_out_hands_it_back()
    {
        var (desk, model) = CrewDesk();
        model.Answer = "Sam wins from Bill after a clean drive.";

        await desk.BuildRaceSummaryAsync(SamWins("race1"));

        Assert.True(PrincipalStillGetsIt(desk));
    }

    [Fact]
    public async Task A_recap_that_says_the_thank_you_keeps_it_until_it_is_dropped_unheard()
    {
        var (desk, model) = CrewDesk();
        model.Answer = "Sam wins from Bill, and Sam thanks his brother Jamie and Fast Eddie's hobby shop.";

        var recap = await desk.BuildRaceSummaryAsync(SamWins("race1"));

        Assert.False(string.IsNullOrWhiteSpace(recap));
        Assert.False(PrincipalStillGetsIt(desk));

        // The next race was called first and the recap was never said.
        desk.ForgetRaceSummary(recap);

        Assert.True(PrincipalStillGetsIt(desk));
    }

    // ------------------------------------------------ #147 (F100): the recap keeps the championship's spelling

    [Fact]
    public async Task A_recap_keeps_a_championship_the_club_typed_in_capitals()
    {
        var (desk, model) = Desk();
        var sam = Move("Sam", 1, 61, 0);
        model.Answer = "WHAT A RACE! Sam is flying in the HFCC WINTER SERIES.";

        var recap = await desk.BuildRaceSummaryAsync(
            ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450)),
            championship: new ChampionshipSwing
            {
                SeasonName = "HFCC WINTER SERIES",
                Moves = new[] { sam },
                Leader = sam,
                RoundsCounted = 2,
            });

        Assert.Contains("HFCC WINTER SERIES", recap);
        Assert.DoesNotContain("WHAT A RACE", recap);
    }

    [Fact]
    public void The_recap_fact_names_carry_the_championship_the_mains_and_the_classes()
    {
        var alex = new ChampionshipMove
        {
            DriverName = "OZZY",
            Position = 1,
            PreviousPosition = 1,
            TotalPoints = 70,
            PointsBehindLeader = 0,
            RacedThisTime = false,
        };

        var names = CommentaryService.RecapFactNames(
            new ChampionshipSwing { SeasonName = "HFCC WINTER SERIES", Moves = new[] { Move("Sam", 2, 61, 9) }, Leader = alex, RoundsCounted = 3 },
            new MainsOnLayout("THE BOWL", "Box Stock", 4, "Carl", "200", 3),
            new string?[] { " MOD ", null }).ToList();

        Assert.Contains("HFCC WINTER SERIES", names);
        Assert.Contains("OZZY", names);
        Assert.Contains("THE BOWL", names);
        Assert.Contains("Carl", names);
        Assert.Contains("MOD", names);
    }

    // ------------------------------------------------ #15 (F101): a teammate out of the race has no place

    [Fact]
    public void A_teammate_who_did_not_finish_or_was_disqualified_is_not_given_a_place()
    {
        Assert.Equal("Bob (P2)", CommentaryService.TeammateLine("Bob", new SessionResultEntry { Position = 2, Laps = 12 }));
        Assert.Equal("Bob (did not finish)", CommentaryService.TeammateLine("Bob", new SessionResultEntry { Position = 6, Laps = 5, Outcome = ResultEditor.Dnf }));
        Assert.Equal("Bob (disqualified)", CommentaryService.TeammateLine("Bob", new SessionResultEntry { Position = 6, Laps = 12, Outcome = ResultEditor.Dq }));
        Assert.Equal("Bob (did not finish)", CommentaryService.TeammateLine("Bob", new SessionResultEntry { Position = 6, Laps = 0 }));
    }

    [Fact]
    public async Task The_principal_is_told_a_disqualified_teammate_was_disqualified_not_sixth()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "A1", DriverName = "Ann", Team = "Red Rockets" });
        profiles.Upsert(new DriverProfile { Transponder = "B1", DriverName = "Bob", Team = "Red Rockets" });

        var (desk, model) = Desk(profiles);

        await desk.BuildPrincipalAfterAsync(
            ARace(
                new SessionResultEntry { Transponder = "A1", DriverName = "Ann", Position = 1, Laps = 12, ElapsedMs = 300_000 },
                new SessionResultEntry { Transponder = "C1", DriverName = "Cy", Position = 2, Laps = 12, ElapsedMs = 300_400 },
                new SessionResultEntry { Transponder = "B1", DriverName = "Bob", Position = 6, Laps = 12, ElapsedMs = 300_900, Outcome = ResultEditor.Dq }),
            "Pat",
            null,
            null);

        Assert.Contains("The team's other drivers in this race: Bob (disqualified)", model.Facts);
        Assert.DoesNotContain("Bob (P6)", model.Facts);
    }

    [Fact]
    public void The_principal_is_not_handed_a_rivalry_whose_driver_only_shares_letters_with_theirs()
    {
        Assert.False(CommentaryService.NamesDriver("Rivalry result: Samantha Lee finished ahead of Bill (P2 vs P4)", "Sam"));
        Assert.True(CommentaryService.NamesDriver("Rivalry result: Sam finished ahead of Bill (P2 vs P4)", "Sam"));
        Assert.True(CommentaryService.NamesDriver("Rivalry result: Bill finished ahead of SAM LEE (P2 vs P4)", "Sam Lee"));
        Assert.False(CommentaryService.NamesDriver("Rivalry result: Sam finished ahead of Bill", " "));
    }

    [Fact]
    public async Task A_race_intro_leader_marked_level_with_a_driver_off_this_grid_is_not_said_to_lead()
    {
        var (desk, model) = Desk();

        await desk.BuildRoundIntroAsync(
            AMain("Bill"),
            stake: new ChampionshipAtStake(
                "Winter Series",
                2,
                "Bill",
                LeaderInThisRace: true,
                new[] { new ChampionshipStanding("Bill", "Bill", 1, 1, 61, 0, LevelAtTopBefore: true) }));

        Assert.Contains("P1 Bill, in this race - 61 points, level on points at the top of the championship", model.Facts);
        Assert.DoesNotContain("leads the championship", model.Facts);
    }
}
