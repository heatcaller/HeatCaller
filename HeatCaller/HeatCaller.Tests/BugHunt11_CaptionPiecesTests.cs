namespace HeatCaller.Tests;

/// <summary>
/// 6 Nov 2026, Bug hunt 11: a long spoken line is captioned a piece at a time
/// (CaptionLog, 26 Oct 2026), and the public captions judged each piece on its
/// own - so the later pieces of a black flag (the typed reason), a long
/// warning, or a line naming a withheld driver or a car by its transponder
/// number went onto the club's relay captions. The whole line is judged once
/// and the verdict carries to every piece.
/// </summary>
public sealed class BugHunt11_CaptionPiecesTests
{
    private static readonly DateTime At = new(2026, 11, 6, 19, 40, 10, DateTimeKind.Utc);

    private const string Reason =
        "Repeatedly cutting the infield chicane on laps four, seven and nine after two earlier warnings from the rostrum about the same corner. " +
        "Then contact with car three at the hairpin that put them into the wall.";

    /// <summary>Says the line into a fresh log, lets it finish, and returns every piece as captioned.</summary>
    private static IReadOnlyList<CaptionLine> Captioned(string line)
    {
        var log = new CaptionLog();
        log.Add(line, At);
        log.SaidInFull(At.AddMinutes(1));

        var pieces = log.Last(CaptionLog.Kept, At.AddMinutes(1));

        // The test is only worth anything if the line really was split.
        Assert.True(pieces.Count >= 3, $"expected the line in at least 3 pieces, got {pieces.Count}");
        return pieces;
    }

