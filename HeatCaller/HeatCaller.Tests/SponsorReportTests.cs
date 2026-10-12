namespace HeatCaller.Tests;

/// <summary>
/// What each sponsor got tonight, written out so a club can send it to
/// them (11 Oct 2026).
///
/// The counting has happened since the paid reads were built -
/// SponsorRotation.ReadsTonight has held it all along - and nothing ever
/// collected it into something a club could email. This is the one thing
/// in the broadcast that survives a treasurer.
///
/// WHICH MAKES IT THE ONE THING HERE THAT MUST NOT OVERSTATE. A report a
/// sponsor can disprove costs the club money rather than face, so most of
/// this file is about the figures the report refuses to give: no slide
/// counts, because the app does not know which sponsor a slide belongs to;
/// no share where there is no share to have had; and a sponsor with no
/// reads named anyway, with the reason, rather than quietly left out.
/// </summary>
public sealed class SponsorReportTests
{
    private static SponsorBooking Sponsor(
        string id,
        string name,
        int weight = 1,
        bool enabled = true,
        string copy = "The best pies in the county.") =>
        new() { Id = id, Name = name, Weight = weight, Enabled = enabled, SalesCopy = copy };

    private static readonly DateTime Night = new(2026, 10, 11, 21, 0, 0, DateTimeKind.Local);

    private static string Report(
        IReadOnlyList<SponsorBooking>? bookings,
        IReadOnlyDictionary<string, int>? reads = null,
        SponsorPlacement placement = SponsorPlacement.Everywhere,
        string? eventName = "Club Night 14") =>
        SponsorReport.ForTheNight(bookings, reads, placement, eventName, Night);

    // ------------------------------------------------------------------
    //  What it says
    // ------------------------------------------------------------------

    [Fact]
    public void It_names_the_night_and_every_sponsor_with_what_they_got()
    {
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern"), Sponsor("b", "Quickway Diner") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 4, ["b"] = 3 });

        Assert.Contains("Club Night 14 - sponsor reads", report);
        Assert.Contains("11 October 2026", report);

        Assert.Contains("7 reads went out", report);
        Assert.Contains("Ralph's Tavern: 4 reads", report);
        Assert.Contains("Quickway Diner: 3 reads", report);
    }

    [Fact]
    public void A_club_that_files_nothing_against_an_event_still_gets_a_report()
    {
        var report = Report(new[] { Sponsor("a", "Ralph's Tavern") }, eventName: null);

        Assert.StartsWith("Sponsor reads", report);
        Assert.Contains("11 October 2026", report);
    }

