namespace HeatCaller.Tests;

/// <summary>
/// The winner's mechanic is the Technical Lead now (17 Oct 2026). The
/// interview prompt asks for "TECH LEAD:" lines, and an answer written with
/// the old "MECHANIC:" label, or the long form, still splits the same way.
/// </summary>
public sealed class TechnicalLeadTests
{
    private static readonly string[] Names = { "Asher", "Wrench", "Audi R8" };

    [Theory]
    [InlineData("TECH LEAD")]
    [InlineData("Technical Lead")]
    [InlineData("MECHANIC")]
    public void The_interview_splits_under_the_new_label_and_the_old_one(string label)
    {
        var text =
            $"HOST: Wrench, what a drive from Asher! How did it feel? {label}: It was on rails all night long. "
            + $"HOST: And what did you change? {label}: We went up a tooth on the pinion before the main.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names);

        Assert.NotNull(interview);
        Assert.Equal(2, interview!.Count);
        Assert.Equal("It was on rails all night long.", interview[0].Answer);
        Assert.Equal("We went up a tooth on the pinion before the main.", interview[1].Answer);
    }

    [Fact]
    public void A_host_saying_tech_with_a_colon_is_not_cut_in_two()
    {
        const string text =
            "HOST: Wrench, let's talk tech: what changed on the car? TECH LEAD: We went up a tooth on the pinion.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names);

        Assert.NotNull(interview);
        Assert.Equal("Wrench, let's talk tech: what changed on the car?", interview![0].Question);
    }

    [Fact]
    public void The_segment_is_named_for_the_technical_lead()
    {
        Assert.Equal("Technical Lead's interview", CommentaryBudget.Spec(SegmentKind.MechanicInterview).Title);
    }
}
