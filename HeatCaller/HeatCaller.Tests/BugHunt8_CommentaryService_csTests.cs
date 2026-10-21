namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, the commentary service (20 Oct 2026). What the hunt found:
///
///  - An interview whose co-host's turns were labelled with her own name
///    ("AMANDA:") was not split at her name, so her name and her next
///    question went out in the guest's voice.
///  - A segment's sponsor was counted for the sponsor report as soon as it
///    was asked, before the model had written anything.
///  - Driver introductions written ahead were recorded as the last line
///    before they were spoken.
/// </summary>
public sealed class BugHunt8_CommentaryService_csTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly string[] Names = { "Asher", "Wrench", "Amanda Jones" };

    private sealed class Brain : ICommentaryBrain
    {
        public string? Answer { get; set; }

        public int Asked { get; private set; }

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
            Asked++;
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    // ------------------------------------------------------------------
    //  The co-host's own name is a host label
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("AMANDA")]
    [InlineData("Amanda Jones")]
    public void The_co_hosts_own_name_splits_the_interview_as_the_host(string label)
    {
        var text =
            $"{label}: Great drive from Asher, how did it go out there? TECH LEAD: We were quick all night long. "
            + $"{label}: And what did you change on the car? TECH LEAD: We went to softer springs before the main.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names, interviewerName: "Amanda Jones");

        Assert.NotNull(interview);
        Assert.Equal(2, interview!.Count);
        Assert.DoesNotContain("Amanda", interview[0].Question, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("We were quick all night long.", interview[0].Answer);
        Assert.Equal("We went to softer springs before the main.", interview[1].Answer);
    }

    [Fact]
    public void Without_a_named_co_host_her_name_is_not_a_label()
    {
        var text =
            "HOST: Great drive from Asher, how did it go out there? TECH LEAD: We were quick all night long. "
            + "HOST: And what did you change on the car? TECH LEAD: We went to softer springs before the main.";

        Assert.Equal(2, CommentaryService.ParseInterview(text, "Wrench", Names)!.Count);
        Assert.Equal(2, CommentaryService.ParseInterview(text, "Wrench", Names, interviewerName: "  ")!.Count);
    }

    [Fact]
    public void A_co_host_name_mid_sentence_is_still_the_host_talking()
    {
        var text =
            "HOST: Over to you, Amanda: no, sorry, how did it go out there? TECH LEAD: We were quick all night long. "
            + "HOST: And what did you change on the car? TECH LEAD: We went to softer springs before the main.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names, interviewerName: "Amanda");

        Assert.NotNull(interview);
        Assert.Equal(2, interview!.Count);
        Assert.Equal("We were quick all night long.", interview[0].Answer);
    }

    // ------------------------------------------------------------------
    //  A segment's sponsor is counted only once it is written
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_segment_that_never_came_back_is_not_counted_for_its_sponsor()
    {
        var model = new Brain { Answer = null };
        var counted = 0;
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true })
        {
            Client = model,
            SegmentSponsorTaken = _ => ("Joe's Hobby Shop", () => counted++),
        };

        var facts = new PackedFacts("Sam and Bill are nose to tail for second.", Array.Empty<string>(), 0);

        Assert.Null(await desk.BuildSegmentAsync(SegmentKind.BattleWatch, facts));
        Assert.Equal(1, model.Asked);
        Assert.Equal(0, counted);

        model.Answer = "Sam and Bill are nose to tail, presented by Joe's Hobby Shop.";

        Assert.NotNull(await desk.BuildSegmentAsync(SegmentKind.BattleWatch, facts));
        Assert.Equal(1, counted);
    }

    // ------------------------------------------------------------------
    //  Driver introductions written ahead
    // ------------------------------------------------------------------

    [Fact]
    public async Task Introductions_written_ahead_are_not_the_last_line_until_spoken()
    {
        var model = new Brain { Answer = "On pole, Sam. Alongside, Bill." };
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 }) { Client = model };

        var grid = new[] { new IntroDriver("1", 1, "Sam", "Sam"), new IntroDriver("2", 2, "Bill", "Bill") };

        var line = await desk.BuildDriverIntroductionsAsync("A-Main", grid, remember: false);

        Assert.Equal("On pole, Sam. Alongside, Bill.", line);
        Assert.Null(desk.LastLine);

        desk.IntroSpoken(line);

        Assert.Equal("On pole, Sam. Alongside, Bill.", desk.LastLine);
    }

    [Fact]
    public async Task Introductions_spoken_straight_away_are_still_the_last_line()
    {
        var model = new Brain { Answer = "On pole, Sam. Alongside, Bill." };
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 }) { Client = model };

        await desk.BuildDriverIntroductionsAsync(
            "A-Main", new[] { new IntroDriver("1", 1, "Sam", "Sam"), new IntroDriver("2", 2, "Bill", "Bill") });

        Assert.Equal("On pole, Sam. Alongside, Bill.", desk.LastLine);
    }
}
