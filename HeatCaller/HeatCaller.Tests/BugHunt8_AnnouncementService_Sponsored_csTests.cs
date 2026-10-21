namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): a sponsored routine call (fast lap, lead
/// change, battle) took the sponsor's ration as the line was built, and
/// when the queue then dropped the call nothing gave the ration back - so
/// the sponsor lost "Presented by" for three minutes over a line nobody
/// heard. AnnouncementService.SponsorTag now hands back a Release, which
/// gives back only its own reservation. These pin down that give-back on
/// SponsoredMomentGate, which is where the ration lives.
/// </summary>
public sealed class BugHunt8_AnnouncementService_Sponsored_csTests
{
    private static readonly DateTime Night = new(2026, 10, 20, 20, 0, 0, DateTimeKind.Local);

    [Fact]
    public void A_dropped_line_gives_the_ration_back_so_the_next_one_is_named()
    {
        var now = Night;
        var gate = new SponsoredMomentGate(() => now);

        Assert.True(gate.TryReserve(SponsoredMoment.LeadChange, out var stamp, out var previous));
        Assert.Null(previous);

        // The queue dropped that lead change; 40 seconds later a real one comes.
        gate.Release(SponsoredMoment.LeadChange, stamp, previous);
        now = now.AddSeconds(40);

        Assert.True(gate.TryReserve(SponsoredMoment.LeadChange));
    }

    [Fact]
    public void Without_a_release_the_ration_still_holds_the_next_line_back()
    {
        var now = Night;
        var gate = new SponsoredMomentGate(() => now);

        Assert.True(gate.TryReserve(SponsoredMoment.LeadChange));
        now = now.AddSeconds(40);

        Assert.False(gate.TryReserve(SponsoredMoment.LeadChange));
    }

    [Fact]
    public void A_release_puts_back_the_last_real_mention_rather_than_clearing_it()
    {
        var now = Night;
        var gate = new SponsoredMomentGate(() => now);

        gate.Mentioned(SponsoredMoment.FastestLap, "b");
        now = now.AddMinutes(3);

        Assert.True(gate.TryReserve(SponsoredMoment.FastestLap, out var stamp, out var previous));
        Assert.Equal((DateTime?)Night, previous);
        gate.Release(SponsoredMoment.FastestLap, stamp, previous);

        // The ration runs from the mention at 20:00 again, which is over.
        Assert.True(gate.CanMention(SponsoredMoment.FastestLap));

        now = Night.AddMinutes(1);
        Assert.False(gate.CanMention(SponsoredMoment.FastestLap));
    }

    [Fact]
    public void A_release_never_undoes_a_mention_that_came_after_it()
    {
        var now = Night;
        var gate = new SponsoredMomentGate(() => now);

        Assert.True(gate.TryReserve(SponsoredMoment.Battle, out var stamp, out var previous));
        now = now.AddSeconds(5);
        gate.Mentioned(SponsoredMoment.Battle, "b");

        gate.Release(SponsoredMoment.Battle, stamp, previous);

        now = now.AddSeconds(30);
        Assert.False(gate.CanMention(SponsoredMoment.Battle));
        Assert.Equal(1, gate.MentionsTonight()["b"][SponsoredMoment.Battle]);
    }
}
