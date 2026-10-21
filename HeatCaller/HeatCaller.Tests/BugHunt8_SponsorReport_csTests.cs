namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): the share a sponsor GOT was worked out over
/// every sponsor's reads, switched-off ones included, while the share they
/// BOUGHT left switched-off sponsors out - so a sponsor read early and then
/// switched off made everybody still running look short.
/// </summary>
public sealed class BugHunt8_SponsorReport_csTests
{
    private static SponsorBooking Sponsor(string id, string name, bool enabled = true) =>
        new() { Id = id, Name = name, Weight = 1, Enabled = enabled, SalesCopy = "Pies." };

    private static readonly DateTime Night = new(2026, 10, 11, 21, 0, 0, DateTimeKind.Local);

    [Fact]
    public void A_sponsor_switched_off_after_their_reads_does_not_shrink_everyone_elses_share()
    {
        var report = SponsorReport.ForTheNight(
            new[]
            {
                Sponsor("a", "Ralph's Tavern", enabled: false),
                Sponsor("b", "Quickway Diner"),
                Sponsor("c", "Corner Garage"),
            },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 4, ["b"] = 3, ["c"] = 3 },
            SponsorPlacement.Everywhere,
            "Club Night 14",
            Night);

        // The headline still counts every read that went out.
        Assert.Contains("10 reads went out", report);

        Assert.Contains("Ralph's Tavern: 4 reads - switched off for this night", report);
        // 3 of the 10 is not "50% of the evening", so the line names the
        // reads it is a share of: the ones to sponsors still in the rotation.
        Assert.Contains("Quickway Diner: 3 reads (50% of the reads to sponsors in tonight's rotation; they bought 50%)", report);
        Assert.Contains("Corner Garage: 3 reads (50% of the reads to sponsors in tonight's rotation; they bought 50%)", report);
        Assert.DoesNotContain("of the evening's reads", report);
    }
}
