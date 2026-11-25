namespace HeatCaller.Tests;

/// <summary>
/// The sound-effect settings, including the six sounds added on 26 Sep and
/// how an older settings file takes them in (SoundEffectsStore.FillMissing).
/// </summary>
public sealed class SoundEffectsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly SoundEffectKind[] AddedOn26Sep =
    {
        SoundEffectKind.PersonalBest,
        SoundEffectKind.CountdownTicks,
        SoundEffectKind.JumpStart,
        SoundEffectKind.YellowFlag,
        SoundEffectKind.RedFlag,
        SoundEffectKind.GreenFlagResumed,
    };

    [Fact]
    public void The_defaults_have_a_setting_for_every_sound()
    {
        var defaults = SoundEffectsProfile.CreateDefault();

        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            Assert.True(defaults.Effects.ContainsKey(kind.ToString()), $"No default for {kind}");
        }
    }

    [Fact]
    public void The_new_sounds_start_switched_off()
    {
        var defaults = SoundEffectsProfile.CreateDefault();

        foreach (var kind in AddedOn26Sep)
        {
            Assert.False(defaults.Effects[kind.ToString()].Enabled, $"{kind} is on by default");
        }

        // And a personal best stays off in practice even once switched on.
        Assert.False(defaults.Effects[nameof(SoundEffectKind.PersonalBest)].IsEnabledFor(practice: true));
    }

    [Fact]
    public void Personal_best_is_a_lap_sound_and_the_flags_are_flag_sounds()
    {
        Assert.Contains(SoundEffectKind.PersonalBest, SoundEffectsProfile.PerLapKinds);

        Assert.Equal(
            new[] { SoundEffectKind.YellowFlag, SoundEffectKind.RedFlag, SoundEffectKind.GreenFlagResumed }.OrderBy(k => k),
            SoundEffectsProfile.FlagKinds.OrderBy(k => k));
    }

    [Fact]
    public void An_older_file_gets_the_new_sounds_on_classic_like_its_others()
    {
        var older = new SoundEffectsProfile();
        older.Effects[nameof(SoundEffectKind.GoodLap)] = new SoundEffectConfig { Enabled = true, BuiltInSound = "PitLane" };
        older.Effects[nameof(SoundEffectKind.FastLap)] = new SoundEffectConfig { Enabled = true, BuiltInSound = "PitLane" };
        older.Effects[nameof(SoundEffectKind.RaceStarted)] = new SoundEffectConfig { Enabled = true, BuiltInSound = "Podium" };

        SoundEffectsStore.FillMissing(older);

        // Classic for all of them since 25 Nov 2026, when it became the only
        // pack (see SoundPacks.EffectsKey): the new sounds used to take the
        // pack most of the others were on.
        foreach (var kind in AddedOn26Sep)
        {
            Assert.Equal(SoundPacks.EffectsKey, older.Effects[kind.ToString()].BuiltInSound);
        }

        Assert.Equal(SoundPacks.EffectsKey, older.Effects[nameof(SoundEffectKind.RaceStarted)].BuiltInSound);
        Assert.True(older.Effects[nameof(SoundEffectKind.RaceStarted)].Enabled);
    }

    [Fact]
    public void A_file_with_no_sounds_at_all_gets_the_classic_pack()
    {
        var empty = new SoundEffectsProfile();

        SoundEffectsStore.FillMissing(empty);

        Assert.All(empty.Effects.Values, config => Assert.Equal(SoundPacks.EffectsKey, config.BuiltInSound));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_club_that_heard_the_invalid_lap_buzzer_on_jump_starts_keeps_a_jump_start_sound(bool invalidLapOn)
    {
        var older = new SoundEffectsProfile();
        older.Effects[nameof(SoundEffectKind.InvalidLap)] = new SoundEffectConfig { Enabled = invalidLapOn, EnabledInPractice = false };

        SoundEffectsStore.FillMissing(older);

        var jump = older.Effects[nameof(SoundEffectKind.JumpStart)];
        Assert.Equal(invalidLapOn, jump.Enabled);
        Assert.False(jump.EnabledInPractice);
    }

    [Fact]
    public void A_jump_start_setting_already_in_the_file_is_left_alone()
    {
        var file = new SoundEffectsProfile();
        file.Effects[nameof(SoundEffectKind.InvalidLap)] = new SoundEffectConfig { Enabled = true };
        file.Effects[nameof(SoundEffectKind.JumpStart)] = new SoundEffectConfig { Enabled = false };

        SoundEffectsStore.FillMissing(file);

        Assert.False(file.Effects[nameof(SoundEffectKind.JumpStart)].Enabled);
    }

    [Fact]
    public void A_null_entry_in_the_file_is_replaced_rather_than_crashing_later()
    {
        var file = new SoundEffectsProfile();
        file.Effects[nameof(SoundEffectKind.GoodLap)] = null!;

        SoundEffectsStore.FillMissing(file);

        Assert.NotNull(file.Effects[nameof(SoundEffectKind.GoodLap)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_streak_is_never_shorter_than_two_laps(int configured)
    {
        var profile = new SoundEffectsProfile { StreakThreshold = configured };

        Assert.Equal(2, profile.StreakThresholdFor(practice: false));
    }

    [Fact]
    public void Settings_saved_are_the_settings_loaded()
    {
        var store = SoundEffectsStore.Load();
        var changed = SoundEffectsStore.Copy(store.Current);
        changed.Effects[nameof(SoundEffectKind.YellowFlag)].Enabled = true;
        changed.Effects[nameof(SoundEffectKind.YellowFlag)].CustomFilePath = @"C:Club Soundsyellow.wav";

        store.Save(changed);

        var reloaded = SoundEffectsStore.Load().Current.Effects[nameof(SoundEffectKind.YellowFlag)];
        Assert.True(reloaded.Enabled);
        Assert.Equal(@"C:Club Soundsyellow.wav", reloaded.CustomFilePath);
        Assert.Equal(SoundPacks.EffectsKey, reloaded.BuiltInSound);
    }
}
