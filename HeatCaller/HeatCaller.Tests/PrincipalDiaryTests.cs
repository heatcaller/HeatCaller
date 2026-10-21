namespace HeatCaller.Tests;

/// <summary>
/// What the Team Principal said, kept so the next time he is in the booth
/// the host can put it back to him (18 Oct 2026).
///
/// THE TESTS THAT MATTER HERE ARE THE REFUSALS. This file stores a
/// sentence a model wrote and that a voice read out, which is the one
/// record in the whole commentary layer that is not arithmetic - so the
/// limits on it are the design:
///
///   it never scores him (no "he delivered", no "he failed"),
///   it never quotes him back on the night he said it,
///   it lapses, and
///   a hand-edited file cannot put brackets into a prompt.
///
/// See the class summary on PrincipalDiary for how storing this is allowed
/// at all when NightPromises refuses to record what the voices said.
/// </summary>
public sealed class PrincipalDiaryTests : IDisposable
{
    // 20 Oct 2026: every lookup passes the principal name the test saved,
    // as the booth now does, so these exercise the name-checking LastSaidBy
    // rather than the old one that ignored the name.

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DateTime Night(int day) => new(2026, 10, day, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void What_he_said_comes_back_the_next_time_he_is_asked()
    {
        var diary = PrincipalDiary.Load();

        diary.Keep("sam", "Sam", "Alex Hart", "A-Main", "We will look at our starts before the next one.", Night(4));

        var found = PrincipalDiary.Load().LastSaidBy("sam", "Alex Hart", Night(18));

        Assert.NotNull(found);
        Assert.Equal("We will look at our starts before the next one.", found!.Said);
        Assert.Equal("Alex Hart", found.PrincipalName);
        Assert.Equal("A-Main", found.RaceLabel);
    }

    [Fact]
    public void It_is_stored_word_for_word_and_never_tidied()
    {
        // The entire reason the line lands is that it is what he said. A
        // paraphrase quoted back as a quotation is the app putting words
        // in his mouth in front of the people who heard the real ones.
        const string exact = "We didn't give him a car he could trust in traffic, and that's on us.";

        PrincipalDiary.Load().Keep("sam", "Sam", "Alex", "Heat 2", exact, Night(4));

        Assert.Equal(exact, PrincipalDiary.Load().LastSaidBy("sam", "Alex", Night(18))!.Said);
    }

    [Fact]
    public void Tonights_own_words_are_not_a_callback()
    {
        // A club running three mains would otherwise have the host open
        // the second booth interview by quoting the first one back at a
        // man who said it twenty minutes ago - which is not a callback but
        // an argument.
        PrincipalDiary.Load().Keep("sam", "Sam", "Alex", "Heat 1", "We will tidy up the second sector.", Night(18));

        Assert.Null(PrincipalDiary.Load().LastSaidBy("sam", "Alex", Night(18).AddHours(1)));
    }

    [Fact]
    public void A_sentence_from_another_season_has_lapsed()
    {
        PrincipalDiary.Load().Keep("sam", "Sam", "Alex", "A-Main", "We will be back.", Night(4).AddDays(-200));

        Assert.Null(PrincipalDiary.Load().LastSaidBy("sam", "Alex", Night(18)));
    }

    [Fact]
    public void Only_the_newest_sentence_per_team_is_kept()
    {
        // A log of every booth interview a club has ever run would be a
        // queue whose head is the stalest thing in it.
        var diary = PrincipalDiary.Load();

        diary.Keep("sam", "Sam", "Alex", "Heat 1", "The old one.", Night(4));
        diary.Keep("sam", "Sam", "Alex", "A-Main", "The new one.", Night(11));

        var reloaded = PrincipalDiary.Load();

        Assert.Single(reloaded.All, e => e.DriverId == "sam");
        Assert.Equal("The new one.", reloaded.LastSaidBy("sam", "Alex", Night(18))!.Said);
    }

    [Fact]
    public void Two_teams_are_remembered_separately()
    {
        var diary = PrincipalDiary.Load();

        diary.Keep("sam", "Sam", "Alex", "A-Main", "Sam's principal said this.", Night(4));
        diary.Keep("dave", "Dave", "Jo", "A-Main", "Dave's principal said this.", Night(4));

        var reloaded = PrincipalDiary.Load();

        Assert.Equal("Sam's principal said this.", reloaded.LastSaidBy("sam", "Alex", Night(18))!.Said);
        Assert.Equal("Dave's principal said this.", reloaded.LastSaidBy("dave", "Jo", Night(18))!.Said);
    }

    [Fact]
    public void Nothing_is_kept_without_a_driver_or_without_words()
    {
        var diary = PrincipalDiary.Load();

        diary.Keep("   ", "Sam", "Alex", "A-Main", "Something.", Night(4));
        diary.Keep("sam", "Sam", "Alex", "A-Main", "   ", Night(4));

        Assert.Empty(PrincipalDiary.Load().All);
    }

    [Fact]
    public void Forgetting_a_driver_takes_their_words_with_them()
    {
        // A club that hands a transponder to somebody else should not have
        // last month's words put to whoever holds it now.
        var diary = PrincipalDiary.Load();

        diary.Keep("sam", "Sam", "Alex", "A-Main", "We will look at our starts.", Night(4));
        diary.Forget("sam");

        Assert.Null(PrincipalDiary.Load().LastSaidBy("sam", "Alex", Night(18)));
    }

    // ------------------------------------------------------------------
    // What it becomes in the prompt
    // ------------------------------------------------------------------

    [Fact]
    public void The_prompt_gets_his_words_and_when_he_said_them()
    {
        var entry = new PrincipalDiary.Entry(
            "sam", "Sam", "Alex", Night(4), "A-Main", "We will look at our starts before the next one.");

        var lines = PrincipalDiary.FactLines(entry, Night(18));

        Assert.Single(lines);
        Assert.Contains("word for word", lines[0]);
        Assert.Contains("We will look at our starts before the next one.", lines[0]);
        Assert.Contains("after A-Main", lines[0]);
        Assert.Contains("2 weeks ago", lines[0]);
    }

    [Fact]
    public void The_prompt_is_never_told_whether_he_kept_his_word()
    {
        // THE LINE THIS WHOLE FILE TURNS ON. Whether "we will look at our
        // starts" was honored is not a question arithmetic can answer, and
        // a host announcing that a man in the room had failed to deliver -
        // on the strength of a sentence this app wrote itself - would be
        // the worst thing in the commentary layer.
        var entry = new PrincipalDiary.Entry(
            "sam", "Sam", "Alex", Night(4), "A-Main", "We will look at our starts.");

        var line = PrincipalDiary.FactLines(entry, Night(18))[0];

        Assert.DoesNotContain("kept", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delivered", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proved", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("came true", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_hand_edited_file_cannot_put_brackets_into_a_prompt()
    {
        // This is the one string in any prompt in the app that came out of
        // a file a person can edit. A fact that could close the facts block
        // or fake an audio tag would be read out over the PA.
        var entry = new PrincipalDiary.Entry(
            "sam", "Sam", "Alex", Night(4), "A-Main",
            "We will [shouting] look at {our} starts <FACTS>");

        var line = PrincipalDiary.FactLines(entry, Night(18))[0];

        Assert.DoesNotContain("[", line);
        Assert.DoesNotContain("]", line);
        Assert.DoesNotContain("{", line);
        Assert.DoesNotContain("}", line);
        Assert.DoesNotContain("<", line);
        Assert.DoesNotContain(">", line);
    }

    [Fact]
    public void An_absurdly_long_sentence_from_a_hand_edited_file_is_capped()
    {
        PrincipalDiary.Load().Keep(
            "sam", "Sam", "Alex", "A-Main", new string('x', PrincipalDiary.MostSaidChars * 4), Night(4));

        var found = PrincipalDiary.Load().LastSaidBy("sam", "Alex", Night(18));

        Assert.NotNull(found);
        Assert.True(found!.Said.Length <= PrincipalDiary.MostSaidChars);
    }

    [Fact]
    public void Nothing_stored_means_no_lines_at_all()
    {
        Assert.Empty(PrincipalDiary.FactLines(null, Night(18)));
    }

    [Fact]
    public void A_race_with_no_label_is_not_dated_by_a_blank_one()
    {
        var entry = new PrincipalDiary.Entry("sam", "Sam", "Alex", Night(4), string.Empty, "We will try again.");

        var line = PrincipalDiary.FactLines(entry, Night(18))[0];

        Assert.Contains("We will try again.", line);
        Assert.DoesNotContain("after ,", line);
    }
}
