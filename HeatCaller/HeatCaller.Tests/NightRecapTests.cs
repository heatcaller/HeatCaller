namespace HeatCaller.Tests;

/// <summary>
/// The night in four minutes (12 Oct 2026) - the piece a club posts for
/// the people who were not there.
///
/// What is testable here is the splitting, and it matters more than it
/// looks: four minutes handed to a voice engine as one request is a request
/// that times out or comes back truncated, and a split that landed
/// mid-sentence would put a comma-shaped pause in the middle of a clause on
/// every piece a club ever posts.
/// </summary>
public sealed class NightRecapTests
{
    /// <summary>
    /// A sentence of a known length, in the units SponsorCopy counts in -
    /// words. Built rather than written out so the tests say what they
    /// mean about LENGTH rather than about one invented paragraph.
    /// </summary>
    private static string Sentence(int words) =>
        string.Join(" ", Enumerable.Range(0, words).Select(at => $"word{at}")) + ".";

    [Fact]
    public void Nothing_to_read_is_no_pieces_rather_than_one_empty_one()
    {
        Assert.Empty(NightRecap.Chunks(null));
        Assert.Empty(NightRecap.Chunks(string.Empty));
        Assert.Empty(NightRecap.Chunks("   \n  "));
    }

    [Fact]
    public void A_short_recap_goes_out_in_one_piece()
    {
        var chunks = NightRecap.Chunks("It was a good night. Twenty-two drivers, six finals, and Bill took the main.");

        Assert.Single(chunks);
        Assert.Contains("Bill took the main", chunks[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_one_is_broken_into_pieces_a_voice_can_take()
    {
        // Ten sentences of twenty words is two hundred words, well past
        // one chunk's twenty seconds.
        var script = string.Join(" ", Enumerable.Range(0, 10).Select(_ => Sentence(20)));

        var chunks = NightRecap.Chunks(script);

        Assert.True(chunks.Count > 1, "A two-hundred-word piece came back as a single voice request.");

        foreach (var chunk in chunks)
        {
            Assert.True(
                SponsorCopy.SpokenLength(chunk) <= NightRecap.LongestChunk,
                $"A chunk runs {SponsorCopy.SpokenLength(chunk).TotalSeconds:0}s, past the {NightRecap.LongestChunk.TotalSeconds:0}s ceiling.");
        }
    }

    [Fact]
    public void It_never_breaks_inside_a_sentence()
    {
        // THE ONE THAT MATTERS. A split mid-clause is a pause in the wrong
        // place on every piece a club ever posts.
        var script = string.Join(" ", Enumerable.Range(0, 8).Select(at => $"Sentence number {at} runs on for a while with plenty of words in it."));

        var chunks = NightRecap.Chunks(script);

        Assert.True(chunks.Count > 1);

        foreach (var chunk in chunks)
        {
            Assert.EndsWith(".", chunk.Trim(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_word_of_a_recap_that_fits_is_kept_and_kept_in_order()
    {
        var script = "First this happened. Then that happened. And then the other thing happened.";

        var chunks = NightRecap.Chunks(script);

        var together = string.Join(" ", chunks);

        Assert.Contains("First this happened", together, StringComparison.Ordinal);
        Assert.Contains("Then that happened", together, StringComparison.Ordinal);
        Assert.Contains("And then the other thing happened", together, StringComparison.Ordinal);

        Assert.True(
            together.IndexOf("First", StringComparison.Ordinal) < together.IndexOf("Then that", StringComparison.Ordinal),
            "The pieces came back out of order.");
    }

    [Fact]
    public void A_single_enormous_sentence_goes_out_whole_rather_than_cut()
    {
        // A sentence split mid-clause by a voice engine is worse than a
        // long one, and a model asked for a broadcast piece does not write
        // minute-long sentences anyway.
        var chunks = NightRecap.Chunks(Sentence(200));

        Assert.Single(chunks);
        Assert.True(SponsorCopy.SpokenLength(chunks[0]) > NightRecap.LongestChunk);
    }

    [Fact]
    public void A_model_that_ignored_the_length_is_cut_off_rather_than_read_for_an_hour()
    {
        // The backstop. Not a target - the word limit should stop this
        // long before - but a club must not have a button that plays for
        // twenty minutes because a model misbehaved.
        var runaway = string.Join(" ", Enumerable.Range(0, 400).Select(_ => Sentence(15)));

        var chunks = NightRecap.Chunks(runaway);

        Assert.True(
            chunks.Count <= NightRecap.MostChunks,
            $"{chunks.Count} pieces came back, past the {NightRecap.MostChunks} backstop.");
    }

    [Fact]
    public void A_full_length_recap_survives_with_every_word_of_it()
    {
        // THE REGRESSION. The first version of this ran the script through
        // SponsorCopy.Tidy, whose cap is six hundred characters because it
        // is sized for a sponsor's read. A four-hundred-word piece is
        // nearer two and a half thousand, so three quarters of every recap
        // would have gone in silence - the club would have got one that
        // stopped a minute in and nothing anywhere would have said why.
        var script = string.Join(" ", Enumerable.Range(0, 33).Select(_ => Sentence(12)));

        var wordsIn = script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.True(wordsIn >= NightRecap.Words - 20, "The fixture is not a full-length recap.");
        Assert.True(script.Length > SponsorCopy.MaxChars, "The fixture is not long enough to catch the sponsor cap.");

        var kept = NightRecap.Chunks(script)
            .Sum(chunk => chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        Assert.Equal(wordsIn, kept);
    }

    [Fact]
    public void The_recaps_own_character_cap_is_above_what_the_splitter_would_ever_keep()
    {
        // Fifteen chunks of twenty seconds at a hundred and fifty words a
        // minute is seven hundred and fifty words. If the character cap
        // sat below that it would be the thing doing the cutting, and it
        // would cut mid-piece rather than at the backstop.
        var mostWordsTheSplitterKeeps =
            NightRecap.MostChunks * NightRecap.LongestChunk.TotalSeconds * SponsorCopy.WordsPerMinute / 60.0;

        Assert.True(
            NightRecap.MostChars > mostWordsTheSplitterKeeps * 6,
            $"The {NightRecap.MostChars}-character cap can cut a piece the splitter would have kept.");
    }

    [Fact]
    public void Line_breaks_in_the_script_do_not_become_pieces_of_their_own()
    {
        // A model asked for several short paragraphs gives them back with
        // blank lines between. Those are paragraphs, not sentences, and a
        // blank one is not a thing to read out.
        var chunks = NightRecap.Chunks("First paragraph here.\n\nSecond paragraph here.\n\n\nThird.");

        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk)));
        Assert.Contains("First paragraph here", string.Join(" ", chunks), StringComparison.Ordinal);
        Assert.Contains("Third", string.Join(" ", chunks), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  What it is, as opposed to the sign-off
    // ------------------------------------------------------------------

    [Fact]
    public void It_gets_its_own_word_allowance_rather_than_the_clubs_line_limit()
    {
        // The Commentary page's limit is about one spoken line. Somebody
        // who wanted terse race calls did not thereby ask for a
        // one-paragraph summary of their evening.
        Assert.True(NightRecap.Words >= 200, "The recap's allowance is too small to be a piece.");
    }

    [Fact]
    public void The_recap_is_the_same_length_whatever_the_club_set_its_line_limit_to()
    {
        // The prompt builder adds its argument to the club's per-line
        // limit. Handed the whole allowance flat, a club with a
        // three-hundred-word limit would get a seven-hundred-word recap -
        // five minutes, not the four this is named for.
        foreach (var clubLimit in new[] { 0, 8, 30, 45, 60, 150, 399, 400 })
        {
            Assert.Equal(
                NightRecap.Words,
                clubLimit + NightRecap.ExtraWordsOver(clubLimit));
        }
    }

    [Fact]
    public void A_club_that_asked_for_longer_writing_than_the_recap_keeps_it()
    {
        // Shortening it here would be this file overruling their setting
        // rather than filling in around it.
        Assert.Equal(0, NightRecap.ExtraWordsOver(NightRecap.Words + 200));
        Assert.Equal(0, NightRecap.ExtraWordsOver(5000));
    }

    [Fact]
    public void A_nonsense_line_limit_does_not_make_the_recap_longer_still()
    {
        // Settings files get hand-edited.
        Assert.Equal(NightRecap.Words, NightRecap.ExtraWordsOver(-1000));
    }

    [Fact]
    public void The_instruction_tells_the_model_who_it_is_for_and_what_it_may_not_invent()
    {
        var said = NightRecap.Instruction;

        // Who it is for is the whole difference from the sign-off.
        Assert.Contains("NOT at the track", said, StringComparison.Ordinal);

        // And the rule every prompt in this app carries.
        Assert.Contains("must come from the facts above", said, StringComparison.Ordinal);
        Assert.Contains("Do not add", said, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_is_named_for_what_it_is()
    {
        // A club looking in the night's folder has to know at a glance
        // which one to post.
        Assert.False(string.IsNullOrWhiteSpace(NightRecap.Label));
        Assert.DoesNotContain("SegmentKind", NightRecap.Label, StringComparison.Ordinal);
    }
}
