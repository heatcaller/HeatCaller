namespace HeatCaller.Tests;

/// <summary>
/// The coaching (22 Oct 2026): the target lap's words, the consistency
/// block's words and arithmetic, which tone a lap earns, the targets kept
/// per driver for the night, and the two new lines' defaults.
/// </summary>
public sealed class CoachingSpokenTests
{
    [Theory]
    [InlineData(9200, 9200, "on target")]
    [InlineData(9240, 9200, "on target")]
    [InlineData(9160, 9200, "on target")]
    [InlineData(9400, 9200, "two tenths off")]
    [InlineData(9100, 9200, "a tenth under")]
    [InlineData(9700, 9200, "half a second off")]
    public void The_target_lap_is_said_as_on_off_or_under(long lapMs, long targetMs, string expected) =>
        Assert.Equal(expected, CoachingWords.PaceSpoken(lapMs, targetMs));

    [Fact]
    public void The_target_lap_on_a_screen_shows_the_figure()
    {
        Assert.Equal("+0.2s", CoachingWords.PaceShown(9400, 9200));
        Assert.Equal("-0.1s", CoachingWords.PaceShown(9100, 9200));
        Assert.Equal("on target", CoachingWords.PaceShown(9220, 9200));
    }

    [Fact]
    public void A_lap_half_as_long_again_as_the_best_is_not_coached_on()
    {
        Assert.True(CoachingWords.IsCleanLap(13_800, 9200));
        Assert.False(CoachingWords.IsCleanLap(13_801, 9200));
        Assert.True(CoachingWords.IsCleanLap(5000, 0));
        Assert.False(CoachingWords.IsCleanLap(0, 9200));

        Assert.Equal(new long[] { 9200, 9300, 9250 }, CoachingWords.CleanLaps(new long[] { 9200, 9300, 20_000, 9250, 0 }));
        Assert.Empty(CoachingWords.CleanLaps(Array.Empty<long>()));
    }

    [Fact]
    public void The_spread_is_the_slowest_less_the_quickest()
    {
        Assert.Equal(100, CoachingWords.Spread(new long[] { 9200, 9300, 9250 }));
        Assert.Equal(0, CoachingWords.Spread(new long[] { 9200 }));
    }

    [Theory]
    [InlineData(5, 100, null, "last five within a tenth - that's your rhythm")]
    [InlineData(5, 40, null, "last five within a tenth - that's your rhythm")]
    [InlineData(5, 300, null, "last five within three tenths")]
    [InlineData(5, 600, null, "spread's six tenths over the last five - settle it down")]
    [InlineData(5, 100, 400L, "last five within a tenth - that's your rhythm, tighter than the five before")]
    [InlineData(5, 600, 300L, "spread's six tenths over the last five - settle it down, looser than the five before")]
    [InlineData(5, 120, 100L, "last five within a tenth - that's your rhythm")]
    [InlineData(8, 300, null, "last eight within three tenths")]
    public void The_consistency_line_says_how_far_apart_and_which_way(int laps, long spreadMs, long? beforeMs, string expected) =>
        Assert.Equal(expected, CoachingWords.ConsistencySpoken(laps, spreadMs, beforeMs));

    [Fact]
    public void A_new_best_is_its_own_tone_whatever_the_target()
    {
        Assert.Equal(CoachTone.NewBest, CoachingWords.ToneFor(9000, 9200, null));
        Assert.Equal(CoachTone.NewBest, CoachingWords.ToneFor(9000, 9200, 8500));
    }

    [Fact]
    public void Against_a_target_the_tone_is_quicker_on_it_or_slower()
    {
        Assert.Equal(CoachTone.Faster, CoachingWords.ToneFor(9100, 9000, 9200));
        Assert.Equal(CoachTone.OnTarget, CoachingWords.ToneFor(9230, 9000, 9200));
        Assert.Equal(CoachTone.Slower, CoachingWords.ToneFor(9400, 9000, 9200));

        // No best yet: the target alone.
        Assert.Equal(CoachTone.Faster, CoachingWords.ToneFor(9000, 0, 9200));
    }

    [Fact]
    public void Without_a_target_the_tone_is_against_the_best()
    {
        Assert.Equal(CoachTone.OnTarget, CoachingWords.ToneFor(9220, 9200, null));
        Assert.Equal(CoachTone.Slower, CoachingWords.ToneFor(9500, 9200, null));
    }

    [Fact]
    public void Nothing_to_measure_against_or_a_spin_earns_no_tone()
    {
        Assert.Null(CoachingWords.ToneFor(9000, 0, null));
        Assert.Null(CoachingWords.ToneFor(20_000, 9000, 9200));
        Assert.Null(CoachingWords.ToneFor(0, 9000, 9200));
    }

    [Theory]
    [InlineData(-100, CoachTone.Faster)]
    [InlineData(30, CoachTone.OnTarget)]
    [InlineData(-49, CoachTone.OnTarget)]
    [InlineData(200, CoachTone.Slower)]
    public void At_the_split_the_tone_follows_the_up_or_down(long deltaMs, CoachTone expected) =>
        Assert.Equal(expected, CoachingWords.ToneAtSplit(deltaMs));

