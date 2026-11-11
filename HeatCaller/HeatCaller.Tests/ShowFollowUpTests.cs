namespace HeatCaller.Tests;

/// <summary>
/// What the 11 Nov 2026 08:52 show turned up: the combined personal bests
/// held by the pacing dial, the winner's two renders taking both synthesis
/// slots, and the recap rendered whole before a word of it was heard.
/// </summary>
public sealed class ShowFollowUpTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    // ------------------------------------------------- personal bests together

    [Fact]
    public void Personal_bests_together_break_the_pacing_gap()
    {
        var combined = Source("AnnouncementService.CombinedCalls.cs");

        Assert.Contains("routineWeight: Math.Max(AnnouncerDensity.Commanding, WeightOf(AnnouncementEventType.PersonalBestLap)),", combined);

        // Commanding is what the dial lets through, whatever it protects.
        Assert.True(AnnouncerDensity.WorthBreakingIn(AnnouncerDensity.Commanding, int.MaxValue / 2));
    }

    // --------------------------------------------------------- the winner's call

    [Fact]
    public void The_winner_s_lines_are_made_one_after_the_other()
    {
        var winner = Source("AnnouncementService.Winner.cs");

        // One background job walks the contenders, waiting on each render.
        Assert.Contains("foreach (var (transponder, text) in toMake)", winner);
        Assert.Contains("if (MakeTheWinnerCall(transponder, text, speed) is { } render)", winner);
        Assert.Contains("await render;", winner);
        Assert.DoesNotContain("_ = Task.Run(() => MakeTheWinnerCall(", winner);
    }

    // ------------------------------------------------------------ recap in pieces

    [Fact]
    public void The_recap_s_first_piece_is_short_and_the_rest_are_longer()
    {
        var sentence = "[excited] Al gorerhythm led every single lap of the thirty-lap race.";
        var recap = string.Join(" ", Enumerable.Repeat(sentence, 20));

        var pieces = RaceControlControl.RecapPieces(recap);

        Assert.True(pieces.Count > 2);
        Assert.True(pieces[0].Length <= RaceControlControl.FirstRecapPieceChars);
        Assert.All(pieces.Skip(1), piece => Assert.True(piece.Length <= RaceControlControl.RecapPieceChars));

        // Cut only at sentence ends, every tag kept with its sentence.
        Assert.All(pieces, piece => Assert.StartsWith("[excited] ", piece));
        Assert.All(pieces, piece => Assert.EndsWith(".", piece));
        Assert.Equal(recap, string.Join(" ", pieces));
    }

    [Fact]
    public void A_sentence_longer_than_a_piece_is_a_piece_of_its_own()
    {
        var longOne = new string('a', RaceControlControl.RecapPieceChars + 50) + ".";

        var pieces = RaceControlControl.RecapPieces($"Short one. {longOne} Last one!");

        Assert.Equal(new[] { "Short one.", longOne, "Last one!" }, pieces);
    }

    [Fact]
    public void A_short_recap_is_one_piece()
    {
        Assert.Equal(new[] { "What a race." }, RaceControlControl.RecapPieces("What a race."));
    }

    [Fact]
    public void The_recap_plays_its_first_piece_and_feeds_the_rest_in_order_before_anything_else()
    {
        var afterRace = Source("RaceControlControl.AfterRace.cs");

        // The first piece's tail runs on into the next; the last gets the long one.
        Assert.Contains("recap.Rest.Count == 0 ? AnnouncementService.SpeechTail.Long : AnnouncementService.SpeechTail.BackToBack,", afterRace);

        // The rest is fed before the honours, so no stinger lands between two pieces.
        var feed = afterRace.IndexOf("await FeedTheRecapAsync(recap.Rest, delivery, nextRace.Token);", StringComparison.Ordinal);
        var honours = afterRace.IndexOf("await SpeakHonoursAsync(honoursSegment, honoursLines, show, nextRace.Token);", StringComparison.Ordinal);
        Assert.True(feed > 0 && honours > feed);

        // Rendered one after another, one slot, and stopped once the first fails.
        Assert.Contains("if (!nextRace.IsCancellationRequested && (at == 0 || made[0].Task.Result is not null))", afterRace);
    }
}