    [Fact]
    public void A_long_black_flag_call_publishes_none_of_its_pieces()
    {
        var pieces = Captioned($"Black flag, Sam Smith. {Reason} Please bring your car in and report to race control.");

        var published = MainForm.PublicCaptionLines(pieces, PublicPrivacy.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void A_long_warning_with_a_penalty_publishes_none_of_its_pieces()
    {
        var pieces = Captioned($"Warning, Sam Smith. {Reason} That's a 10 second penalty.");

        var published = MainForm.PublicCaptionLines(pieces, PublicPrivacy.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void A_long_line_naming_an_initials_only_driver_in_its_first_piece_withholds_every_piece()
    {
        var register = PublicPrivacy.From(new[]
        {
            new DriverProfile { Transponder = "T1", DriverName = "Sam Parker", InitialsOnline = true },
        });

        var pieces = Captioned(
            "Sam Parker crosses the line to take the win in the A main by a little over two seconds. " +
            "What a drive that was from the back of the grid after a tangle on the very first lap of the race. " +
            "The fast lap of the night went to a quick run on lap twelve with a fourteen point two. " +
            "Next up on the track is the B main, so drivers please make your way to the rostrum now.");

        // Only the first piece names the driver.
        Assert.Contains("Sam Parker", pieces[0].Text);
        Assert.All(pieces.Skip(1), p => Assert.DoesNotContain("Sam", p.Text));

        var published = MainForm.PublicCaptionLines(pieces, register);

        Assert.Empty(published);
    }

    [Fact]
    public void A_long_line_naming_a_car_by_its_transponder_number_withholds_every_piece()
    {
        var pieces = Captioned(
            "Car 4417321 crosses the line to take the win in the A main by a little over two seconds. " +
            "What a drive that was from the back of the grid after a tangle on the very first lap of the race. " +
            "The fast lap of the night went to a quick run on lap twelve with a fourteen point two. " +
            "Next up on the track is the B main, so drivers please make your way to the rostrum now.");

        var published = MainForm.PublicCaptionLines(pieces, PublicPrivacy.Empty, t => t.Contains("4417321"));

        Assert.Empty(published);
    }

    [Fact]
    public void A_normal_long_line_still_publishes_every_piece()
    {
        var pieces = Captioned(
            "Alex Jones crosses the line to take the win in the A main by a little over two seconds. " +
            "What a drive that was from the back of the grid after a tangle on the very first lap of the race. " +
            "The fast lap of the night went to a quick run on lap twelve with a fourteen point two. " +
            "Next up on the track is the B main, so drivers please make your way to the rostrum now.");

        var published = MainForm.PublicCaptionLines(pieces, PublicPrivacy.Empty);

        Assert.Equal(pieces.Select(p => p.Text), published.Select(p => p.Text));
        Assert.Equal(pieces.Select(p => p.N), published.Select(p => p.N));
    }

    [Fact]
    public void Every_piece_of_a_line_carries_the_whole_line_and_the_room_still_sees_the_pieces()
    {
        const string line = "Black flag, Sam Smith. " + Reason;
        var pieces = Captioned(line);

        Assert.All(pieces, p => Assert.Equal(line, p.WholeText));
        Assert.All(pieces, p => Assert.True(p.Text.Length <= CaptionLog.PieceChars));
        Assert.StartsWith("Black flag, Sam Smith.", pieces[0].Text);
    }

    // 6 Nov 2026, Bug hunt 11b review: "Shorten published names" shortened
    // each piece on its own, so a long line cut between "Sam" and "Smith" put
    // the full surname online - neither piece held the full name to shorten.

    private static string Shorten(string text) =>
        AnnouncementService.ShortenNames(text, new Dictionary<string, string> { ["Sam Smith"] = "Sam S." });

    /// <summary>One long sentence (no full stop to cut at) whose first piece ends on "Sam" and whose second starts "Smith".</summary>
    private static string NameSplitAcrossPieces()
    {
        var lead = "Moving up into third place after a long hard chase through the whole field of the A main with a lovely move at the hairpin";

        // As close to a full piece as " again" lets it get, with "Sam" still in it and " Smith" not.
        while (lead.Length + " again".Length + " is Sam".Length <= CaptionLog.PieceChars)
        {
            lead += " again";
        }

        return lead + " is Sam Smith in the blue car who started from the very back of the grid and has been the quickest driver on the track for the last three laps of the race.";
    }

    [Fact]
    public void A_name_split_across_two_pieces_is_still_shortened_online()
    {
        var pieces = Captioned(NameSplitAcrossPieces());

        // The test is only worth anything if the cut really falls inside the name.
        Assert.EndsWith(" Sam", pieces[0].Text);
        Assert.StartsWith("Smith ", pieces[1].Text);

        var published = MainForm.PublicCaptionLines(pieces, PublicPrivacy.Empty, shorten: Shorten);

        Assert.NotEmpty(published);
        Assert.All(published, p => Assert.DoesNotContain("Smith", p.Text));
        Assert.Contains(published, p => p.Text.Contains("Sam S."));
        Assert.All(published, p => Assert.True(p.Text.Length <= CaptionLog.PieceChars));
        // Every word of the line still goes out, shortened, in order, under the pieces' own numbers.
        Assert.Equal(Shorten(NameSplitAcrossPieces()), string.Join(" ", published.Select(p => p.Text)));
        Assert.All(published, p => Assert.Contains(p.N, pieces.Select(q => q.N)));
        Assert.Equal(published.Select(p => p.N).OrderBy(n => n), published.Select(p => p.N));

        // The room's own captions are untouched.
        Assert.Contains(pieces, p => p.Text.Contains("Smith"));
    }

    [Fact]
    public void A_name_split_across_two_pieces_is_left_as_said_when_shortening_is_off()
    {
        var pieces = Captioned(NameSplitAcrossPieces());

        var published = MainForm.PublicCaptionLines(pieces, PublicPrivacy.Empty);

        Assert.Equal(pieces.Select(p => p.Text), published.Select(p => p.Text));
        Assert.Equal(pieces.Select(p => p.N), published.Select(p => p.N));
    }

    [Fact]
    public void A_short_line_is_still_shortened_as_before()
    {
        var log = new CaptionLog();
        var line = log.Add("Sam Smith takes the win in the A main.", At)!;

        var published = MainForm.PublicCaptionLines(new[] { line }, PublicPrivacy.Empty, shorten: Shorten);

        var only = Assert.Single(published);
        Assert.Equal("Sam S. takes the win in the A main.", only.Text);
        Assert.Equal(line.N, only.N);

        // A line made by hand, with no whole line behind it, too.
        var byHand = MainForm.PublicCaptionLines(new[] { new CaptionLine(7, "Well driven, Sam Smith!", At) }, PublicPrivacy.Empty, shorten: Shorten);
        Assert.Equal("Well driven, Sam S.!", Assert.Single(byHand).Text);
    }
}
