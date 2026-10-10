namespace HeatCaller.Tests;

/// <summary>
/// The show's lifetime and the flag's reach (9 Oct bug hunt #5).
///
/// THE SHOW. A round intro's show is opened by ShowTreatment.StartAsync,
/// which takes 0.8 to 3 seconds - it waits out the opener's ring-out - and
/// only then hands back the ShowRun that ends it. The race can leave
/// staging inside that window (a countdown started by hand, a reset, a
/// stop, a red flag) and the watcher asks for the show to be cut while
/// there is nothing yet to cut. One latch could not carry that: the cut
/// spent it, ended nothing, and the live run that landed a moment later
/// was never ended at all - so ShowOnAir stayed counted up, MainForm kept
/// the club's music muted for the rest of the evening, and the bed looped
/// under the countdown, the horn and every heat after it. The tests below
/// run that race, both halves at once, and ask for the one thing that
/// matters: the club gets its music back. On the old shape they fail.
///
/// Nothing here plays anything: AudioBus is silent for the whole test run
/// (TestSetup), and the runs below are built with no bed at all, so
/// FinishAsync does its counting and no audio.
///
/// THE FLAG. A line taken off the voice queue carries the interrupt
/// generation it was taken under, which is how a flag drops a line that is
/// still synthesising or still waiting for the speaker. Every priority
/// line was exempt from that stamp, and spared by the flag's queue purge
/// as well - including Reminder, which is how the post-race recap is
/// queued. Green pressed from Idle (no countdown, so the countdown's own
/// cut never runs) left the recap holding nothing to drop it, and it read
/// the previous race's result over a green race. See
/// AnnouncementService.SurvivesAFlag.
/// </summary>
public sealed class BugHunt5ShowLatchTests : IDisposable
{
    private readonly TempAppData _data = new();

    public BugHunt5ShowLatchTests() => ShowOnAir.ResetForTests();

    public void Dispose()
    {
        // Static, so a test that left a show open would silence the music
        // in every test after it - and in a real app run started by the
        // layout test in the same process.
        ShowOnAir.ResetForTests();
        _data.Dispose();
    }

    /// <summary>
    /// An announcer the runs below can unsubscribe their ducking from.
    /// Nothing asks it to speak.
    /// </summary>
    private static AnnouncementService Announcer() =>
        new(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

    /// <summary>
    /// A show run with no bed: FinishAsync takes the ducking handler off
    /// and gives the club's music back, which is the whole of what these
    /// tests are about, without a sample going anywhere.
    /// </summary>
    private static ShowRun NewRun(AnnouncementService announcer) =>
        new(announcer, _ => { }, new ShowSpeaking(), null, null, Task.CompletedTask, TimeSpan.Zero, 0.22f, null);

    // --- show-1: the cut that arrives before there is anything to cut ---------

    [Fact]
    public async Task A_cut_asked_for_while_the_show_is_still_opening_still_brings_the_music_back()
    {
        var announcer = Announcer();
        var life = new ShowLifetime();

        // The countdown is started by hand while StartAsync is still
        // waiting out the opener. The watcher asks for the cut; no run has
        // been handed over, so there is nothing for it to end.
        await life.EndAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);

        // StartAsync lands: it counted ShowOnAir up on its way in, and the
        // handle that counts it back down arrives here.
        ShowOnAir.Begin();
        Assert.True(ShowOnAir.Playing);

        var cutDuringOpen = life.Opened(NewRun(announcer));

        Assert.True(cutDuringOpen);

        await life.EndAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task The_cut_and_the_open_landing_together_still_end_the_show()
    {
        var announcer = Announcer();

        // Run often: the two halves are on different threads, and the
        // point of the fences in ShowLifetime is that no interleaving of
        // them leaves the show on air.
        for (var attempt = 0; attempt < 500; attempt++)
        {
            ShowOnAir.ResetForTests();

            var life = new ShowLifetime();
            var run = NewRun(announcer);

            // As StartAsync does, before it hands the run over.
            ShowOnAir.Begin();

            var watcher = Task.Run(() => life.EndAsync(cutShort: true));

            var opener = Task.Run(async () =>
            {
                // Exactly what the code after StartAsync does.
                if (life.Opened(run))
                {
                    await life.EndAsync(cutShort: true);
                }
            });

            await Task.WhenAll(watcher, opener);

            Assert.False(ShowOnAir.Playing);
        }
    }

    [Fact]
    public async Task A_show_called_off_before_it_was_opened_is_not_opened_at_all()
    {
        var life = new ShowLifetime();

        await life.EndAsync(cutShort: true);

        // What the intro checks before asking the treatment to open
        // anything: the race has already moved on, so there is no show to
        // put under it.
        Assert.True(life.CutAlreadyWanted);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task The_natural_end_of_an_intro_ends_the_show_once_and_the_guarantee_after_it_does_nothing()
    {
        var announcer = Announcer();
        var life = new ShowLifetime();

        ShowOnAir.Begin();
        Assert.False(life.Opened(NewRun(announcer)));

        // Nobody cut it: the intro was read, the quiet came, and the bed
        // ends on the piece's own fade.
        await life.EndAsync(cutShort: false);
        Assert.False(ShowOnAir.Playing);

        // And the belt-and-braces end on the way out, which must not count
        // the music down a second time.
        ShowOnAir.Begin();
        await life.EndAsync(cutShort: true);
        Assert.True(ShowOnAir.Playing);
    }

    // --- voice-1: what a flag may pre-empt -----------------------------------

    [Fact]
    public void A_flag_no_longer_spares_the_reminder_that_carries_the_recap()
    {
        // The 90 seconds of the last race's result that used to speak over
        // a green race. Still priority - chatter cannot drop it and it
        // speaks under a caution - but no longer immune to a flag.
        Assert.False(AnnouncementService.SurvivesAFlag(AnnouncementEventType.Reminder));
    }

    [Fact]
    public void A_flag_still_spares_the_race_control_lines_it_always_did()
    {
        AnnouncementEventType[] spared =
        {
            AnnouncementEventType.GridCall,
            AnnouncementEventType.RaceStaging,
            AnnouncementEventType.CountdownStarted,
            AnnouncementEventType.DriverRelease,
            AnnouncementEventType.AchievementUnlocked,
            AnnouncementEventType.RaceStarted,
            AnnouncementEventType.YellowFlag,
            AnnouncementEventType.RedFlag,
            AnnouncementEventType.GreenFlagResumed,
            AnnouncementEventType.RaceTimeOver,
            AnnouncementEventType.RaceCompleted,
        };

        foreach (var type in spared)
        {
            Assert.True(AnnouncementService.SurvivesAFlag(type), $"{type} must still survive a flag");
        }
    }

    [Fact]
    public void Chatter_and_race_moments_are_cleared_by_a_flag_as_they_always_were()
    {
        AnnouncementEventType[] cleared =
        {
            AnnouncementEventType.FastLap,
            AnnouncementEventType.LapTime,
            AnnouncementEventType.LeaderChange,
            AnnouncementEventType.CloseBattle,
            AnnouncementEventType.LastLap,
            AnnouncementEventType.FinalMinute,
            AnnouncementEventType.TrackRecord,
            AnnouncementEventType.PositionReport,
        };

        foreach (var type in cleared)
        {
            Assert.False(AnnouncementService.SurvivesAFlag(type), $"{type} must not survive a flag");
        }
    }
}
