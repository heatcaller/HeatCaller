using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// A penalty, or a lap added or taken away by hand (8 Nov 2026). Two faults
/// from one review, both about trusting what the announcer says:
///
///   the gaps it gave disagreed with the board after a correction - it kept
///   its own book of crossings, and moved only the current lap's for a
///   penalty, and gave a hand-added lap a crossing the board never has;
///
///   a correction went through the lead change, lapped, battle and rivalry
///   calls like a crossing, so a 10 s penalty could make the room hear
///   "Sam takes the lead" for a pass nobody made.
///
/// See AnnouncementService.LapCrossingBook and KeepInStepWithCorrection.
/// </summary>
public sealed class HandCorrectionCallsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// A car as the board and the announcer are each given it: equal lap
    /// times making up the race time less the penalty, one per lap except
    /// any added by hand, which have none.
    /// </summary>
    private static (RaceStanding Announced, RacerStanding Board) Car(
        string transponder, int laps, long elapsedMs, long penaltyMs = 0, int handAddedLaps = 0, bool adjusted = false)
    {
        var timed = laps - handAddedLaps;
        var lapTimes = new List<long>();
        var driven = elapsedMs - penaltyMs;

        for (var i = 0; i < timed; i++)
        {
            lapTimes.Add(i < timed - 1 ? driven / timed : driven - (driven / timed * (timed - 1)));
        }

        var board = new RacerStanding { TransponderId = transponder, Laps = laps, ElapsedMs = elapsedMs, PenaltyMs = penaltyMs };
        board.LapTimesMs.AddRange(lapTimes);

        var announced = new RaceStanding
        {
            Transponder = transponder,
            Id = transponder,
            Laps = laps,
            ElapsedMs = elapsedMs,
            PenaltyMs = penaltyMs,
            Status = adjusted ? "adjusted" : "active",
            LapTimesMs = lapTimes.ToArray(),
        };

        return (announced, board);
    }

    /// <summary>An announcer with every call and sound off but those asked for, Sam (101) and Bill (102) on file.</summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer(
        string? leaderChange = null, string? lapped = null)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        if (leaderChange is not null)
        {
            voice.Current.Events[nameof(AnnouncementEventType.LeaderChange)] = new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { leaderChange } };
        }

        if (lapped is not null)
        {
            voice.Current.Events[nameof(AnnouncementEventType.Lapped)] = new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { lapped } };
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);
        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        return (announcer, shown, log);
    }

    /// <summary>Long past the per-lap batch window, for a call that should NOT come.</summary>
    private static void LetTheBatchesSettle() => Thread.Sleep(900);

    private static void UntilShown(ConcurrentQueue<string> shown, string words)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !shown.Any(text => text.Contains(words, StringComparison.Ordinal)))
        {
            Thread.Sleep(50);
        }
    }

    // --- The gap said is the board's gap ----------------------------------

    [Fact]
    public void After_a_penalty_the_spoken_gap_is_the_boards()
    {
        var (announcer, _, _) = Announcer();

        // Bill over lap 10 at 60.0, then given 5 s. Sam over lap 9 at 56.0.
        // Bill's own lap 9 was 54.0 - 59.0 with the penalty, as the board
        // has it - so Sam is not two seconds back of anything.
        var billLap10 = Car("102", 10, 60_000);
        var billPenalised = Car("102", 10, 65_000, penaltyMs: 5_000, adjusted: true);
        var samLap9 = Car("101", 9, 56_000);

        announcer.OnStandingUpdated(billLap10.Announced);
        announcer.OnStandingUpdated(billPenalised.Announced);
        announcer.OnStandingUpdated(samLap9.Announced);
        announcer.Muted = true;

        var board = RaceGaps.Between(samLap9.Board, billPenalised.Board);

        Assert.Equal(board, announcer.GapAsSpoken("101", "102"));
        Assert.Equal("-", RaceGaps.Format(board));
    }

    [Fact]
    public void After_a_lap_added_by_hand_the_spoken_gap_is_the_boards()
    {
        var (announcer, _, _) = Announcer();

        // Sam over lap 9 at 53.0, Bill over lap 9 at 54.0 - and then a lap
        // given to Bill by hand. That lap has no crossing on the board, which
        // lines Bill's history up with the END of his lap count: the voice
        // used to give it one at 54.0 and measure Sam against that.
        var samLap9 = Car("101", 9, 53_000);
        var billLap9 = Car("102", 9, 54_000);
        var billPlusOne = Car("102", 10, 54_000, handAddedLaps: 1, adjusted: true);

        announcer.OnStandingUpdated(samLap9.Announced);
        announcer.OnStandingUpdated(billLap9.Announced);
        announcer.OnStandingUpdated(billPlusOne.Announced);
        announcer.Muted = true;

        var board = RaceGaps.Between(samLap9.Board, billPlusOne.Board);

        Assert.Equal(board, announcer.GapAsSpoken("101", "102"));
        Assert.Equal("+5.000", RaceGaps.Format(board));
    }

    [Fact]
    public void Between_crossings_the_spoken_gap_is_the_boards_too()
    {
        var (announcer, _, _) = Announcer();

        var billLap5 = Car("102", 5, 50_000);
        var samLap5 = Car("101", 5, 50_700);

        announcer.OnStandingUpdated(billLap5.Announced);
        announcer.OnStandingUpdated(samLap5.Announced);
        announcer.Muted = true;

        Assert.Equal(RaceGaps.Between(samLap5.Board, billLap5.Board), announcer.GapAsSpoken("101", "102"));
        Assert.Equal(new RaceGap(0, 700), announcer.GapAsSpoken("101", "102"));
    }

    // --- A correction makes no call ----------------------------------------

    [Fact]
    public void A_penalty_that_changes_the_lead_is_not_called_and_nor_is_the_next_crossing()
    {
        var (announcer, shown, _) = Announcer(leaderChange: "{driver} now leads");

        // Bill leads Sam on lap 5.
        announcer.OnStandingUpdated(Car("102", 5, 50_000).Announced);
        announcer.OnStandingUpdated(Car("101", 5, 50_400).Announced);
        var leadsBefore = announcer.RaceFactsNow().LeadChanges;

        // Ten seconds for Bill: Sam leads on the board, but passed nobody.
        announcer.OnStandingUpdated(Car("102", 5, 60_000, penaltyMs: 10_000, adjusted: true).Announced);
        LetTheBatchesSettle();

        Assert.Empty(shown);

        // An AI line about Bill leading is untrue now all the same.
        Assert.Equal(leadsBefore + 1, announcer.RaceFactsNow().LeadChanges);

        // Not counted as a lead change on the tower, or in the next call's count.
        Assert.Equal(0, announcer.Leads.Changes);
        Assert.Equal("101", announcer.Leads.Leader);

        // Sam's next real crossing, still in front: nothing to call either.
        announcer.OnStandingUpdated(Car("101", 6, 60_500).Announced);
        LetTheBatchesSettle();

        Assert.Empty(shown);

        // The control: a real pass is still called. Bill over lap 7 first.
        announcer.OnStandingUpdated(Car("102", 7, 71_000, penaltyMs: 10_000).Announced);
        UntilShown(shown, "now leads");
        announcer.Muted = true;

        Assert.Single(shown, text => text.StartsWith("Bill now leads", StringComparison.Ordinal));
        Assert.Equal(1, announcer.Leads.Changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_penalty_that_puts_a_car_a_lap_down_is_not_called_as_a_lapping(bool penalty)
    {
        var (announcer, shown, _) = Announcer(lapped: "{driver} laps {rival}");

        // Bill over lap 10 at 100.0, Sam over lap 9 at 95.0 - on the lead lap.
        announcer.OnStandingUpdated(Car("102", 10, 100_000).Announced);
        announcer.OnStandingUpdated(Car("101", 9, 95_000).Announced);

        if (penalty)
        {
            // Ten seconds for Sam: behind Bill's lap-10 crossing on the board.
            announcer.OnStandingUpdated(Car("101", 9, 105_000, penaltyMs: 10_000, adjusted: true).Announced);
            LetTheBatchesSettle();

            Assert.Empty(shown);
        }

        // Bill's next real crossing. With the penalty Sam was already a lap
        // down - kept in step, not called a lap late. Without it, Bill has
        // crossed twice since Sam did: a real lapping, called.
        announcer.OnStandingUpdated(Car("102", 11, 110_000).Announced);

        if (penalty)
        {
            LetTheBatchesSettle();
        }
        else
        {
            UntilShown(shown, "laps");
        }

        announcer.Muted = true;

        if (penalty)
        {
            Assert.Empty(shown);
        }
        else
        {
            Assert.Equal(new[] { "Bill laps Sam" }, shown.ToList());
        }
    }

    [Fact]
    public void A_penalty_that_swaps_a_battle_lines_cars_cuts_it_off_once_and_calls_the_ruling()
    {
        var (announcer, shown, log) = Announcer();
        var samChasingBill = new BattlePair(AheadId: "102", BehindId: "101", Position: 1);

        announcer.OnStandingUpdated(Car("102", 4, 40_000).Announced);
        announcer.OnStandingUpdated(Car("101", 4, 40_400).Announced);
        announcer.PutBattleLineOnAir(samChasingBill);

        // Bill penalised behind Sam - and then Sam's next real crossing.
        announcer.OnStandingUpdated(Car("102", 4, 50_000, penaltyMs: 10_000, adjusted: true).Announced);
        announcer.OnStandingUpdated(Car("101", 5, 50_400).Announced);
        announcer.Muted = true;

        // Cut off by the correction (8 Nov 2026: it used to be let finish),
        // once, and not again on the crossing after.
        Assert.Single(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal)
            && line.Contains("a correction by hand", StringComparison.Ordinal));

        // And the call, with its spice - worded as the ruling it was, once
        // (Asher, 8 Nov 2026: "add a takes the lead call. it's a battle").
        Assert.Equal(new[] { "And with that ruling, Sam takes the lead!" }, shown.ToList());
    }

    [Theory]
    // Words began 200 ms ago: wait until 1.8 s of them have been heard.
    [InlineData(10_000, 10_200, 1_600)]
    // Lead-in silence still playing (words start in 0.5 s): 0.5 + 1.8.
    [InlineData(10_500, 10_000, 2_300)]
    // Three seconds in already: still a beat before the cut.
    [InlineData(10_000, 13_000, 500)]
    public void A_cut_off_line_is_heard_for_a_beat_first(long wordsAtMs, long nowMs, long expectedDelayMs)
    {
        // Asher, 8 Nov 2026: "the first call would stop playing so quick you
        // don't know what was about to be said. it should wait a beat".
        Assert.Equal(expectedDelayMs, AnnouncementService.CutDelayMs(wordsAtMs, nowMs));
    }

    [Fact]
    public void Battle_lines_are_cut_after_a_beat_and_flags_still_cut_at_once()
    {
        var battles = File.ReadAllText(Path.Combine(RepoRootForSource(), "HeatCaller", "HeatCaller", "AnnouncementService.BattleLines.cs"));
        var flags = File.ReadAllText(Path.Combine(RepoRootForSource(), "HeatCaller", "HeatCaller", "AnnouncementService.RaceEvents.cs"));

        Assert.DoesNotContain("StopCurrentPlayback()", battles, StringComparison.Ordinal);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(battles, @"StopCurrentPlaybackAfterABeat\(\)").Count);
        Assert.Contains("StopCurrentPlayback();", flags, StringComparison.Ordinal);
    }

    private static string RepoRootForSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find check.cmd.");
    }

    [Fact]
    public void A_ruling_for_another_place_says_the_place()
    {
        Assert.Equal("[excited] And with that ruling, Sam is up into third!", AnnouncementService.RulingPassWords("Sam", 3));
        Assert.Equal("[excited] And with that ruling, Sam takes the lead!", AnnouncementService.RulingPassWords("Sam", 1));
    }

    [Fact]
    public void A_lap_taken_away_by_hand_with_no_lap_times_loses_its_crossing()
    {
        var (announcer, _, _) = Announcer();

        // A source with no lap history keeps the old book - but a correction
        // still gives no crossing, and takes away the one for a lap removed.
        RaceStanding Bare(string transponder, int laps, long elapsedMs, bool adjusted = false) => new()
        {
            Transponder = transponder,
            Id = transponder,
            Laps = laps,
            ElapsedMs = elapsedMs,
            Status = adjusted ? "adjusted" : "active",
        };

        // Bill over lap 6 at 60.0; the lap taken away, then given back by
        // hand; then over lap 7 for real. Sam over lap 6 at 65.0.
        announcer.OnStandingUpdated(Bare("102", 5, 50_000));
        announcer.OnStandingUpdated(Bare("102", 6, 60_000));
        announcer.OnStandingUpdated(Bare("102", 5, 60_000, adjusted: true));
        announcer.OnStandingUpdated(Bare("102", 6, 60_000, adjusted: true));
        announcer.OnStandingUpdated(Bare("102", 7, 70_000));
        announcer.OnStandingUpdated(Bare("101", 6, 65_000));
        announcer.Muted = true;

        // Bill's lap 6 has no crossing now, so there is nothing to measure
        // Sam against - not "five seconds back" off the lap that was removed.
        Assert.Equal(new RaceGap(0, null), announcer.GapAsSpoken("101", "102"));
    }
}
