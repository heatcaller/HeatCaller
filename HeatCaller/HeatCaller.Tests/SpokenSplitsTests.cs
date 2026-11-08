namespace HeatCaller.Tests;

/// <summary>
/// The spoken split times (22 Oct 2026): the split time read out, the split
/// coach's up-or-down, the battle gap at the split, and the sectors at the
/// flag - their defaults, their words and the arithmetic that decides when
/// they are said.
/// </summary>
public sealed class SpokenSplitsTests
{
    private static readonly AnnouncementEventType[] PracticeOnly =
    {
        AnnouncementEventType.SplitTime,
        AnnouncementEventType.SplitDelta,
        AnnouncementEventType.SplitBattle,
    };

    [Fact]
    public void The_three_live_split_lines_read_in_practice_and_not_in_races()
    {
        var events = VoiceProfile.CreateDefault().Events;

        foreach (var type in PracticeOnly)
        {
            var config = events[type.ToString()];

            Assert.True(config.IsEnabledFor(practice: true), $"{type} is not on in practice");
            Assert.False(config.IsEnabledFor(practice: false), $"{type} is on in races");
            Assert.NotEmpty(config.EffectiveTemplates);
        }
    }

    [Fact]
    public void The_sectors_at_the_flag_start_off()
    {
        var config = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.SectorsRecap)];

        Assert.False(config.IsEnabledFor(practice: false));
        Assert.False(config.IsEnabledFor(practice: true));
        Assert.All(config.EffectiveTemplates, line => Assert.Contains("{sectors}", line));
    }

    [Fact]
    public void Every_new_line_has_a_label_and_names_its_placeholders()
    {
        var expected = new Dictionary<AnnouncementEventType, string[]>
        {
            [AnnouncementEventType.SplitTime] = new[] { "{splittime}", "{split}" },
            [AnnouncementEventType.SplitDelta] = new[] { "{delta}", "At the split" },
            [AnnouncementEventType.SplitBattle] = new[] { "{rival}", "{gap}", "{battle}" },
            [AnnouncementEventType.SectorsRecap] = new[] { "{sectors}" },
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
    public void The_new_events_are_added_at_the_end_so_saved_numbers_keep_their_meaning()
    {
        var all = Enum.GetValues<AnnouncementEventType>();

        Assert.Equal(AnnouncementEventType.SplitRecordPace + 1, AnnouncementEventType.SplitTime);
        Assert.Equal(AnnouncementEventType.SplitTime + 1, AnnouncementEventType.SplitDelta);
        Assert.Equal(AnnouncementEventType.SplitDelta + 1, AnnouncementEventType.SplitBattle);
        Assert.Equal(AnnouncementEventType.SplitBattle + 1, AnnouncementEventType.SectorsRecap);
        // The coaching lines came after these (22 Oct 2026) - see
        // CoachingSpokenTests for the end of the list.
        Assert.Equal(AnnouncementEventType.SectorsRecap + 1, AnnouncementEventType.TargetPace);
        Assert.True(AnnouncementEventType.SectorsRecap < all.Max());
    }

    [Fact]
    public void Lap_times_only_leaves_the_split_time_and_the_coach_as_the_club_has_them()
    {
        Assert.Contains(AnnouncementEventType.SplitTime, LapTimesOnly.LeftAsSet);
        Assert.Contains(AnnouncementEventType.SplitDelta, LapTimesOnly.LeftAsSet);
        Assert.DoesNotContain(AnnouncementEventType.SplitBattle, LapTimesOnly.LeftAsSet);
        Assert.DoesNotContain(AnnouncementEventType.SplitBattle, LapTimesOnly.Spoken);
        Assert.DoesNotContain(AnnouncementEventType.SectorsRecap, LapTimesOnly.Spoken);
    }

    [Theory]
    [InlineData(-100, "up a tenth")]
    [InlineData(-210, "up two tenths")]
    [InlineData(-500, "up half a second")]
    [InlineData(300, "down three tenths")]
    [InlineData(1500, "down a second and a half")]
    [InlineData(40, "level")]
    [InlineData(-49, "level")]
    [InlineData(0, "level")]
    public void The_coach_says_up_down_or_level(long deltaMs, string expected)
    {
        Assert.Equal(expected, AnnouncementService.DeltaSpoken(deltaMs));
    }

    [Fact]
    public void The_coach_on_a_screen_shows_the_figure()
    {
        Assert.Equal("up 0.2s", AnnouncementService.DeltaShown(-210));
        Assert.Equal("down 1.5s", AnnouncementService.DeltaShown(1500));
        Assert.Equal("level", AnnouncementService.DeltaShown(20));
    }

    [Theory]
    [InlineData(1300, "down one point three", "down 1.3s")]
    [InlineData(-1150, "up one point two", "up 1.2s")]
    [InlineData(2700, "down two point seven", "down 2.7s")]
    public void The_coach_says_the_tenth_the_screen_shows(long deltaMs, string spoken, string shown)
    {
        // 8 Nov 2026: "down 1.3s" on the screen was "down a second and a
        // half" from the speaker - GapSpoken's halves.
        Assert.Equal(spoken, AnnouncementService.DeltaSpoken(deltaMs));
        Assert.Equal(shown, AnnouncementService.DeltaShown(deltaMs));
    }

    [Theory]
    [InlineData(1300, "one point three behind your ghost", "+1.3s")]
    [InlineData(-200, "two tenths ahead of your ghost", "-0.2s")]
    public void The_ghost_gap_is_said_to_the_tenth_it_is_shown_to(long gapMs, string spoken, string shown)
    {
        Assert.Equal(spoken, CoachingWords.GhostGapSpoken(gapMs));
        Assert.Equal(shown, CoachingWords.GhostGapShown(gapMs));
    }

    [Fact]
    public void A_line_opened_by_a_value_starts_with_a_capital_after_its_tag()
    {
        Assert.Equal("Up a tenth", AnnouncementService.CapitalisedFirst("up a tenth"));
        Assert.Equal("[excited] Eight point four zero seconds", AnnouncementService.CapitalisedFirst("[excited] eight point four zero seconds"));
        Assert.Equal("Level", AnnouncementService.CapitalisedFirst("Level"));
        Assert.Equal(string.Empty, AnnouncementService.CapitalisedFirst(string.Empty));
    }

    [Fact]
    public void The_sectors_line_names_who_was_quickest_through_each()
    {
        Assert.Equal(
            "Quickest through sector one was Sam, sector two Bill.",
            AnnouncementService.SectorsRecapText(new[] { "Sam", "Bill" }, null));

        Assert.Equal(
            "Quickest through sector one was Sam, sector two Bill, sector three Frank.",
            AnnouncementService.SectorsRecapText(new[] { "Sam", "Bill", "Frank" }, "Quickest through {sectors}."));
    }

    [Fact]
    public void One_driver_quickest_everywhere_is_said_as_that()
    {
        Assert.Equal("Sam was quickest through both sectors.", AnnouncementService.SectorsRecapText(new[] { "Sam", "Sam" }, "Quickest through {sectors}."));
        Assert.Equal("Sam was quickest through every sector.", AnnouncementService.SectorsRecapText(new[] { "Sam", "Sam", "sam" }, null));
    }

    [Fact]
    public void Too_few_named_sectors_say_nothing()
    {
        Assert.Null(AnnouncementService.SectorsRecapText(new[] { "Sam" }, null));
        Assert.Null(AnnouncementService.SectorsRecapText(new[] { "Sam", " " }, null));
        Assert.Null(AnnouncementService.SectorsRecapText(Array.Empty<string>(), null));
    }

    [Fact]
    public void A_sector_nobody_has_a_name_for_is_left_out_but_the_rest_are_said()
    {
        Assert.Equal(
            "Quickest through sector one was Sam, sector three Bill.",
            AnnouncementService.SectorsRecapText(new[] { "Sam", "", "Bill" }, null));
    }

    [Fact]
    public void The_quickest_through_each_sector_is_worked_out_with_ties_to_the_higher_finisher()
    {
        var field = new List<(int Position, long[] Bests)>
        {
            (1, new long[] { 4_100, 5_000 }),
            (2, new long[] { 4_000, 5_200 }),
            (3, new long[] { 4_000, 4_900 }),
        };

        // Sector one: P2 and P3 tie on 4.000 - P2 finished higher. Sector two: P3.
        Assert.Equal(new[] { 1, 2 }, RaceControlControl.QuickestThroughSectors(field, 2));
    }

    [Fact]
    public void A_sector_with_no_time_has_nobody_quickest()
    {
        var field = new List<(int Position, long[] Bests)>
        {
            (1, new long[] { 4_100, 0 }),
        };

        Assert.Equal(new[] { 0, -1 }, RaceControlControl.QuickestThroughSectors(field, 2));
    }

    [Theory]
    [InlineData(10_400, 10_000, 400L)]
    [InlineData(10_999, 10_000, 999L)]
    public void A_fight_at_the_split_is_under_a_second_with_the_chaser_behind(long chaser, long ahead, long expected)
    {
        Assert.Equal(expected, RaceControlControl.SplitBattleGap(chaser, ahead));
    }

    [Theory]
    [InlineData(11_000, 10_000)]
    [InlineData(10_000, 10_400)]
    [InlineData(10_000, 10_000)]
    public void A_second_apart_or_the_chaser_first_is_not_a_fight(long chaser, long ahead)
    {
        Assert.Null(RaceControlControl.SplitBattleGap(chaser, ahead));
    }

    [Fact]
    public void A_pair_is_said_again_only_five_laps_on_and_with_the_gap_moved()
    {
        // Said on lap 4 at 0.6s.
        Assert.False(RaceControlControl.SplitBattleWorthSaying(4, 600, 8, 300));
        Assert.False(RaceControlControl.SplitBattleWorthSaying(4, 600, 9, 650));
        Assert.True(RaceControlControl.SplitBattleWorthSaying(4, 600, 9, 400));
        Assert.True(RaceControlControl.SplitBattleWorthSaying(4, 600, 12, 800));
    }
}
