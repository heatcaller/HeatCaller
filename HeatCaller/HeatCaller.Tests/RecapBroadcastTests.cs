using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// The recap as a broadcast (27 Sep 2026): the mechanic's interview split
/// between the two voices (CommentaryService.ParseInterview), a race named
/// as a heat or a main (DescribeKind), and the mechanic's voice standing in
/// for the announcer's for a line (VoiceProfile.CloneWithVoice).
///
/// The mechanic used to have a Windows voice among its per-engine voices.
/// That voice was retired on 10 Oct 2026, so what is checked here is the
/// same rule over the voices that are left: only what was picked for the
/// mechanic replaces the announcer's, and everything else is kept.
///
/// Since 10 Oct 2026 it also holds what the recap and the round intro
/// actually SAY - the club's own history and what the race is worth - read
/// off the prompt rather than out of a model. See the last section.
/// </summary>
public sealed class RecapBroadcastTests : IDisposable
{
    /// <summary>
    /// Declared first: ProfileStore.Load below reads from wherever
    /// AppPaths points, and a test must not read the club's own profiles.
    /// </summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly string[] Names = { "Asher", "Wrench", "Audi R8" };

    // --- The interview ------------------------------------------------------------------

    [Fact]
    public void An_interview_in_the_form_asked_for_splits_into_its_exchanges()
    {
        // As LocalLlmClient hands it over: the lines joined into one, and the
        // leading "HOST:" taken off as though it were a label on the answer.
        const string text =
            "Wrench, what a drive from Asher! How did the car feel? MECHANIC: It was on rails - he never put a wheel wrong. "
            + "HOST: And what did you change? MECHANIC: We went up a tooth on the pinion, to 23T, and it paid off on the straight.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names);

        Assert.NotNull(interview);
        Assert.Equal(2, interview!.Count);
        Assert.Equal("Wrench, what a drive from Asher! How did the car feel?", interview[0].Question);
        Assert.Equal("It was on rails - he never put a wheel wrong.", interview[0].Answer);
        Assert.Equal("And what did you change?", interview[1].Question);
        Assert.Equal("We went up a tooth on the pinion, to 23T, and it paid off on the straight.", interview[1].Answer);
    }

    [Fact]
    public void Labels_in_bold_in_any_case_or_by_the_mechanics_name_are_read()
    {
        const string text =
            "**Host:** Wrench, great win! **Wrench:** Thanks - the car was perfect. host : What changed? mechanic: Nothing, we left it alone.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names);

        Assert.Equal(2, interview!.Count);
        Assert.Equal("Wrench, great win!", interview[0].Question);
        Assert.Equal("Thanks - the car was perfect.", interview[0].Answer);
        Assert.Equal("What changed?", interview[1].Question);
        Assert.Equal("Nothing, we left it alone.", interview[1].Answer);
    }

    [Fact]
    public void The_mechanics_name_mid_sentence_is_not_taken_for_the_mechanic_speaking()
    {
        const string text = "HOST: Over to you, Wrench: how did it feel out there? MECHANIC: Like it was on rails all night.";

        var exchange = Assert.Single(CommentaryService.ParseInterview(text, "Wrench", Names)!);

        Assert.Equal("Over to you, Wrench: how did it feel out there?", exchange.Question);
        Assert.Equal("Like it was on rails all night.", exchange.Answer);
    }