    [Fact]
    public void The_tones_are_short_and_told_apart_by_pitch_and_shape()
    {
        var faster = ToneGenerator.CoachNotes(CoachTone.Faster);
        var onTarget = ToneGenerator.CoachNotes(CoachTone.OnTarget);
        var slower = ToneGenerator.CoachNotes(CoachTone.Slower);
        var newBest = ToneGenerator.CoachNotes(CoachTone.NewBest);

        Assert.Single(faster);
        Assert.Single(onTarget);
        Assert.Single(slower);
        Assert.True(faster[0].Freq > onTarget[0].Freq && onTarget[0].Freq > slower[0].Freq);

        // The new best is two notes, rising.
        Assert.Equal(2, newBest.Length);
        Assert.True(newBest[1].Freq > newBest[0].Freq);

        foreach (var notes in new[] { faster, onTarget, slower, newBest })
        {
            Assert.True(notes.Sum(n => n.DurationMs) <= 160, "A coaching tone outlasts a car's crossing.");
        }

        Assert.True(ToneGenerator.CoachToneLevel < 1f);
    }

    [Theory]
    [InlineData("9.2", 9200L)]
    [InlineData("9.20", 9200L)]
    [InlineData("9,25", 9250L)]
    [InlineData("9.2s", 9200L)]
    [InlineData(" 12.345 ", 12_345L)]
    [InlineData("0.5", null)]
    [InlineData("700", null)]
    [InlineData("fast", null)]
    [InlineData("", null)]
    public void A_target_is_typed_as_seconds(string typed, long? expected) =>
        Assert.Equal(expected, CoachingWords.ParseTarget(typed));

    [Fact]
    public void A_target_is_kept_per_driver_for_the_night()
    {
        DriverTargets.ClearAll();

        try
        {
            Assert.True(DriverTargets.Set("T-100", 9200));
            Assert.True(DriverTargets.Set("T-200", 10_500));

            Assert.Equal(9200, DriverTargets.TargetMs("t-100"));
            Assert.Equal(10_500, DriverTargets.TargetMs("T-200"));
            Assert.Null(DriverTargets.TargetMs("T-300"));
            Assert.Null(DriverTargets.TargetMs(null));
            Assert.Equal(2, DriverTargets.All().Count);

            // Under a second is not a lap.
            Assert.False(DriverTargets.Set("T-300", 500));

            Assert.True(DriverTargets.Clear("T-100"));
            Assert.False(DriverTargets.Clear("T-100"));
            Assert.Null(DriverTargets.TargetMs("T-100"));
            Assert.Equal(10_500, DriverTargets.TargetMs("T-200"));
        }
        finally
        {
            DriverTargets.ClearAll();
        }
    }

    [Fact]
    public void The_coaching_lines_are_on_in_practice_and_off_in_races()
    {
        var events = VoiceProfile.CreateDefault().Events;

        var target = events[nameof(AnnouncementEventType.TargetPace)];
        Assert.True(target.IsEnabledFor(practice: true));
        Assert.False(target.IsEnabledFor(practice: false));
        Assert.All(target.EffectiveTemplates, line => Assert.Contains("{pace}", line));
        Assert.Equal(1, target.TargetPaceEveryNLaps);

        var consistency = events[nameof(AnnouncementEventType.ConsistencyCoach)];
        Assert.True(consistency.IsEnabledFor(practice: true));
        Assert.False(consistency.IsEnabledFor(practice: false));
        Assert.All(consistency.EffectiveTemplates, line => Assert.Contains("{consistency}", line));
        Assert.Equal(5, consistency.ConsistencyEveryLaps);
    }

    [Fact]
    public void Each_coaching_line_has_a_label_and_names_its_placeholders()
    {
        var expected = new Dictionary<AnnouncementEventType, string[]>
        {
            [AnnouncementEventType.TargetPace] = new[] { "{pace}", "{target}", "Target lap..." },
            [AnnouncementEventType.ConsistencyCoach] = new[] { "{consistency}", "{spread}", "{laps}", "Consistency" },
            [AnnouncementEventType.GhostGap] = new[] { "{ghostgap}", "{gap}", "Gap to their ghost" },
        };

        foreach (var (type, words) in expected)
        {
            var (label, hint) = VoiceProfile.Describe(type);

            Assert.NotEqual(type.ToString(), label);

            foreach (var word in words)
            {
                Assert.Contains(word, hint, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_coaching_lines_are_added_at_the_end_so_saved_numbers_keep_their_meaning()
    {
        Assert.Equal(AnnouncementEventType.SectorsRecap + 1, AnnouncementEventType.TargetPace);
        Assert.Equal(AnnouncementEventType.TargetPace + 1, AnnouncementEventType.ConsistencyCoach);

        // The gap to the ghost came after them (22 Oct 2026).
        Assert.Equal(AnnouncementEventType.ConsistencyCoach + 1, AnnouncementEventType.GhostGap);
        Assert.Equal(AnnouncementEventType.GhostGap, Enum.GetValues<AnnouncementEventType>().Max());
    }

    [Fact]
    public void Lap_times_only_leaves_the_coaching_as_the_club_has_it()
    {
        Assert.Contains(AnnouncementEventType.TargetPace, LapTimesOnly.LeftAsSet);
        Assert.Contains(AnnouncementEventType.ConsistencyCoach, LapTimesOnly.LeftAsSet);
        Assert.Contains(AnnouncementEventType.GhostGap, LapTimesOnly.LeftAsSet);
    }

    [Fact]
    public void An_old_settings_file_reads_the_coaching_cadences_as_their_defaults()
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<AnnouncementEventConfig>("{\"Enabled\":true}")!;

        Assert.Equal(1, config.TargetPaceEveryNLaps);
        Assert.Equal(5, config.ConsistencyEveryLaps);
    }
}