    [Fact]
    public void One_read_is_one_read()
    {
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });

        Assert.Contains("1 read went out", report);
        Assert.Contains("Ralph's Tavern: 1 read", report);
        Assert.DoesNotContain("1 reads", report);
    }

    // ------------------------------------------------------------------
    //  The share bought, beside the share given
    // ------------------------------------------------------------------

    [Fact]
    public void A_sponsor_on_twice_the_weight_sees_both_numbers()
    {
        // The rotation's promise is that everyone ends within one read of
        // their share, and the only way a club can stand behind that is to
        // show both. A report with only the count would have the sponsor
        // on weight two comparing their four with somebody else's three
        // and ringing up about it.
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern", weight: 2), Sponsor("b", "Quickway Diner") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 4, ["b"] = 2 });

        Assert.Contains("Ralph's Tavern: 4 reads (67% of the evening's reads; they bought 67%)", report);
        Assert.Contains("Quickway Diner: 2 reads (33% of the evening's reads; they bought 33%)", report);
    }

    [Fact]
    public void A_club_with_one_sponsor_is_not_told_they_had_a_hundred_per_cent()
    {
        // "A hundred per cent of the evening's reads" is a sentence about
        // nothing, and it reads as padding - which is the one tone this
        // report cannot afford.
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 6 });

        Assert.Contains("Ralph's Tavern: 6 reads", report);
        Assert.DoesNotContain("%", report);
    }

    [Fact]
    public void A_night_with_no_reads_has_no_shares_either()
    {
        var report = Report(new[] { Sponsor("a", "Ralph's Tavern", weight: 2), Sponsor("b", "Quickway Diner") });

        Assert.Contains("No sponsor reads went out", report);
        Assert.DoesNotContain("%", report);
    }

    // ------------------------------------------------------------------
    //  A zero with a reason beside it
    // ------------------------------------------------------------------

    [Fact]
    public void A_sponsor_who_got_nothing_is_in_the_report_with_the_reason()
    {
        // Leaving them out is the one thing a club must not do with a
        // figure it is sending to somebody who paid.
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern"), Sponsor("b", "Quickway Diner") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 5 });

        Assert.Contains("Quickway Diner: no reads", report);
        Assert.Contains("no gap long enough", report);
    }

    [Fact]
    public void A_sponsor_with_nothing_typed_for_them_is_told_so_rather_than_blamed_on_the_night()
    {
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern"), Sponsor("b", "Quickway Diner", copy: "") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 5 });

        // READ AS THE LINE IT IS, not as one substring. The reason is at
        // the END of a sponsor's line and the share they bought sits
        // between it and the count, so "no reads - nothing was typed"
        // never was contiguous and this assertion could not have passed.
        // Its sibling above gets this right by asserting the two halves
        // separately; this is the same thing done on the line itself, which
        // also pins that the reason is on the right sponsor's line.
        var line = report
            .Split('\n')
            .Single(one => one.StartsWith("Quickway Diner:", StringComparison.Ordinal))
            .TrimEnd();

        Assert.Contains("no reads", line, StringComparison.Ordinal);
        Assert.EndsWith("- nothing was typed for them to read", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sponsor_switched_off_for_the_night_says_so_and_takes_no_share()
    {
        // A sponsor out of the rotation bought no share of that night, and
        // counting their weight would make everybody else's share look
        // short.
        var report = Report(
            new[]
            {
                Sponsor("a", "Ralph's Tavern"),
                Sponsor("b", "Quickway Diner"),
                Sponsor("c", "Last Season Motors", weight: 8, enabled: false),
            },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 3, ["b"] = 3 });

        Assert.Contains("Last Season Motors: no reads - switched off for this night", report);

        // The two who ran split it evenly - the eight-weight sponsor who
        // was not in it does not make them look half-served.
        Assert.Contains("Ralph's Tavern: 3 reads (50% of the evening's reads; they bought 50%)", report);
    }

    // ------------------------------------------------------------------
    //  Where the reads were allowed at all
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(SponsorPlacement.Everywhere, "both in the gaps between races and on the tail of a commercial break")]
    [InlineData(SponsorPlacement.AdBreaks, "tail of a commercial break only")]
    [InlineData(SponsorPlacement.Segments, "gaps between races only")]
    [InlineData(SponsorPlacement.Nowhere, "switched off everywhere")]
    public void It_says_where_reads_were_allowed_so_a_row_of_zeroes_explains_itself(
        SponsorPlacement placement,
        string expected)
    {
        var report = Report(new[] { Sponsor("a", "Ralph's Tavern") }, placement: placement);

        Assert.Contains(expected, report);
    }

    // ------------------------------------------------------------------
    //  What it refuses to claim
    // ------------------------------------------------------------------

    [Fact]
    public void It_never_claims_a_slide_count()
    {
        // The Big Screen cycles a flat list of pictures and the app does
        // not know which sponsor any slide belongs to. "Your logo was on
        // screen fourteen times" is not a number this app has, and a
        // club-wide figure invoices nobody.
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 4 });

        Assert.DoesNotContain("impression", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("on screen", report, StringComparison.OrdinalIgnoreCase);

        // And it says what it does not cover, in the report itself, so a
        // club is never the one who has to explain the gap.
        Assert.Contains("does not count the slides", report);
    }

    [Fact]
    public void A_club_with_no_sponsors_gets_one_honest_sentence()
    {
        Assert.Contains("No sponsors were on the books", Report(null));
        Assert.Contains("No sponsors were on the books", Report(Array.Empty<SponsorBooking>()));
    }

    [Fact]
    public void A_sponsor_with_no_name_or_no_id_is_not_a_line_in_a_report()
    {
        // A half-made booking somebody added and never filled in. Naming
        // it would put a blank line in front of a sponsor.
        var report = Report(
            new[]
            {
                Sponsor("a", "Ralph's Tavern"),
                Sponsor("b", "   "),
                Sponsor(string.Empty, "No Id Motors"),
            },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 2 });

        Assert.Contains("Ralph's Tavern", report);
        Assert.DoesNotContain("No Id Motors", report);

        // And with only one real sponsor left, no share is claimed.
        Assert.DoesNotContain("%", report);
    }

    [Fact]
    public void A_count_for_a_sponsor_nobody_has_is_ignored_rather_than_added_in()
    {
        // A sponsor removed mid-season whose reads are still in the
        // night's counts. They are not in the report and their reads are
        // not in the total, because the total is what the report's own
        // percentages are worked out of.
        var report = Report(
            new[] { Sponsor("a", "Ralph's Tavern") },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 3, ["gone"] = 9 });

        Assert.Contains("3 reads went out", report);
        Assert.DoesNotContain("12", report);
    }

    [Fact]
    public void The_file_name_puts_the_night_first_so_a_folder_of_them_sorts()
    {
        Assert.Equal("2026-10-11 sponsor reads.txt", SponsorReport.FileNameFor(Night));
    }
}