    [Fact]
    public void A_preamble_before_the_first_host_line_is_dropped()
    {
        const string text = "Here is the interview. HOST: Wrench, how was it? MECHANIC: Brilliant, from start to finish.";

        var exchange = Assert.Single(CommentaryService.ParseInterview(text, "Wrench", Names)!);

        Assert.Equal("Wrench, how was it?", exchange.Question);
        Assert.Equal("Brilliant, from start to finish.", exchange.Answer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("What a race that was, and what a drive from Asher!")]
    [InlineData("MECHANIC: We changed nothing at all. HOST: Great stuff, thanks.")]
    [InlineData("HOST: Wrench, how was it? HOST: And the car today?")]
    [InlineData("HOST: Wrench? MECHANIC: Great.")]
    public void An_interview_that_cant_be_split_into_two_voices_is_not_used(string text)
    {
        Assert.Null(CommentaryService.ParseInterview(text, "Wrench", Names));
    }

    [Fact]
    public void A_broken_exchange_ends_the_interview_and_no_more_than_two_are_kept()
    {
        const string broken = "HOST: How was it out there? MECHANIC: Smooth all the way round. HOST: And the car? HOST: Anything else?";
        Assert.Single(CommentaryService.ParseInterview(broken, "Wrench", Names)!);

        const string three =
            "HOST: First question here? MECHANIC: First answer here. HOST: Second question here? MECHANIC: Second answer here. "
            + "HOST: Third question here? MECHANIC: Third answer here.";
        Assert.Equal(2, CommentaryService.ParseInterview(three, string.Empty, Names)!.Count);
    }

    [Fact]
    public void A_shouted_question_is_calmed_and_a_long_answer_cut_to_whole_sentences()
    {
        const string sentence = "We tuned it all week and it paid off.";
        var longAnswer = string.Join(" ", Enumerable.Repeat(sentence, 8));

        var exchange = Assert.Single(CommentaryService.ParseInterview(
            $"HOST: WRENCH, THAT WAS AN INCREDIBLE DRIVE BY ASHER! MECHANIC: {longAnswer}", "Wrench", Names)!);

        Assert.Equal("Wrench, that was an incredible drive by Asher!", exchange.Question);

        // Nine words a sentence, and 45 words with the usual fifth of slack
        // is 54: six whole sentences.
        Assert.Equal(string.Join(" ", Enumerable.Repeat(sentence, 6)), exchange.Answer);
    }

    // --- Heat or main -------------------------------------------------------------------

    [Theory]
    [InlineData(RoundKind.Qualifier, -1, false, "qualifying heat")]
    [InlineData(RoundKind.Qualifier, -1, true, null)]
    [InlineData(RoundKind.Main, 0, false, "the A-Main, the top final of the class")]
    [InlineData(RoundKind.Main, 1, true, "the B-Main")]
    [InlineData(RoundKind.Main, -1, false, "a main")]
    [InlineData(RoundKind.Practice, -1, false, "practice session")]
    public void A_race_is_named_a_heat_or_a_main(RoundKind kind, int tier, bool clubNight, string? expected)
    {
        Assert.Equal(expected, CommentaryService.DescribeKind(new RoundDefinition { Kind = kind, MainTier = tier }, clubNight));
    }

    [Fact]
    public void A_freeform_race_is_neither()
    {
        Assert.Null(CommentaryService.DescribeKind(null, clubNight: false));
    }

    // --- The mechanic's voice -------------------------------------------------------------

    [Fact]
    public void The_mechanics_voice_replaces_only_what_was_picked_for_it()
    {
        var announcer = new VoiceProfile
        {
            Engine = VoiceEngine.ElevenLabs,
            ApiKey = "key",
            VoiceId = "announcer-id",
            VoiceName = "Adam",
            KokoroVoiceName = "am_michael",
            KokoroStandsIn = false,
            Stability = 0.3,
            Style = 0.4,
        };

        // Only an ElevenLabs voice picked for the mechanic: no HeatCaller
        // voice, no Kokoro voice.
        var mechanic = new SecondVoice("mechanic-id", "Bill", string.Empty, string.Empty, string.Empty);

        var copy = announcer.CloneWithVoice(mechanic);

        Assert.Equal("mechanic-id", copy.VoiceId);
        Assert.Equal("Bill", copy.VoiceName);

        // Not picked, so the announcer's: the Kokoro voice, the stand-in
        // choice, the key, the delivery.
        Assert.Equal("am_michael", copy.KokoroVoiceName);
        Assert.False(copy.KokoroStandsIn);
        Assert.Equal("key", copy.ApiKey);
        Assert.Equal(0.3, copy.Stability);
        Assert.Equal(0.4, copy.Style);
        Assert.Equal(VoiceEngine.ElevenLabs, copy.EffectiveEngine);

        // A copy - the saved profile still speaks as the announcer.
        Assert.Equal("announcer-id", announcer.VoiceId);

        Assert.True(mechanic.IsSetFor(VoiceEngine.ElevenLabs));
        Assert.False(mechanic.IsSetFor(VoiceEngine.Kokoro));
        Assert.False(mechanic.IsSetFor(VoiceEngine.HeatCaller));
    }

    [Fact]
    public void A_kokoro_voice_picked_for_the_mechanic_replaces_the_announcers()
    {
        var announcer = new VoiceProfile
        {
            Engine = VoiceEngine.Kokoro,
            KokoroVoiceName = "am_michael",
        };

        var mechanic = new SecondVoice(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "bf_emma");

        var copy = announcer.CloneWithVoice(mechanic);

        Assert.Equal("bf_emma", copy.KokoroVoiceName);
        Assert.Equal(VoiceEngine.Kokoro, copy.EffectiveEngine);

        // The announcer still speaks as itself.
        Assert.Equal("am_michael", announcer.KokoroVoiceName);
        Assert.True(mechanic.IsSetFor(VoiceEngine.Kokoro));
    }

    [Fact]
    public void A_mechanics_voice_left_null_by_a_hand_edited_settings_file_reads_as_none()
    {
        var settings = new AppSettings { MechanicElevenLabsVoiceId = null!, MechanicKokoroVoiceName = "af_heart" };

        Assert.False(settings.MechanicVoice.IsSetFor(VoiceEngine.ElevenLabs));
        Assert.True(settings.MechanicVoice.IsSetFor(VoiceEngine.Kokoro));
    }

    // --- Where the show's music goes ----------------------------------------------------

    /// <summary>
    /// The repo, found by walking up from the test binary to check.cmd -
    /// the same trick NavRailTests and InstallerTests use.
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    [Fact]
    public void The_recaps_show_is_not_opened_until_the_recap_has_been_written()
    {
        // REHEARSAL 5, 10 OCT 2026. The opener and bed were started before
        // `await recapTask`, so that the opener would play across the gap
        // after the teaser rather than being added to it. That assumed the
        // model answers in about the length of the gap. It does not: the
        // night's log has the recap taking 41.6s and 37.5s to write and
        // render against a 6s gap, and the club heard the opener land, ring
        // out, and then eighteen seconds of bed with nobody talking over it.
        //
        // Pinned by reading the source rather than by driving the sequence,
        // which needs a model, a session and a sound device. What is being
        // held is an ORDER of two lines in one method, which is exactly what
        // reading the file can see and what a behavioural test of this would
        // have to fake its way around.
        var source = File.ReadAllText(
            Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "RaceControlControl.AfterRace.cs"));

        var recapAwaited = source.IndexOf("var recap = await recapTask!;", StringComparison.Ordinal);
        var showOpened = source.IndexOf("opening = treatment.StartAsync(SegmentKind.RaceRecap", StringComparison.Ordinal);

        Assert.True(recapAwaited > 0, "RaceControlControl.AfterRace.cs no longer awaits the recap the way this test reads it.");
        Assert.True(showOpened > 0, "RaceControlControl.AfterRace.cs no longer opens the recap's show the way this test reads it.");

        Assert.True(
            showOpened > recapAwaited,
            "The recap's show is opened before the recap is awaited, so the club hears the opener and then however "
            + "long the model takes with nothing over the music. Open the show once the recap is in hand.");

        // And still before the gap is waited out, so what is left of the
        // gap and the opener's own run-up overlap rather than queueing up.
        var gapAwaited = source.IndexOf("await gap;", showOpened, StringComparison.Ordinal);

        Assert.True(gapAwaited > showOpened, "The teaser gap is no longer waited out after the show opens.");
    }

