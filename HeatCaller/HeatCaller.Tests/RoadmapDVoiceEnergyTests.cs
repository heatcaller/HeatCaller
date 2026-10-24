namespace HeatCaller.Tests;

/// <summary>
/// Roadmap, 22 Oct 2026 - the delivery following how big the race is
/// (DeliveryEnergy), and the model told to write for the ear.
/// </summary>
public sealed class RoadmapDVoiceEnergyTests
{
    [Fact]
    public void The_top_main_is_livelier_and_the_heats_are_steadier()
    {
        Assert.Equal(DeliveryEnergy.Steady, DeliveryEnergies.For(new RaceStature(RaceBilling.Practice, false)));
        Assert.Equal(DeliveryEnergy.Steady, DeliveryEnergies.For(new RaceStature(RaceBilling.Heat, false)));
        Assert.Equal(DeliveryEnergy.Normal, DeliveryEnergies.For(new RaceStature(RaceBilling.LesserMain, false)));
        Assert.Equal(DeliveryEnergy.Lively, DeliveryEnergies.For(new RaceStature(RaceBilling.TopMain, false)));
    }

    [Fact]
    public void The_last_race_of_the_night_goes_up_a_step()
    {
        Assert.Equal(DeliveryEnergy.Normal, DeliveryEnergies.For(new RaceStature(RaceBilling.Heat, true)));
        Assert.Equal(DeliveryEnergy.Lively, DeliveryEnergies.For(new RaceStature(RaceBilling.LesserMain, true)));
        Assert.Equal(DeliveryEnergy.Lively, DeliveryEnergies.For(new RaceStature(RaceBilling.TopMain, true)));
    }

    [Fact]
    public void A_club_with_no_programme_keeps_its_own_voice()
    {
        Assert.Equal(DeliveryEnergy.Normal, DeliveryEnergies.For(RaceStature.Unknown));
    }

    [Fact]
    public void Normal_is_the_clubs_own_settings_exactly()
    {
        Assert.Equal((0.5, 0.2), DeliveryEnergies.Apply(0.5, 0.2, DeliveryEnergy.Normal));
    }

    [Fact]
    public void A_step_moves_a_tenth_each_way()
    {
        Assert.Equal((0.4, 0.3), DeliveryEnergies.Apply(0.5, 0.2, DeliveryEnergy.Lively));
        Assert.Equal((0.6, 0.1), DeliveryEnergies.Apply(0.5, 0.2, DeliveryEnergy.Steady));
    }

    [Fact]
    public void A_livelier_step_never_wanders_past_the_safe_bounds()
    {
        // Stability stops at 0.30 and style at 0.50.
        Assert.Equal((0.3, 0.5), DeliveryEnergies.Apply(0.35, 0.45, DeliveryEnergy.Lively));

        // A club already past them is left where it chose to be, never pushed further.
        Assert.Equal((0.2, 0.7), DeliveryEnergies.Apply(0.2, 0.7, DeliveryEnergy.Lively));
    }

    [Fact]
    public void A_steadier_step_never_flattens_past_the_bound()
    {
        Assert.Equal((0.85, 0.0), DeliveryEnergies.Apply(0.8, 0.05, DeliveryEnergy.Steady));
        Assert.Equal((0.95, 0.0), DeliveryEnergies.Apply(0.95, 0.0, DeliveryEnergy.Steady));
    }

    [Fact]
    public void Only_a_clubs_own_ElevenLabs_voice_on_a_numeric_model_is_stepped()
    {
        Assert.True(DeliveryEnergies.AppliesTo(new VoiceProfile { Engine = VoiceEngine.ElevenLabs, ModelId = "eleven_flash_v2_5" }));
        Assert.False(DeliveryEnergies.AppliesTo(new VoiceProfile { Engine = VoiceEngine.ElevenLabs, ModelId = "eleven_v3" }));
        Assert.False(DeliveryEnergies.AppliesTo(new VoiceProfile { Engine = VoiceEngine.HeatCaller, ModelId = "eleven_flash_v2_5" }));
        Assert.False(DeliveryEnergies.AppliesTo(new VoiceProfile { Engine = VoiceEngine.Kokoro, ModelId = "eleven_flash_v2_5" }));
    }

    [Fact]
    public void The_steps_are_three_so_a_line_has_three_takes_at_most()
    {
        Assert.Equal(3, Enum.GetValues<DeliveryEnergy>().Length);
        Assert.Equal(new[] { DeliveryEnergy.Normal, DeliveryEnergy.Lively }, DeliveryEnergies.Others(DeliveryEnergy.Steady));
    }

    [Fact]
    public void A_stepped_take_is_saved_apart_from_the_clubs_own()
    {
        // The cache must tell them apart, or a steadier take would be played
        // for the top main - and the club's own take must keep its key, or
        // every prepared line would be lost.
        var own = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, ModelId = "eleven_flash_v2_5", VoiceId = "v", Stability = 0.5, Style = 0.2 };
        var (stability, style) = DeliveryEnergies.Apply(own.Stability, own.Style, DeliveryEnergy.Lively);
        var lively = own.CloneForDelivery(stability, style);

        Assert.NotEqual(VoiceLibrary.KeyFor(own, "Green flag!"), VoiceLibrary.KeyFor(lively, "Green flag!"));
        Assert.Equal(VoiceLibrary.KeyFor(own, "Green flag!"), VoiceLibrary.KeyFor(own.CloneForDelivery(own.Stability, own.Style), "Green flag!"));
    }

    [Fact]
    public void The_writer_is_told_to_write_for_the_ear()
    {
        Assert.Contains("contractions", CommentaryService.SpokenStyleLine);
        Assert.Contains("fragments", CommentaryService.SpokenStyleLine);
        Assert.Contains("Into the hairpin. Side by side.", CommentaryService.SpokenStyleLine);
    }

    [Fact]
    public void The_voice_settings_box_shows_only_where_it_changes_something()
    {
        Assert.True(VoiceSettingsControl.EnergyBoxApplies(true, "eleven_flash_v2_5"));
        Assert.False(VoiceSettingsControl.EnergyBoxApplies(true, "eleven_v3"));
        Assert.False(VoiceSettingsControl.EnergyBoxApplies(false, "eleven_flash_v2_5"));
    }

    [Fact]
    public void The_switch_is_on_for_a_club_that_never_touched_it()
    {
        Assert.True(new AppSettings().VoiceEnergyFollowsRace);
    }
}
