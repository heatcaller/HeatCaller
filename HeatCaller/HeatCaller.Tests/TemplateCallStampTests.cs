namespace HeatCaller.Tests;

/// <summary>
/// Template calls carry a race-facts stamp too (21 Nov 2026), so they are
/// held to the race the way an AI line is - at every checkpoint and on every
/// sweep - with a profile that drops a call only for what makes it untrue.
/// See AnnouncementService.TemplateFactsFor.
/// </summary>
public sealed class TemplateCallStampTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static AnnouncementService Announcer() =>
        new(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load()) { Muted = true };

    private static RaceFactsStamp Template(RaceFactsStamp now, bool lead = false, bool lapTimes = false) =>
        now with { SayByMs = null, LeaderLapsAllowed = null, LeadMatters = lead, LapTimesMatter = lapTimes, FromTemplate = true };

    private static RaceFactsStamp Race(long race = 1, long fastest = 3, long leads = 1, int leaderLaps = 5, long takenOff = 0) =>
        new(race, fastest, leads, leaderLaps, 100_000, LapsTakenOff: takenOff);

    // --- Which calls get a stamp, with which profile -----------------------------

    [Fact]
    public void A_lap_call_gets_a_template_stamp_that_tolerates_the_rest_of_the_race()
    {
        var stamp = Announcer().TemplateFactsFor(AnnouncementEventType.LapTime);

        Assert.NotNull(stamp);
        Assert.True(stamp!.FromTemplate);
        Assert.False(stamp.LeadMatters);
        Assert.False(stamp.LapTimesMatter);
        Assert.Null(stamp.LeaderLapsAllowed);
        Assert.Null(stamp.SayByMs);
    }

    [Fact]
    public void A_fastest_lap_call_is_also_made_untrue_by_a_quicker_lap()
    {
        var stamp = Announcer().TemplateFactsFor(AnnouncementEventType.FastLap)!;

        Assert.True(stamp.LapTimesMatter);
        Assert.False(stamp.LeadMatters);
    }

    [Fact]
    public void A_lead_change_and_a_gap_to_the_leader_are_made_untrue_by_a_change_of_leader()
    {
        var announcer = Announcer();

        Assert.True(announcer.TemplateFactsFor(AnnouncementEventType.LeaderChange)!.LeadMatters);
        Assert.True(announcer.TemplateFactsFor(AnnouncementEventType.FinalLapsGap)!.LeadMatters);
    }

    [Fact]
    public void A_clock_call_is_stamped_too()
    {
        Assert.NotNull(Announcer().TemplateFactsFor(AnnouncementEventType.FinalMinute));
    }

    [Fact]
    public void An_instruction_or_a_flag_call_carries_no_stamp()
    {
        var announcer = Announcer();

        Assert.Null(announcer.TemplateFactsFor(AnnouncementEventType.DriverRelease));
        Assert.Null(announcer.TemplateFactsFor(AnnouncementEventType.RaceStaging));
    }

    // --- What makes a template call untrue ----------------------------------------

    [Fact]
    public void A_new_race_makes_any_template_call_untrue()
    {
        Assert.Equal("the race it was about is over",
            AnnouncementService.WhyNoLongerTrue(Template(Race()), Race(race: 2)));
    }

    [Fact]
    public void A_lap_call_survives_the_leader_lapping_on_a_lead_change_and_a_quicker_lap_elsewhere()
    {
        var then = Template(Race());

        Assert.Null(AnnouncementService.WhyNoLongerTrue(then, Race(leaderLaps: 9)));
        Assert.Null(AnnouncementService.WhyNoLongerTrue(then, Race(leads: 2)));
        Assert.Null(AnnouncementService.WhyNoLongerTrue(then, Race(fastest: 4)));
    }

    [Fact]
    public void A_template_call_survives_the_leader_having_fewer_laps_than_before()
    {
        // Practice and qualifying order by best lap: the car on top can have
        // fewer laps than the last one. An AI line about the front still goes.
        Assert.Null(AnnouncementService.WhyNoLongerTrue(Template(Race(leaderLaps: 7)), Race(leaderLaps: 4)));
        Assert.NotNull(AnnouncementService.WhyNoLongerTrue(Race(leaderLaps: 7), Race(leaderLaps: 4)));
    }

    [Fact]
    public void A_fastest_lap_call_goes_when_a_quicker_lap_is_set_or_a_lap_is_taken_off()
    {
        var then = Template(Race(), lapTimes: true);

        Assert.Equal("a quicker lap has been set since it was written",
            AnnouncementService.WhyNoLongerTrue(then, Race(fastest: 4)));
        Assert.Equal("a lap has been taken off by hand since it was written",
            AnnouncementService.WhyNoLongerTrue(then, Race(takenOff: 1)));
    }

    [Fact]
    public void A_lead_change_call_goes_when_the_lead_changes_again()
    {
        Assert.Equal("the lead has changed hands since it was written",
            AnnouncementService.WhyNoLongerTrue(Template(Race(), lead: true), Race(leads: 2)));
    }

    // --- An AI line keeps what only an AI line does -------------------------------

    [Fact]
    public void Only_an_AI_line_waits_behind_the_lap_calls_or_spares_the_routine_calls()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("facts ??= TemplateFactsFor(type);", queue, StringComparison.Ordinal);
        Assert.Contains(".Select(waiting => waiting.Facts is { FromTemplate: false } || waiting.Type == AnnouncementEventType.Reminder)", queue, StringComparison.Ordinal);
        Assert.Contains("if (isPriority && facts is not { FromTemplate: false })", queue, StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }
}
