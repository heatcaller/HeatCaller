namespace HeatCaller.Tests;

/// <summary>
/// The character tones (8 Nov 2026): nine writing tones beyond Enthusiastic,
/// Neutral and Dry, each kept as one entry in CommentaryTones. What is
/// checked here is the plumbing and the promises - that each tone reaches
/// every prompt, keeps its truth rule, and gets its own audio tags - not how
/// well a model plays the part.
/// </summary>
public sealed class CharacterToneTests : IDisposable
{
    /// <summary>Declared first: ProfileStore.Load reads from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    public static TheoryData<CommentaryTone> Characters()
    {
        var data = new TheoryData<CommentaryTone>();

        foreach (var tone in CommentaryTones.All)
        {
            data.Add(tone.Tone);
        }

        return data;
    }

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

    private static (CommentaryService Desk, Scribe Model) Desk(CommentaryTone tone, bool tags = true, bool v3 = true)
    {
        var model = new Scribe();
        var settings = new AppSettings
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 60,
            CommentaryToneStyle = tone,
            CommentaryVoiceTags = tags,
        };

        var desk = new CommentaryService(ProfileStore.Load(), settings)
        {
            Client = model,
            VoiceSupportsAudioTagsProvider = () => v3,
        };

        return (desk, model);
    }

    private static PackedFacts Facts(string text) => new(text, Array.Empty<string>(), 0);

    private static SessionRecord ARace() => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 10, 10, 19, 10, 0, DateTimeKind.Utc),
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = "Sam", DriverName = "Sam", Position = 1, Laps = 12, ElapsedMs = 300_000 },
            new() { Transponder = "Bill", DriverName = "Bill", Position = 2, Laps = 12, ElapsedMs = 300_400 },
        },
    };

    // ------------------------------------------------------------------
    //  The table
    // ------------------------------------------------------------------

    [Fact]
    public void Every_new_tone_has_an_entry_and_the_first_three_keep_their_own_wording()
    {
        var characters = Enum.GetValues<CommentaryTone>()
            .Except(new[] { CommentaryTone.Enthusiastic, CommentaryTone.Neutral, CommentaryTone.DryUnderstated });

        Assert.All(characters, tone => Assert.NotNull(CommentaryTones.Find(tone)));

        Assert.Null(CommentaryTones.Find(CommentaryTone.Enthusiastic));
        Assert.Null(CommentaryTones.Find(CommentaryTone.Neutral));
        Assert.Null(CommentaryTones.Find(CommentaryTone.DryUnderstated));
    }

    [Fact]
    public void The_saved_numbers_of_the_first_three_tones_never_move()
    {
        // Stored as an int - a tone inserted above these would quietly turn
        // everyone's saved Dry into something else.
        Assert.Equal(0, (int)CommentaryTone.Enthusiastic);
        Assert.Equal(1, (int)CommentaryTone.Neutral);
        Assert.Equal(2, (int)CommentaryTone.DryUnderstated);
        Assert.All(CommentaryTones.All, tone => Assert.True((int)tone.Tone > 2));
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void Every_tone_is_labelled_with_its_group_and_carries_a_worked_example(CommentaryTone tone)
    {
        var profile = CommentaryTones.Find(tone)!;

        Assert.Matches("^(Dark|Silly|Distorted): ", profile.Label);
        Assert.Contains("given \"P1 leads P2 by 0.4s on lap 12\"", profile.ToneLine);
        Assert.False(string.IsNullOrWhiteSpace(profile.Segment));
        Assert.False(string.IsNullOrWhiteSpace(profile.Exchange));
        Assert.False(string.IsNullOrWhiteSpace(profile.Close));
        Assert.False(string.IsNullOrWhiteSpace(profile.Host));
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void A_tone_tag_is_one_the_strip_pattern_takes_off_again(CommentaryTone tone)
    {
        var profile = CommentaryTones.Find(tone)!;

        foreach (var tag in new[] { profile.Opener, profile.Sentence }.OfType<string>())
        {
            Assert.Matches(@"^\[[a-zA-Z ]{1,24}\]$", tag);
            Assert.Equal("Lap twelve.", VoiceProfile.StripAudioTags($"{tag} Lap twelve.").Trim());
        }
    }

    // ------------------------------------------------------------------
    //  The prompts
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Characters))]
    public async Task A_segment_is_written_in_character_and_under_the_truth_rule(CommentaryTone tone)
    {
        var (desk, model) = Desk(tone);
        var profile = CommentaryTones.Find(tone)!;

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Contains(profile.ToneLine, model.SystemPrompt);
        Assert.Contains(CommentaryTones.CharacterRule, model.SystemPrompt);
        Assert.Contains(profile.Segment, model.SystemPrompt);
        Assert.Contains("Never invent names, times, positions or events", model.SystemPrompt);
        Assert.DoesNotContain("hype-man", model.SystemPrompt);
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public async Task A_recap_closes_in_character(CommentaryTone tone)
    {
        var (desk, model) = Desk(tone);
        var profile = CommentaryTones.Find(tone)!;

        await desk.BuildRaceSummaryAsync(ARace());

        Assert.Contains(profile.Close, model.SystemPrompt);
        Assert.Contains(CommentaryTones.CharacterRule, model.SystemPrompt);
        Assert.DoesNotContain("high-energy trackside broadcaster", model.SystemPrompt);
    }

    [Fact]
    public async Task Enthusiastic_is_still_written_exactly_as_it_was()
    {
        var (desk, model) = Desk(CommentaryTone.Enthusiastic);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Contains("Be LOUD and thrilled", model.SystemPrompt);
        Assert.DoesNotContain(CommentaryTones.CharacterRule, model.SystemPrompt);
    }

    // ------------------------------------------------------------------
    //  The audio tags
    // ------------------------------------------------------------------

    [Fact]
    public void Eldritch_whispers_every_sentence()
    {
        var (desk, _) = Desk(CommentaryTone.Eldritch);

        Assert.Equal(
            "[whispers] The hairpin stirs. [whispers] Sam is four tenths clear.",
            desk.PrepareSummaryForSpeech("The hairpin stirs. Sam is four tenths clear."));
    }

    [Fact]
    public void Noir_sighs_once_and_leaves_the_rest_to_the_voice()
    {
        var (desk, _) = Desk(CommentaryTone.Noir);

        Assert.Equal(
            "[sighs] Lap twelve. Sam's four tenths up.",
            desk.PrepareSummaryForSpeech("Lap twelve. Sam's four tenths up."));
    }

    [Fact]
    public void A_character_cold_open_keeps_its_own_opener_rather_than_the_shout()
    {
        var (desk, _) = Desk(CommentaryTone.Villain);

        Assert.Equal(
            "[laughs] Welcome. [mischievously] Sam leads.",
            desk.PrepareSegmentForSpeech(SegmentKind.ColdOpen, "Welcome. Sam leads."));
    }

    [Fact]
    public void Enthusiastic_tags_are_unchanged()
    {
        var (desk, _) = Desk(CommentaryTone.Enthusiastic);

        Assert.Equal("[excited] Sam wins! [excited] By a tenth!", desk.PrepareSummaryForSpeech("Sam wins! By a tenth!"));
        Assert.Equal("[shouting] Hello! [excited] Sam leads!", desk.PrepareSegmentForSpeech(SegmentKind.ColdOpen, "Hello! Sam leads!"));
    }

    [Theory]
    [InlineData(CommentaryTone.Neutral)]
    [InlineData(CommentaryTone.DryUnderstated)]
    public void The_calm_tones_stay_untagged(CommentaryTone tone)
    {
        var (desk, _) = Desk(tone);

        Assert.False(CommentaryTones.ReadsTags(tone));
        Assert.Equal("Sam wins. By a tenth.", desk.PrepareSummaryForSpeech("Sam wins. By a tenth."));
    }

    [Fact]
    public void No_tags_without_a_v3_voice_or_with_the_box_unticked()
    {
        Assert.Equal("The hairpin stirs.", Desk(CommentaryTone.Eldritch, v3: false).Desk.PrepareSummaryForSpeech("The hairpin stirs."));
        Assert.Equal("The hairpin stirs.", Desk(CommentaryTone.Eldritch, tags: false).Desk.PrepareSummaryForSpeech("The hairpin stirs."));
    }
}
