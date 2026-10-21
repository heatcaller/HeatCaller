namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, finding 47: the qualifying report announced a pole "margin"
/// worked out from fast laps, which no qualifying method ranks on, and
/// would name P2 as pole when the P1 line had been dropped for having no
/// name.
/// </summary>
public sealed class BugHunt8_CommentaryReportFacts_csTests
{
    private static string All(IEnumerable<FactBlock> blocks) => string.Join(" | ", blocks.Select(b => b.Text));

    [Fact]
    public void Pole_is_never_given_a_margin_taken_from_fast_laps()
    {
        // Sam took pole on laps; Bill's fast lap is just slower.
        var blocks = CommentaryReportFacts.QualifyingReport(
            new[]
            {
                new QualifyingLine("A", "Sam", 1, 12.100),
                new QualifyingLine("B", "Bill", 2, 12.900),
            },
            "Box Stock",
            "q-1");

        var text = All(blocks);

        Assert.Contains("Sam takes pole in Box Stock, with a best lap of 12.100s.", text);
        Assert.DoesNotContain(" by ", text);
        Assert.DoesNotContain("a long way", text);
        Assert.DoesNotContain("from Bill", text);
    }

    [Fact]
    public void Pole_without_a_lap_time_is_just_the_name()
    {
        var blocks = CommentaryReportFacts.QualifyingReport(
            new[] { new QualifyingLine("A", "Sam", 1, 0) },
            "Box Stock",
            "q-1");

        Assert.Contains("Sam takes pole in Box Stock.", All(blocks));
    }

    [Fact]
    public void P2_is_not_announced_as_pole_when_the_P1_line_is_missing()
    {
        // The reader skipped P1 (no name) but kept Bill at position 2.
        var blocks = CommentaryReportFacts.QualifyingReport(
            new[]
            {
                new QualifyingLine("B", "Bill", 2, 12.300),
                new QualifyingLine("C", "Pat", 3, 12.500),
            },
            "Box Stock",
            "q-1");

        Assert.DoesNotContain("takes pole", All(blocks));
    }

    [Fact]
    public void The_best_lap_goes_in_with_a_decimal_point_on_a_comma_decimal_PC()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var blocks = CommentaryReportFacts.QualifyingReport(
                new[] { new QualifyingLine("A", "Sam", 1, 12.345) },
                "Box Stock",
                "q-1");

            Assert.Contains("12.345s", All(blocks));
            Assert.DoesNotContain("12,345", All(blocks));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
