using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Known issues, 22 Oct 2026 - the voice and the announcer: a held battle
/// waiting out the pacing gap, "presented by" counts kept across a restart,
/// timing from the first read after a speaker dropout, a driver's own best
/// before tonight, and names shortened for a voice off this PC.
/// </summary>
public sealed class KnownIssues1Tests
{
    private static readonly DateTime Tonight = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Local);

    // --- A battle held by the pacing dial waits out the gap ---------------------------------

    [Fact]
    public void A_held_battle_waits_out_the_pacing_gap_before_it_is_offered_again()
    {
        Assert.Equal(TimeSpan.FromSeconds(8), AnnouncementService.BattleCooldown(8));
    }

    [Fact]
    public void A_held_battle_waits_only_what_is_left_of_the_gap()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), AnnouncementService.BattleCooldown(20, 5));

        // A second from the end of the gap: the offer window, not a whole gap more.
        Assert.Equal(TimeSpan.FromSeconds(3), AnnouncementService.BattleCooldown(8, 7));
    }

    [Fact]
    public void A_held_battle_never_waits_less_than_the_offer_window_or_more_than_a_minute()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), AnnouncementService.BattleCooldown(0));
        Assert.Equal(TimeSpan.FromSeconds(3), AnnouncementService.BattleCooldown(double.NaN));
        Assert.Equal(TimeSpan.FromSeconds(60), AnnouncementService.BattleCooldown(400));
    }

    // --- "Presented by" kept across a restart ------------------------------------------------

    [Fact]
    public void Tonights_presented_by_counts_come_back_after_a_restart()
    {
        var now = Tonight;
        var before = new SponsoredMomentGate(() => now);
        string? saved = null;
        before.Changed = json => saved = json;

        before.Mentioned(SponsoredMoment.FastestLap, "joes");
        before.Mentioned(SponsoredMoment.FastestLap, "joes");
        before.Mentioned(SponsoredMoment.RaceResult, "joes");

        Assert.NotNull(saved);

        now = now.AddHours(1);
        var after = new SponsoredMomentGate(() => now);
        after.Restore(saved);

        var counts = after.MentionsTonight()["joes"];
        Assert.Equal(2, counts[SponsoredMoment.FastestLap]);
        Assert.Equal(1, counts[SponsoredMoment.RaceResult]);
    }

    [Fact]
    public void Last_weeks_presented_by_counts_never_come_back()
    {
        var now = Tonight.AddDays(-7);
        var lastWeek = new SponsoredMomentGate(() => now);
        lastWeek.Mentioned(SponsoredMoment.LeadChange, "joes");
        var saved = lastWeek.Save();

        now = Tonight;
        var tonight = new SponsoredMomentGate(() => now);
        tonight.Restore(saved);

        Assert.Empty(tonight.MentionsTonight());
    }

    [Fact]
    public void Laying_the_count_back_twice_does_not_double_it()
    {
        var now = Tonight;
        var gate = new SponsoredMomentGate(() => now);
        gate.Mentioned(SponsoredMoment.LeadChange, "joes");
        var saved = gate.Save();

        gate.Restore(saved);
        gate.Restore(saved);

        Assert.Equal(1, gate.MentionsTonight()["joes"][SponsoredMoment.LeadChange]);
    }

    [Fact]
    public void A_count_that_will_not_read_is_passed_over()
    {
        var gate = new SponsoredMomentGate(() => Tonight);

        gate.Restore("{not json");
        gate.Restore(string.Empty);

        Assert.Empty(gate.MentionsTonight());
    }

    // --- Timing from the first read ----------------------------------------------------------

    private sealed class Silence : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(22050, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    [Fact]
    public void Nothing_is_heard_until_the_output_first_reads()
    {
        var heardAt = 0;
        var provider = new FirstReadSampleProvider(new Silence(), () => heardAt++);

        Assert.False(provider.FirstRead.IsCompleted);

        provider.Read(new float[64], 0, 64);
        provider.Read(new float[64], 0, 64);

        Assert.True(provider.FirstRead.IsCompleted);
        Assert.Equal(1, heardAt);
    }

    [Fact]
    public async Task The_post_is_timed_from_the_first_read_not_from_the_queue()
    {
        var provider = new FirstReadSampleProvider(new Silence());
        var played = new TaskCompletionSource();
        var post = FirstReadSampleProvider.AfterFirstRead(provider.FirstRead, TimeSpan.FromMilliseconds(50), played.Task);

        // The device is still reopening: well past the post, and not there yet.
        await Task.Delay(200);
        Assert.False(post.IsCompleted);

        provider.Read(new float[64], 0, 64);
        await post.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_sound_that_never_reaches_the_device_still_lets_the_next_thing_go()
    {
        var provider = new FirstReadSampleProvider(new Silence());
        var played = new TaskCompletionSource();
        var post = FirstReadSampleProvider.AfterFirstRead(provider.FirstRead, TimeSpan.FromSeconds(30), played.Task);

        played.SetResult();

        await post.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // --- A driver's own best before tonight ----------------------------------------------------

    private static SessionRecord Race(DateTime startedLocal, string layout, bool demo = false, params (string Transponder, long FastMs)[] results)
    {
        var record = new SessionRecord
        {
            TrackLayoutName = layout,
            IsDemo = demo,
            StartedAtUtc = startedLocal.ToUniversalTime(),
        };

        foreach (var (transponder, fast) in results)
        {
            record.Results.Add(new SessionResultEntry { Transponder = transponder, DriverName = transponder, FastLapMs = fast });
        }

        return record;
    }

    [Fact]
    public void A_drivers_best_before_tonight_is_their_quickest_earlier_lap_on_this_layout()
    {
        var races = new[]
        {
            Race(Tonight.AddDays(-14), "Infield", false, ("101", 12_400)),
            Race(Tonight.AddDays(-7), "Infield", false, ("101", 12_100), ("102", 13_000)),
            Race(Tonight.AddDays(-7), "Oval", false, ("101", 9_000)),
        };

        var bests = PaceHistory.PersonalBestsBefore(races, "Infield", TonightAtTheTrack.NightOf(Tonight));

        Assert.Equal(12.1, bests["101"], 3);
        Assert.Equal(13.0, bests["102"], 3);
    }

    [Fact]
    public void Ghosts_and_demos_are_not_a_best_before_this_race()
    {
        // 22 Oct 2026: tonight's saved heats now count (bug hunt 9, n=8), so the
        // 11.0 s heat an hour ago is 101's best; the demo and the ghost still are not.
        var races = new[]
        {
            Race(Tonight.AddHours(-1), "Infield", false, ("101", 11_000)),
            Race(Tonight.AddDays(-7), "Infield", true, ("102", 10_000)),
            Race(Tonight.AddDays(-7), "Infield", false, (Ghosts.Prefix + "1", 9_000)),
        };

        var bests = PaceHistory.PersonalBestsBefore(races, "Infield", TonightAtTheTrack.NightOf(Tonight));

        Assert.Single(bests);
        Assert.Equal(11.0, bests["101"], 3);
    }

    [Fact]
    public void No_layout_means_no_best_is_claimed()
    {
        var races = new[] { Race(Tonight.AddDays(-7), "Infield", false, ("101", 12_000)) };

        Assert.Empty(PaceHistory.PersonalBestsBefore(races, " ", TonightAtTheTrack.NightOf(Tonight)));
        Assert.Empty(PaceHistory.PersonalBestsBefore(null, "Infield", TonightAtTheTrack.NightOf(Tonight)));
    }

    // --- Names shortened for a voice off this PC -----------------------------------------------

    [Fact]
    public void A_full_name_goes_to_a_cloud_voice_as_the_pages_show_it()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Billy Smith", "Sam Jones", "Ace" });

        Assert.Equal(
            "Billy S takes the lead from Sam J, and Ace is third!",
            AnnouncementService.ShortenNames("Billy Smith takes the lead from Sam Jones, and Ace is third!", map));
    }

    [Fact]
    public void Two_drivers_who_would_share_a_short_name_are_still_told_apart()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Sam Jones", "Sam Jordan" });

        Assert.NotEqual(map["Sam Jones"], map["Sam Jordan"]);
    }

    [Fact]
    public void Only_whole_names_are_shortened_and_the_longest_first()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Sam Smith", "Sam Smith-Jones" });

        var said = AnnouncementService.ShortenNames("Sam Smith-Jones passes Sam Smith. Sam Smithers watches.", map);

        Assert.DoesNotContain("-Jones", said);
        Assert.Contains("Sam Smithers", said);
    }
}
