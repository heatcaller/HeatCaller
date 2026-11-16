namespace HeatCaller.Tests;

/// <summary>
/// Children's privacy plan 4.1 (15 Nov 2026, Asher's decision): Sailor-mouth
/// plays as Enthusiastic - clean - whenever any junior is in the race being
/// called or is the subject of the line, and the night recap is clean if any
/// junior raced that night.
/// </summary>
public sealed class ChildrensPrivacy_SailorMouthTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

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
            return Task.FromResult<string?>("Mia is four tenths up on Bill.");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Scribe Model) Desk()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "8001", DriverName = "Mia Kent", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "8002", DriverName = "Bill Travers" });

        var model = new Scribe();
        var settings = new AppSettings
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 60,
            CommentaryToneStyle = CommentaryTone.SailorMouth,
            AllowSailorMouth = true,
        };

        return (new CommentaryService(profiles, settings) { Client = model, VoiceSupportsAudioTagsProvider = () => true }, model);
    }

    private static PackedFacts Facts(string text) => new(text, Array.Empty<string>(), 0);

    private static SessionRecord ARace(string first, string firstName) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = new DateTime(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 15, 19, 10, 0, DateTimeKind.Utc),
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = first, DriverName = firstName, Position = 1, Laps = 12, ElapsedMs = 300_000 },
            new() { Transponder = "8003", DriverName = "Ann Other", Position = 2, Laps = 12, ElapsedMs = 300_400 },
        },
    };

    [Fact]
    public void The_tone_for_a_line_is_clean_only_for_sailor_mouth_around_juniors()
    {
        var sailor = new AppSettings { CommentaryToneStyle = CommentaryTone.SailorMouth, AllowSailorMouth = true };
        var pirate = new AppSettings { CommentaryToneStyle = CommentaryTone.Pirate };

        Assert.Equal(CommentaryTone.Enthusiastic, CommentaryTones.Effective(sailor, juniorsAround: true));
        Assert.Equal(CommentaryTone.SailorMouth, CommentaryTones.Effective(sailor, juniorsAround: false));
        Assert.Equal(CommentaryTone.Pirate, CommentaryTones.Effective(pirate, juniorsAround: true));
    }

    [Fact]
    public async Task A_line_about_adults_keeps_sailor_mouth()
    {
        var (desk, model) = Desk();

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Bill Travers leads Ann Other by 0.4s on lap 12."));

        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);
    }

    [Fact]
    public async Task A_line_naming_a_junior_is_clean()
    {
        var (desk, model) = Desk();

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Mia Kent leads Bill Travers by 0.4s on lap 12."));

        Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);
        Assert.Contains("Be LOUD and thrilled", model.SystemPrompt);

        // And the next line, about adults only, is back to Sailor-mouth: the mark lasts one call.
        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Bill Travers leads Ann Other by 0.4s on lap 12."));
        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);
    }

    [Fact]
    public async Task Any_junior_in_the_race_on_track_makes_every_line_clean()
    {
        var (desk, model) = Desk();
        desk.CurrentRaceTransponders = () => new[] { "8001", "8002" };

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Bill Travers leads Ann Other by 0.4s on lap 12."));

        Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);

        desk.CurrentRaceTransponders = () => new[] { "8002" };

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Bill Travers leads Ann Other by 0.4s on lap 12."));

        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);
    }

    [Fact]
    public async Task A_race_recap_with_a_junior_in_it_is_clean()
    {
        var (desk, model) = Desk();

        await desk.BuildRaceSummaryAsync(ARace("8001", "Mia Kent"));
        Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);

        await desk.BuildRaceSummaryAsync(ARace("8002", "Bill Travers"));
        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);
    }

    [Fact]
    public async Task The_night_recap_is_clean_when_a_junior_raced_tonight()
    {
        var (desk, model) = Desk();

        desk.JuniorRacedTonight = () => false;
        await desk.BuildNightRecapAsync(Facts("Bill Travers won the A-Main by 0.4s."));
        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);

        desk.JuniorRacedTonight = () => true;
        await desk.BuildNightRecapAsync(Facts("Bill Travers won the A-Main by 0.4s."));
        Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);
    }

    [Fact]
    public void Main_form_tells_the_commentary_about_juniors()
    {
        // The two hooks are wired where the commentary is made.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "HeatCaller", "HeatCaller")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "MainForm.cs"));

        Assert.Contains("_commentary.CurrentRaceTransponders =", source);
        Assert.Contains("_commentary.JuniorRacedTonight =", source);
    }
}
