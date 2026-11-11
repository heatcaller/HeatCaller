namespace HeatCaller.Tests;

/// <summary>
/// The time left in a timed race (28 Oct 2026, asked for by Asher): when the
/// clock passes a mark (TimeLeftCalls), which marks the announcer says
/// (Minutes left's "only the last N minutes", the last-minute countdown),
/// the words, how the queue treats them, and the two new rows' defaults.
/// </summary>
public sealed class TimeLeftCallsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Start = new(2026, 10, 28, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>Every mark called through a race of <paramref name="limit"/>, the clock read every half second as the window does.</summary>
    private static List<int> CallsThrough(TimeSpan limit)
    {
        var calls = new TimeLeftCalls();
        var said = new List<int>();

        for (var ms = 0L; ms <= (long)limit.TotalMilliseconds + 5_000; ms += 500)
        {
            if (calls.Next(Start, limit, TimeSpan.FromMilliseconds(ms), timedRace: true) is { } mark)
            {
                said.Add(mark);
            }
        }

        return said;
    }

    [Theory]
    [InlineData(299.5, 300)]
    [InlineData(300, 300)]
    [InlineData(240, 240)]
    [InlineData(240.1, 300)]
    [InlineData(61, 120)]
    [InlineData(60, 60)]
    [InlineData(45.5, 60)]
    [InlineData(45, 45)]
    [InlineData(31, 45)]
    [InlineData(16, 30)]
    [InlineData(1, 15)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void The_clock_is_at_the_next_mark_it_has_not_passed(double secondsLeft, int mark) =>
        Assert.Equal(mark, TimeLeftCalls.MarkFor(TimeSpan.FromSeconds(secondsLeft)));

    [Fact]
    public void A_five_minute_race_calls_four_three_and_two_minutes_then_every_fifteen_seconds()
    {
        // Five minutes is not said at the green, and one minute is the
        // session's final minute, so neither is here.
        Assert.Equal(new[] { 240, 180, 120, 45, 30, 15 }, CallsThrough(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void A_race_of_odd_length_calls_its_first_whole_minute()
    {
        Assert.Equal(new[] { 300, 240, 180, 120, 45, 30, 15 }, CallsThrough(TimeSpan.FromSeconds(330)));
    }

    [Fact]
    public void A_race_of_a_minute_or_less_calls_only_the_last_minute_marks()
    {
        Assert.Equal(new[] { 45, 30, 15 }, CallsThrough(TimeSpan.FromSeconds(90)));
        Assert.Equal(new[] { 30, 15 }, CallsThrough(TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void Each_mark_is_called_once()
    {
        var calls = new TimeLeftCalls();
        var limit = TimeSpan.FromMinutes(5);

        Assert.Null(calls.Next(Start, limit, TimeSpan.Zero, timedRace: true));
        Assert.Equal(240, calls.Next(Start, limit, TimeSpan.FromSeconds(60), timedRace: true));
        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(60.5), timedRace: true));
        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(61), timedRace: true));
    }

    [Fact]
    public void A_race_first_seen_part_way_through_calls_nothing_it_has_already_passed()
    {
        // A race restored after a restart, two and a half minutes in: the
        // marks it passed before are not called all at once.
        var calls = new TimeLeftCalls();
        var limit = TimeSpan.FromMinutes(5);

        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(150), timedRace: true));
        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(150.5), timedRace: true));
        Assert.Equal(120, calls.Next(Start, limit, TimeSpan.FromSeconds(180), timedRace: true));
    }

    [Fact]
    public void After_a_freeze_only_the_latest_mark_is_called_and_only_while_it_is_still_true()
    {
        var calls = new TimeLeftCalls();
        var limit = TimeSpan.FromMinutes(5);

        Assert.Null(calls.Next(Start, limit, TimeSpan.Zero, timedRace: true));

        // Frozen from 4:59 to 2:40 left: four and three minutes went by
        // unheard, three twenty seconds ago - too long to still say.
        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(140), timedRace: true));

        // Frozen again to 1:50 left: two minutes was ten seconds ago, near
        // enough to still say.
        Assert.Equal(120, calls.Next(Start, limit, TimeSpan.FromSeconds(190), timedRace: true));

        // Frozen to 0:22 left: thirty seconds was eight seconds ago, too
        // long to still be true, and forty-five is gone with it.
        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(278), timedRace: true));
        Assert.Equal(15, calls.Next(Start, limit, TimeSpan.FromSeconds(285), timedRace: true));
    }

    [Fact]
    public void A_new_race_starts_again_and_nothing_is_called_outside_a_timed_race()
    {
        var calls = new TimeLeftCalls();
        var limit = TimeSpan.FromMinutes(5);

        Assert.Null(calls.Next(Start, limit, TimeSpan.Zero, timedRace: true));
        Assert.Equal(240, calls.Next(Start, limit, TimeSpan.FromSeconds(60), timedRace: true));

        // The next race, from its own green.
        var next = Start.AddMinutes(10);
        Assert.Null(calls.Next(next, limit, TimeSpan.Zero, timedRace: true));
        Assert.Equal(240, calls.Next(next, limit, TimeSpan.FromSeconds(60), timedRace: true));

        // Practice, a race on laps, no race, no time limit.
        Assert.Null(new TimeLeftCalls().Next(Start, limit, TimeSpan.FromSeconds(60), timedRace: false));
        Assert.Null(calls.Next(next, null, TimeSpan.FromSeconds(120), timedRace: true));
        Assert.Null(calls.Next(null, limit, TimeSpan.FromSeconds(120), timedRace: true));
    }

    [Fact]
    public void A_red_flag_stops_the_clock_and_the_calls_with_it()
    {
        var calls = new TimeLeftCalls();
        var limit = TimeSpan.FromMinutes(5);

        Assert.Null(calls.Next(Start, limit, TimeSpan.Zero, timedRace: true));

        // Under the red the window asks with timedRace false, and the race
        // clock has stopped at 1:10 in.
        Assert.Null(calls.Next(Start, limit, TimeSpan.FromSeconds(70), timedRace: false));

        // Green again at 1:10 in: four minutes was ten seconds ago and the
        // clock has not moved on since, so it is still said.
        Assert.Equal(240, calls.Next(Start, limit, TimeSpan.FromSeconds(70), timedRace: true));
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(5, 2, true)]
    [InlineData(5, 6, false)]
    [InlineData(0, 9, true)]
    [InlineData(0, 30, true)]
    [InlineData(3, 1, false)]
    [InlineData(0, 1, false)]
    public void Minutes_left_says_every_minute_or_only_the_last_few(int from, int minutes, bool said) =>
        Assert.Equal(said, new AnnouncementEventConfig { MinutesLeftFrom = from }.SaysMinutesLeft(minutes));

    [Fact]
    public void The_two_new_rows_are_off_by_default_with_the_last_five_minutes_set()
    {
        var defaults = VoiceProfile.CreateDefault();

        var minutes = defaults.Events[nameof(AnnouncementEventType.MinutesLeft)];
        Assert.False(minutes.Enabled);
        Assert.Equal(5, minutes.MinutesLeftFrom);
        Assert.All(minutes.Templates, line => Assert.Contains("{minutes}", line, StringComparison.Ordinal));

        var seconds = defaults.Events[nameof(AnnouncementEventType.FinalSeconds)];
        Assert.False(seconds.Enabled);
        Assert.All(seconds.Templates, line => Assert.Contains("{seconds}", line, StringComparison.Ordinal));

        // A settings file from before this reads as the last five minutes.
        Assert.Equal(5, new AnnouncementEventConfig().MinutesLeftFrom);

        // Weighted with the final minute, so the pacing dial keeps them
        // wherever it keeps that.
        Assert.Equal(defaults.Events[nameof(AnnouncementEventType.FinalMinute)].Weight, minutes.Weight);
        Assert.Equal(defaults.Events[nameof(AnnouncementEventType.FinalMinute)].Weight, seconds.Weight);
    }

    [Fact]
    public void The_new_events_are_added_at_the_end_so_saved_numbers_keep_their_meaning()
    {
        Assert.Equal(AnnouncementEventType.GhostGap + 1, AnnouncementEventType.MinutesLeft);
        Assert.Equal(AnnouncementEventType.MinutesLeft + 1, AnnouncementEventType.FinalSeconds);

        // And every call added since, after them (10 Nov 2026).
        Assert.Equal(AnnouncementEventType.FinalSeconds + 1, AnnouncementEventType.WinnerCalled);
        Assert.Equal(AnnouncementEventType.WinnerCalled + 1, AnnouncementEventType.PersonalBestsTogether);
        Assert.Equal(AnnouncementEventType.PersonalBestsTogether + 1, AnnouncementEventType.BestEverHere);
        Assert.Equal(AnnouncementEventType.BestEverHere, Enum.GetValues<AnnouncementEventType>().Max());
    }

    [Fact]
    public void The_clocks_rows_sit_together_around_the_final_minute()
    {
        var rows = VoiceSettingsControl.AnnouncementTypes().ToList();
        var finalMinute = rows.IndexOf(AnnouncementEventType.FinalMinute);

        Assert.Equal(AnnouncementEventType.MinutesLeft, rows[finalMinute - 1]);
        Assert.Equal(AnnouncementEventType.FinalSeconds, rows[finalMinute + 1]);
        Assert.Equal(Enum.GetValues<AnnouncementEventType>().Length - 1, rows.Count);
        Assert.DoesNotContain(AnnouncementEventType.Reminder, rows);
    }

    [Fact]
    public void The_time_left_waits_its_turn_and_a_newer_one_replaces_an_older()
    {
        Assert.True(AnnouncementService.IsWorthWaitingFor(AnnouncementEventType.MinutesLeft));
        Assert.True(AnnouncementService.IsWorthWaitingFor(AnnouncementEventType.FinalSeconds));

        Assert.True(AnnouncementService.IsTimeLeftCall(AnnouncementEventType.MinutesLeft));
        Assert.True(AnnouncementService.IsTimeLeftCall(AnnouncementEventType.FinalMinute));
        Assert.True(AnnouncementService.IsTimeLeftCall(AnnouncementEventType.FinalSeconds));
        Assert.False(AnnouncementService.IsTimeLeftCall(AnnouncementEventType.LastLap));

        // A flag still clears them, as it clears the final minute.
        Assert.False(AnnouncementService.SurvivesAFlag(AnnouncementEventType.MinutesLeft));
        Assert.False(AnnouncementService.SurvivesAFlag(AnnouncementEventType.FinalSeconds));
    }

    [Fact]
    public void Lap_times_only_leaves_the_time_left_as_the_club_has_it()
    {
        Assert.Contains(AnnouncementEventType.MinutesLeft, LapTimesOnly.LeftAsSet);
        Assert.Contains(AnnouncementEventType.FinalSeconds, LapTimesOnly.LeftAsSet);
    }

    [Fact]
    public void Minutes_left_is_said_in_words_with_numerals_on_screen()
    {
        var (announcer, shown) = Announcer(from: 5);

        announcer.NotifyTimeLeft(240);

        Assert.Equal(new[] { "4 minutes to go." }, Shown(shown));
    }

    [Fact]
    public void Minutes_left_holds_back_minutes_before_the_last_few()
    {
        var (announcer, shown) = Announcer(from: 3);

        announcer.NotifyTimeLeft(300);
        announcer.NotifyTimeLeft(240);

        Assert.Empty(Shown(shown));
    }

    [Fact]
    public void Every_minute_calls_them_all_but_never_the_final_minute_twice()
    {
        var (announcer, shown) = Announcer(from: 0);

        announcer.NotifyTimeLeft(600);
        announcer.NotifyTimeLeft(60);

        Assert.Equal(new[] { "10 minutes to go." }, Shown(shown));
    }

    [Fact]
    public void The_last_minute_is_called_every_fifteen_seconds()
    {
        var (announcer, shown) = Announcer(from: 5);

        announcer.NotifyTimeLeft(45);
        announcer.NotifyTimeLeft(30);
        announcer.NotifyTimeLeft(15);
        announcer.NotifyTimeLeft(10);

        Assert.Equal(new[] { "45 seconds left.", "30 seconds left.", "15 seconds left." }, Shown(shown));
    }

    [Fact]
    public void Either_row_switched_off_is_not_said()
    {
        var (announcer, shown) = Announcer(from: 5, minutesOn: false, secondsOn: false);

        announcer.NotifyTimeLeft(240);
        announcer.NotifyTimeLeft(30);

        Assert.Empty(Shown(shown));
    }

    private static List<string> Shown(List<string> shown)
    {
        lock (shown)
        {
            return shown.ToList();
        }
    }

    /// <summary>An announcer with every line and sound off but the two time-left rows, each with one line.</summary>
    private static (AnnouncementService Announcer, List<string> Shown) Announcer(int from, bool minutesOn = true, bool secondsOn = true)
    {
        var profiles = ProfileStore.Load();

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        voice.Current.Events[nameof(AnnouncementEventType.MinutesLeft)] = new AnnouncementEventConfig
        {
            Enabled = minutesOn,
            Weight = 90,
            MinutesLeftFrom = from,
            Templates = { "{minutes} minutes to go." },
        };

        voice.Current.Events[nameof(AnnouncementEventType.FinalSeconds)] = new AnnouncementEventConfig
        {
            Enabled = secondsOn,
            Weight = 90,
            Templates = { "{seconds} seconds left." },
        };

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);

        var shown = new List<string>();
        announcer.AnnouncementTextReady += text =>
        {
            lock (shown)
            {
                shown.Add(text);
            }
        };

        return (announcer, shown);
    }
}
