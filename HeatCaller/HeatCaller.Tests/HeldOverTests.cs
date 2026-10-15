namespace HeatCaller.Tests;

/// <summary>
/// The one call kept for the end of the line being spoken - see HeldOver.
///
/// THE REPORT: a lap time two seconds into its sentence, a change of lead
/// arriving behind it, and the lead change thrown away with "still speaking
/// the previous one". The biggest call in the set lost to the smallest by
/// arriving second. These are the rules that stop that without bringing
/// back the backlog the drop-while-speaking rule was written to prevent.
/// </summary>
public sealed class HeldOverTests
{
    private const int LapTime = 30;
    private const int LapCount = 40;
    private const int Streak = 70;
    private const int FastLap = 90;
    private const int LeadChange = 100;

    private sealed record Call(string What);

    private DateTime _now = new(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc);

    private HeldOver<Call> Slot() => new(() => _now);

    [Fact]
    public void A_lead_change_arriving_behind_a_lap_time_is_kept_rather_than_lost()
    {
        var slot = Slot();

        Assert.True(slot.Offer(new Call("lead"), LeadChange, LapTime, out var displaced));
        Assert.Null(displaced);

        Assert.Equal("lead", slot.Take(out var stale)?.What);
        Assert.Null(stale);
    }

    [Fact]
    public void A_small_call_behind_another_small_call_is_lost_as_it_always_was()
    {
        // A lap count behind a lap time is the chatter the drop rule exists
        // for. Keeping it would rebuild the backlog one slot at a time.
        var slot = Slot();

        Assert.False(slot.Offer(new Call("count"), LapCount, LapTime, out _));
        Assert.False(slot.IsHolding);
    }

    [Fact]
    public void A_call_must_clearly_outweigh_the_line_being_spoken_the_same_way_it_must_to_break_the_gap()
    {
        var slot = Slot();

        // A fast lap (90) over a streak (70) is twenty points - not enough.
        Assert.False(slot.Offer(new Call("fast"), FastLap, Streak, out _));

        // Over a lap count (40) it is fifty - kept.
        Assert.True(slot.Offer(new Call("fast"), FastLap, LapCount, out _));
    }

    [Fact]
    public void Only_one_call_is_ever_kept_and_a_bigger_one_takes_the_slot()
    {
        var slot = Slot();

        Assert.True(slot.Offer(new Call("fast"), FastLap, LapTime, out _));
        Assert.True(slot.Offer(new Call("lead"), LeadChange, LapTime, out var displaced));

        // The fast lap is handed back so the caller can count it as lost -
        // it now will be.
        Assert.Equal("fast", displaced?.What);
        Assert.Equal("lead", slot.Take(out _)?.What);
        Assert.Null(slot.Take(out _));
    }

    [Fact]
    public void A_smaller_or_equal_call_does_not_push_out_the_one_kept()
    {
        var slot = Slot();

        Assert.True(slot.Offer(new Call("lead"), LeadChange, LapTime, out _));
        Assert.False(slot.Offer(new Call("fast"), FastLap, LapTime, out _));
        Assert.False(slot.Offer(new Call("lead again"), LeadChange, LapTime, out _));

        Assert.Equal("lead", slot.Take(out _)?.What);
    }

    [Fact]
    public void A_call_kept_too_long_is_dropped_rather_than_said_late()
    {
        // A lead change said after the lead has changed back is worse than
        // one never said.
        var slot = Slot();

        slot.Offer(new Call("lead"), LeadChange, LapTime, out _);

        _now += AnnouncerDensity.HoldOverFor + TimeSpan.FromMilliseconds(1);

        Assert.Null(slot.Take(out var stale));
        Assert.Equal("lead", stale?.What);
        Assert.False(slot.IsHolding);
    }

    [Fact]
    public void A_stale_kept_call_is_no_rival_to_a_fresh_one()
    {
        var slot = Slot();

        slot.Offer(new Call("old lead"), LeadChange, LapTime, out _);

        _now += AnnouncerDensity.HoldOverFor + TimeSpan.FromSeconds(1);

        Assert.True(slot.Offer(new Call("fast"), FastLap, LapTime, out var displaced));
        Assert.Equal("old lead", displaced?.What);
        Assert.Equal("fast", slot.Take(out _)?.What);
    }

    [Fact]
    public void A_flag_takes_whatever_was_kept()
    {
        var slot = Slot();

        slot.Offer(new Call("lead"), LeadChange, LapTime, out _);

        Assert.Equal("lead", slot.Clear()?.What);
        Assert.Null(slot.Take(out var stale));
        Assert.Null(stale);
    }

    [Fact]
    public void The_field_window_is_short_and_the_hold_is_a_few_seconds()
    {
        // The window is ON TOP of every lap call's latency, so it has to
        // stay well under a second; the hold has to outlast a lap call
        // without outlasting the moment.
        Assert.InRange(AnnouncerDensity.FieldWindow.TotalMilliseconds, 100, 600);
        Assert.InRange(AnnouncerDensity.HoldOverFor.TotalSeconds, 2, 6);
    }
}