    // --- What the recap and the intro actually say (10 Oct 2026) -------------------------

    /// <summary>
    /// A brain that keeps what it was asked and answers the same thing
    /// every time, so a PROMPT can be read instead of a model being run.
    ///
    /// The whole point of this layer is that the model only ever phrases
    /// what it is handed, so what is worth testing is what it is handed.
    /// </summary>
    private sealed class Scribe : ICommentaryBrain
    {
        public string Facts { get; private set; } = string.Empty;

        public string SystemPrompt { get; private set; } = string.Empty;

        public string? Answer { get; set; } = "And that is a wrap on the A-Main!";

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

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Scribe Model) Desk(ProfileStore? profiles = null)
    {
        var model = new Scribe();

        var desk = new CommentaryService(
            profiles ?? ProfileStore.Load(),
            new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        return (desk, model);
    }

    /// <summary>A finisher whose transponder is their name, so no profile is needed to say it.</summary>
    private static SessionResultEntry Finisher(string name, int position, int laps, long elapsedMs = 300_000, long fastLapMs = 0) => new()
    {
        Transponder = name,
        DriverName = name,
        Position = position,
        Laps = laps,
        ElapsedMs = elapsedMs,
        FastLapMs = fastLapMs,
    };

    private static SessionRecord ARace(params SessionResultEntry[] results) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 10, 10, 19, 10, 0, DateTimeKind.Utc),
        Results = results.ToList(),
    };

    /// <summary>A Box Stock history for these drivers and no pairs worth a rivalry line.</summary>
    private static RaceForm History(params DriverForm[] grid) => new(grid, Array.Empty<HeadToHead>(), "Box Stock");

    private static RoundDefinition AMain(params string[] roster) => new()
    {
        RoundLabel = "A-Main",
        ClassName = "Box Stock",
        Kind = RoundKind.Main,
        MainTier = 0,
        AssignedTransponders = roster.ToList(),
    };

    /// <summary>The indented lines under a header in the facts - the history, or the standings.</summary>
    private static List<string> Under(string header, string facts) =>
        facts.Split('\n')
            .SkipWhile(line => !line.StartsWith(header, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public async Task A_recap_at_a_club_with_no_history_reads_exactly_as_it_did()
    {
        // THE CLUB IN ITS FIRST WEEK IS THE MOST IMPORTANT CLUB NOT TO
        // BREAK. GridHistory hands back an empty grid when nothing has
        // ever been filed, and the recap has to come out byte for byte
        // what it was before any of this existed.
        var (desk, model) = Desk();

        await desk.BuildRaceSummaryAsync(ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450)));

        var plain = model.Facts;

        await desk.BuildRaceSummaryAsync(
            ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450)),
            history: new RaceForm(Array.Empty<DriverForm>(), Array.Empty<HeadToHead>(), "Box Stock"));

        Assert.Equal(plain, model.Facts);
        Assert.DoesNotContain("History before this race", plain);

        // And it is still the recap it always was.
        Assert.Contains("Final results:", plain);
        Assert.Contains("How close it was:", plain);
    }

    [Fact]
    public async Task The_recap_says_what_the_result_meant_and_forbids_inventing_any_more_of_it()
    {
        var (desk, model) = Desk();

        await desk.BuildRaceSummaryAsync(
            ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450)),
            history: History(
                new DriverForm("Sam", "Sam", 21, 0, 6),
                new DriverForm("Bill", "Bill", 30, 4, 12)));

        var facts = model.Facts;

        // ResultInContext, which had no caller anywhere in the app until
        // this. It is the whole reason the recap had nothing to say.
        Assert.Contains("History before this race", facts);
        Assert.Contains("first ever Box Stock win here, at the 22nd attempt", facts);

        // And the winner's record, in the past, where it cannot contradict
        // the race just run.
        Assert.Contains("Before this race Sam had 21 starts in Box Stock races here, with no wins and 6 podiums.", facts);

        // THE RULE THE WHOLE THING RESTS ON: the colour can be made up and
        // a record cannot.
        Assert.Contains("every count in them is exact", facts);
        Assert.Contains("Do not state a win, a podium, a start, a run of results or a first that is not listed", facts);

        // The rule the numbers have always had, still stated.
        Assert.Contains("do not state a time that does not appear here", facts);
    }

    [Fact]
    public async Task A_placing_that_means_nothing_new_puts_nothing_in_the_recap()
    {
        var (desk, model) = Desk();

        await desk.BuildRaceSummaryAsync(
            ARace(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12, 300_450), Finisher("Pat", 3, 11, 301_000)),
            history: History(
                // Four starts: not yet a record worth reading out.
                new DriverForm("Sam", "Sam", 4, 1, 2),
                new DriverForm("Bill", "Bill", 30, 4, 12),
                new DriverForm("Pat", "Pat", 25, 2, 9)));

        var facts = model.Facts;

        Assert.Contains("Sam's 2nd Box Stock win here.", facts);
        Assert.DoesNotContain("Before this race Sam had", facts);
        Assert.Single(Under("History before this race", facts));
    }

    [Fact]
    public async Task The_recap_carries_no_more_history_lines_than_the_cap()
    {
        var (desk, model) = Desk();

        // A first win and two first podiums, and the winner's record on
        // top of that: four facts offered, three carried.
        await desk.BuildRaceSummaryAsync(
            ARace(
                Finisher("Sam", 1, 12),
                Finisher("Bill", 2, 12, 300_450),
                Finisher("Pat", 3, 11, 301_000),
                Finisher("Jo", 4, 11, 302_000)),
            history: History(
                new DriverForm("Sam", "Sam", 21, 0, 6),
                new DriverForm("Bill", "Bill", 9, 0, 0),
                new DriverForm("Pat", "Pat", 9, 0, 0),
                new DriverForm("Jo", "Jo", 9, 0, 0)));

        Assert.Equal(
            CommentaryHistoryFacts.RecapFacts,
            Under("History before this race", model.Facts).Count);
    }

    [Fact]
    public async Task A_driver_the_announcer_calls_something_else_is_still_one_driver_in_the_recap()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Samuel Jones", AnnouncerName = "Sammy" });

        var (desk, model) = Desk(profiles);

        await desk.BuildRaceSummaryAsync(
            ARace(Finisher("101", 1, 12), Finisher("Bill", 2, 12, 300_450)),
            history: History(new DriverForm("101", "Samuel Jones", 21, 0, 6)));

        var facts = model.Facts;

        // GridHistory counts a driver by their profile's DriverName; the
        // recap names them by their announcer name. Handed over as it came,
        // the model is given two names for one car and writes about two
        // drivers - inventing a finisher in the segment the whole club
        // listens to.
        Assert.Contains("P1 Sammy", facts);
        Assert.Contains("Sammy's first ever Box Stock win here", facts);
        Assert.DoesNotContain("Samuel Jones", facts);
    }

    [Fact]
    public async Task A_comma_decimal_PC_still_writes_the_recaps_numbers_with_a_point()
    {
        var was = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var (desk, model) = Desk();

            await desk.BuildRaceSummaryAsync(
                ARace(Finisher("Sam", 1, 12, fastLapMs: 6_935), Finisher("Bill", 2, 12, 300_450)),
                history: History(new DriverForm("Sam", "Sam", 21, 0, 6)));

            var facts = model.Facts;

            Assert.Contains("at 6.935s", facts);
            Assert.Contains("21 starts", facts);

            // Nothing anywhere in the prompt written "6,935" - the voice
            // read that as "six, nine hundred thirty five".
            Assert.DoesNotMatch("[0-9],[0-9]", facts);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public async Task A_race_intro_says_what_the_race_is_worth_without_predicting_it()
    {
        var (desk, model) = Desk();

        await desk.BuildRoundIntroAsync(
            AMain("Sam", "Bill"),
            form: History(new DriverForm("Sam", "Sam", 0, 0, 0)),
            stake: new ChampionshipAtStake(
                "Winter Series",
                4,
                "Bill",
                LeaderInThisRace: true,
                new[]
                {
                    new ChampionshipStanding("Bill", "Bill", 1, 1, 61, 0),
                    new ChampionshipStanding("Sam", "Sam", 2, 2, 54, 7),
                }));

        var facts = model.Facts;

        Assert.Contains("Sam is making their first ever Box Stock start at this club.", facts);
        Assert.Contains("Championship as it stands BEFORE this race: Winter Series, 4 rounds counted so far", facts);
        Assert.Contains("P1 Bill, in this race - 61 points, leads the championship", facts);
        Assert.Contains("P2 Sam, in this race - 54 points, 7 points behind the leader", facts);

        // The leader is here, so there is nobody to name separately.
        Assert.DoesNotContain("Championship leader, who is NOT in this race", facts);

        // What this race decides is NOT this app's to call.
        Assert.Contains("do not predict a winner or a champion", facts);
        Assert.Contains("Do not state a win, a podium, a start, a run of results or a first that is not listed", facts);
    }

    [Fact]
    public async Task An_absent_championship_leader_is_named_so_the_gap_has_somebody_at_the_end_of_it()
    {
        var (desk, model) = Desk();

        await desk.BuildRoundIntroAsync(
            AMain("Sam"),
            stake: new ChampionshipAtStake(
                "Winter Series",
                4,
                "Alex",
                LeaderInThisRace: false,
                new[] { new ChampionshipStanding("Sam", "Sam", 2, 2, 54, 7) }));

        Assert.Contains("Championship leader, who is NOT in this race: Alex", model.Facts);
    }

    [Fact]
    public async Task A_race_intro_at_a_club_with_no_history_reads_exactly_as_it_did()
    {
        var (desk, model) = Desk();

        await desk.BuildRoundIntroAsync(AMain("Sam", "Bill"));

        var plain = model.Facts;

        await desk.BuildRoundIntroAsync(
            AMain("Sam", "Bill"),
            form: new RaceForm(Array.Empty<DriverForm>(), Array.Empty<HeadToHead>(), "Box Stock"));

        Assert.Equal(plain, model.Facts);
        Assert.DoesNotContain("History", plain);
        Assert.DoesNotContain("Championship", plain);
    }

    [Fact]
    public async Task A_driver_the_intro_does_not_name_gets_no_history_line_in_it()
    {
        var (desk, model) = Desk();

        // The history is read for everyone assigned to the round; the facts
        // name the first eight. A line about a driver the prompt was never
        // introduced to is a line about somebody who, as far as it knows,
        // is not in this race.
        await desk.BuildRoundIntroAsync(
            AMain("Sam"),
            form: History(
                new DriverForm("Sam", "Sam", 20, 3, 8, new[] { 1, 1, 5 }),
                new DriverForm("Ghost", "Ghost", 0, 0, 0)));

        var facts = model.Facts;

        Assert.Contains("Sam has won the last two Box Stock races here.", facts);
        Assert.DoesNotContain("Ghost", facts);
    }
}
