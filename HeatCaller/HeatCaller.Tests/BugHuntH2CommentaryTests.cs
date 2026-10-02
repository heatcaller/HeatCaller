namespace HeatCaller.Tests;

/// <summary>
/// A line too long for the HeatCaller voice to take at once - a race recap
/// with the broadcast opening and closing, every sentence tagged [excited] -
/// is cut into pieces the relay will take, at sentence ends, counted with
/// the tags in the way the relay counts them for a v3 voice (2 Oct 2026).
/// </summary>
public sealed class BugHuntH2CommentaryTests
{
    private const int Max = HeatCallerVoiceClient.MaxCharacters;

    private static string Recap(int sentences) =>
        string.Join(" ", Enumerable.Range(1, sentences)
            .Select(i => $"[excited] Sentence number {i} of the recap, and Sam held on by three tenths over Bill!"));

    [Fact]
    public void A_line_that_fits_is_left_whole()
    {
        var line = "[excited] What a finish! [excited] Sam wins it by a tenth!";

        Assert.Equal(new[] { line }, AnnouncementService.SplitForHeatCaller("  " + line + " ", Max));
    }

    [Fact]
    public void A_line_that_only_overflows_with_its_tags_is_still_cut()
    {
        // Under the limit with the tags taken out, over it with them in:
        // the line a v3 voice on the relay used to refuse.
        var line = Recap(8);

        Assert.True(line.Replace("[excited] ", string.Empty).Length <= Max);
        Assert.True(line.Length > Max);

        var pieces = AnnouncementService.SplitForHeatCaller(line, Max);

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, piece => Assert.True(piece.Length <= Max));
    }

    [Fact]
    public void A_long_recap_is_cut_at_sentence_ends_and_loses_nothing()
    {
        var line = Recap(20);

        var pieces = AnnouncementService.SplitForHeatCaller(line, Max);

        Assert.All(pieces, piece =>
        {
            Assert.True(piece.Length <= Max);
            Assert.StartsWith("[excited] ", piece);
            Assert.EndsWith("!", piece);
        });
        Assert.Equal(line, string.Join(" ", pieces));
    }

    [Fact]
    public void One_sentence_longer_than_the_limit_is_cut_between_words_and_keeps_its_tag()
    {
        var words = string.Join(" ", Enumerable.Repeat("faster", 150));
        var line = "[excited] " + words + "!";

        var pieces = AnnouncementService.SplitForHeatCaller(line, Max);

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, piece =>
        {
            Assert.True(piece.Length <= Max);
            Assert.StartsWith("[excited] faster", piece);
        });

        var spoken = string.Join(" ", pieces.Select(p => p["[excited] ".Length..]));
        Assert.Equal(words + "!", spoken);
    }

    [Fact]
    public void An_unbroken_run_of_letters_is_still_cut_to_fit()
    {
        var line = new string('x', Max * 2 + 5);

        var pieces = AnnouncementService.SplitForHeatCaller(line, Max);

        Assert.All(pieces, piece => Assert.True(piece.Length <= Max));
        Assert.Equal(line, string.Concat(pieces));
    }
}
