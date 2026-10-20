namespace HeatCaller.Tests;

/// <summary>
/// How the booth sounds against how the recap sounds (18 Oct 2026).
///
/// The claim being pinned is narrow and worth stating plainly: the booth
/// interview is always STEADIER AND LESS PERFORMED than the recap it sits
/// inside, and a club that never asked for broadcast voice settings is
/// never given any.
/// </summary>
public sealed class BoothTreatmentTests
{
    [Fact]
    public void The_booth_is_steadier_and_less_performed_than_the_recap()
    {
        var recap = (Stability: 0.3, Style: 0.8);

        var booth = BoothTreatment.Delivery(recap);

        Assert.NotNull(booth);
        Assert.True(booth!.Value.Stability > recap.Stability, "the booth should be steadier than the recap");
        Assert.True(booth.Value.Style < recap.Style, "the booth should be less performed than the recap");
    }

    [Fact]
    public void A_club_that_never_asked_for_broadcast_voice_settings_is_not_given_any()
    {
        // Null is a club that has NOT turned on SummaryUseBroadcastVoice -
        // it wants its own voice profile used as configured, everywhere.
        // Handing it a booth setting would override a switch the club
        // deliberately left off.
        Assert.Null(BoothTreatment.Delivery(null));
        Assert.Equal(string.Empty, BoothTreatment.Describe(null));
    }

    [Fact]
    public void It_goes_halfway_to_flat_and_never_all_the_way()
    {
        // Stability at 1 is not calm, it is inert - the same voice reading
        // a shopping list - and a principal accounting for a night his
        // team got wrong still has something at stake.
        var booth = BoothTreatment.Delivery((0.0, 1.0));

        Assert.NotNull(booth);
        Assert.Equal(0.5, booth!.Value.Stability, 3);
        Assert.True(booth.Value.Stability < 1.0);
    }

    [Fact]
    public void It_keeps_half_the_shows_character_rather_than_cutting_it_out()
    {
        // The booth is still part of the same broadcast; cutting style to
        // nothing would make it sound like a different app wrote it.
        var booth = BoothTreatment.Delivery((0.5, 0.6));

        Assert.NotNull(booth);
        Assert.Equal(0.3, booth!.Value.Style, 3);
        Assert.True(booth.Value.Style > 0.0);
    }

    [Fact]
    public void An_already_flat_recap_leaves_the_booth_flat_rather_than_over_one()
    {
        var booth = BoothTreatment.Delivery((1.0, 0.0));

        Assert.NotNull(booth);
        Assert.Equal(1.0, booth!.Value.Stability, 3);
        Assert.Equal(0.0, booth.Value.Style, 3);
    }

    [Theory]
    [InlineData(-5.0, 9.0)]
    [InlineData(4.0, -2.0)]
    public void A_hand_edited_settings_file_cannot_push_the_booth_out_of_range(double stability, double style)
    {
        // The two constants are applied to numbers that come from a club's
        // own settings box, and a hand-edited settings file can hold
        // anything.
        var booth = BoothTreatment.Delivery((stability, style));

        Assert.NotNull(booth);
        Assert.InRange(booth!.Value.Stability, 0.0, 1.0);
        Assert.InRange(booth.Value.Style, 0.0, 1.0);
    }

    [Fact]
    public void The_console_says_what_the_booth_is_doing_when_it_does_it()
    {
        // The change is inaudible as a CAUSE: a club that thinks the booth
        // sounds flat has no other way to tell whether that is this
        // calculation, their voice profile, or the model writing short
        // answers.
        var said = BoothTreatment.Describe((0.3, 0.8));

        Assert.Contains("booth", said);
        Assert.Contains("stability", said);
        Assert.Contains("style", said);
    }
}
