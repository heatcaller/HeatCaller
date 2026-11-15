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

        public string Reply { get; set; } = "Sam is four tenths up on Bill.";

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
            return Task.FromResult<string?>(Reply);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Scribe Model) Desk(CommentaryTone tone, bool tags = true, bool v3 = true, bool allowSailorMouth = true, int swears = 2)
    {
        var model = new Scribe();
        var settings = new AppSettings
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 60,
            CommentaryToneStyle = tone,
            CommentaryVoiceTags = tags,
            AllowSailorMouth = allowSailorMouth,
            SailorMouthSwears = swears,
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

        Assert.Matches("^(Dark|Silly|Distorted|Adult): ", profile.Label);
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
        Assert.Contains(profile.Rule, model.SystemPrompt);
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
        Assert.Contains(profile.Rule, model.SystemPrompt);
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
    public void The_town_crier_shouts_every_sentence()
    {
        var (desk, _) = Desk(CommentaryTone.TownCrier);

        Assert.Equal(
            "[shouting] Hear ye! [shouting] Sam doth lead.",
            desk.PrepareSummaryForSpeech("Hear ye! Sam doth lead."));
    }

    [Fact]
    public void The_pirate_laughs_once_then_is_excited()
    {
        var (desk, _) = Desk(CommentaryTone.Pirate);

        Assert.Equal(
            "[laughs] Arr. [excited] Sam be clear.",
            desk.PrepareSummaryForSpeech("Arr. Sam be clear."));
    }

    [Fact]
    public void The_kept_tones_keep_the_numbers_they_were_saved_under()
    {
        // Six of the nine went the evening they came in (8 Nov 2026); a
        // club that had saved Pirate must still get Pirate.
        Assert.Equal(7, (int)CommentaryTone.Villain);
        Assert.Equal(8, (int)CommentaryTone.Pirate);
        Assert.Equal(10, (int)CommentaryTone.TownCrier);
        Assert.Equal(12, (int)CommentaryTone.SailorMouth);
        Assert.Equal(4, CommentaryTones.All.Count);
    }

    [Fact]
    public async Task A_setting_left_on_a_retired_tone_writes_as_enthusiastic()
    {
        // 3 was Noir.
        var (desk, model) = Desk((CommentaryTone)3);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Contains("Be LOUD and thrilled", model.SystemPrompt);
        Assert.Null(CommentaryTones.Find((CommentaryTone)3));
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
        Assert.Equal("Hear ye.", Desk(CommentaryTone.TownCrier, v3: false).Desk.PrepareSummaryForSpeech("Hear ye."));
        Assert.Equal("Hear ye.", Desk(CommentaryTone.TownCrier, tags: false).Desk.PrepareSummaryForSpeech("Hear ye."));
    }

    // ------------------------------------------------------------------
    //  Sailor-mouth (15 Nov 2026)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sailor_mouth_swears_under_its_own_hard_lines_not_family_friendly()
    {
        var (desk, model) = Desk(CommentaryTone.SailorMouth);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);
        Assert.DoesNotContain("family-friendly", model.SystemPrompt);
        Assert.Contains("nothing racist, antisemitic or hateful", model.SystemPrompt);
        Assert.Contains("no slurs", model.SystemPrompt);
        Assert.Contains("never at a driver as a person", model.SystemPrompt);

        // The facts-first opening is the shared rule's, word for word.
        Assert.StartsWith(CommentaryTones.CharacterRule[..120], CommentaryTones.SailorMouthRule);
    }

    [Fact]
    public async Task The_other_character_tones_stay_family_friendly()
    {
        foreach (var tone in new[] { CommentaryTone.Villain, CommentaryTone.Pirate, CommentaryTone.TownCrier })
        {
            var (desk, model) = Desk(tone);

            await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

            Assert.Contains(CommentaryTones.CharacterRule, model.SystemPrompt);
            Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);
        }
    }

    [Fact]
    public async Task Switched_off_by_the_club_a_saved_sailor_mouth_plays_as_enthusiastic()
    {
        var (desk, model) = Desk(CommentaryTone.SailorMouth, allowSailorMouth: false);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Contains("Be LOUD and thrilled", model.SystemPrompt);
        Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);

        var settings = new AppSettings { CommentaryToneStyle = CommentaryTone.SailorMouth, AllowSailorMouth = false };
        Assert.Equal(CommentaryTone.Enthusiastic, CommentaryTones.Effective(settings));

        // The saved choice is left alone, so switching the rule back on brings it back.
        Assert.Equal(CommentaryTone.SailorMouth, settings.CommentaryToneStyle);
        settings.AllowSailorMouth = true;
        Assert.Equal(CommentaryTone.SailorMouth, CommentaryTones.Effective(settings));
    }

    [Theory]
    [InlineData(0, "SWEARING: none at all")]
    [InlineData(1, "SWEARING: at most 1 swear word in a line")]
    [InlineData(3, "SWEARING: at most 3 swear words in a line")]
    [InlineData(99, "SWEARING: at most 6 swear words in a line")]
    public async Task The_dial_count_is_told_to_the_model(int swears, string told)
    {
        var (desk, model) = Desk(CommentaryTone.SailorMouth, swears: swears);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Contains(told, model.SystemPrompt);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public async Task The_f_word_is_the_first_pick_whenever_the_dial_allows_a_swear(int swears, bool asked)
    {
        // 15 Nov 2026, Asher: "make the model drop more f bombs across the board on sailor setting".
        var (desk, model) = Desk(CommentaryTone.SailorMouth, swears: swears);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.Equal(asked, model.SystemPrompt.Contains("make the f-word (fuck, fucking, fucked) your first pick", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_sailor_mouth_piece_asks_for_f_bombs()
    {
        var sailor = CommentaryTones.All.Single(t => t.Tone == CommentaryTone.SailorMouth);

        Assert.Contains("the f-word is your go-to", sailor.ToneLine);
        Assert.Contains("shit is your next favourite", sailor.ToneLine);
        Assert.Contains("Hardly ever say damn", sailor.ToneLine);
        Assert.Contains("motherfucker", sailor.ToneLine);
        Assert.Contains("cocksucker", sailor.ToneLine);
        Assert.Contains("eat shit", sailor.ToneLine);
        Assert.Contains("eat a bag of sick dicks", sailor.ToneLine);
        Assert.Contains("never a driver", sailor.ToneLine);
        Assert.Contains("f-bomb", sailor.Segment);
        Assert.Contains("f-bombs", sailor.Exchange);
        Assert.Contains("who the fuck won", sailor.Close);
        Assert.Contains("f-bombs", sailor.Host);
    }

    [Fact]
    public async Task Other_tones_are_never_told_a_swear_count()
    {
        var (desk, model) = Desk(CommentaryTone.Pirate);

        await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12."));

        Assert.DoesNotContain("SWEARING:", model.SystemPrompt);
    }

    [Fact]
    public async Task A_line_over_the_dial_has_its_spare_swears_taken_out()
    {
        var (desk, model) = Desk(CommentaryTone.SailorMouth);
        model.Reply = "Sam is fucking clear, and Bill is fucking stuck on lap 12, fucking hell.";

        // Default dial: two a line.
        Assert.Equal(
            "Sam is fucking clear, and Bill is stuck on lap 12, hell.",
            await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12.")));
    }

    [Fact]
    public void The_dial_starts_at_two()
    {
        Assert.Equal(2, new AppSettings().SailorMouthSwears);
    }

    [Fact]
    public void Clubs_allow_sailor_mouth_until_they_switch_it_off()
    {
        Assert.True(new AppSettings().AllowSailorMouth);
    }

    [Fact]
    public async Task The_recap_show_is_read_to_the_room_in_sailor_mouth()
    {
        var (desk, model) = Desk(CommentaryTone.SailorMouth);

        await desk.BuildNightRecapAsync(Facts("Sam won the A-Main by 0.4s."));

        Assert.Contains(CommentaryTones.SailorMouthRule, model.SystemPrompt);
    }

    [Fact]
    public async Task What_gets_posted_is_never_written_sailor_mouth()
    {
        var facts = Facts("Sam won the A-Main by 0.4s.");

        foreach (var write in new Func<CommentaryService, Task>[]
                 {
                     d => d.BuildDriverCutAsync(facts),
                     d => d.BuildSeasonPieceAsync(facts),
                     d => d.BuildNightBeforeAsync(facts),
                 })
        {
            var (desk, model) = Desk(CommentaryTone.SailorMouth);

            await write(desk);

            Assert.DoesNotContain(CommentaryTones.SailorMouthRule, model.SystemPrompt);
            Assert.Contains("Be LOUD and thrilled", model.SystemPrompt);
        }
    }

    [Fact]
    public async Task The_night_recap_keeps_any_other_character_tone()
    {
        var (desk, model) = Desk(CommentaryTone.Pirate);

        await desk.BuildNightRecapAsync(Facts("Sam won the A-Main by 0.4s."));

        Assert.Contains(CommentaryTones.Find(CommentaryTone.Pirate)!.ToneLine, model.SystemPrompt);
    }

    [Theory]
    [InlineData(CommentaryTone.SailorMouth)]
    [InlineData(CommentaryTone.Enthusiastic)]
    public async Task A_line_with_a_hate_term_is_dropped_in_any_tone(CommentaryTone tone)
    {
        var (desk, model) = Desk(tone);
        model.Reply = "Sam is four tenths up on that f@g Bill.";

        Assert.Null(await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12.")));
    }

    [Fact]
    public async Task A_sweary_line_is_said_as_written()
    {
        var (desk, model) = Desk(CommentaryTone.SailorMouth);
        model.Reply = "Sam is fucking clear of Bill on lap 12.";

        Assert.Equal(model.Reply, await desk.BuildSegmentAsync(SegmentKind.BattleWatch, Facts("Sam leads Bill by 0.4s on lap 12.")));
    }

    [Fact]
    public void The_four_character_tones_are_premium_and_say_so()
    {
        Assert.All(CommentaryTones.All, tone =>
        {
            Assert.True(tone.Premium);
            Assert.EndsWith(" (Premium)", tone.DisplayLabel);
            Assert.StartsWith(tone.Label, tone.DisplayLabel);
        });
    }
}
