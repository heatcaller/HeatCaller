namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the announcer, fourth group: a black-flagged
/// car still held provisional pole; a close battle was called a second time
/// once its two cars swapped; and lines that will now never be said - a
/// beaten fastest lap, lines cleared for an instruction, lines binned by a
/// mute or a stop, and a prepared recap or teaser dropped at the speaker -
/// stayed on the Big Screen and in the feed as said. The voice can't speak
/// in a test, so the speaker-side paths are checked in the source, as
/// NeverToBeHeardTests does.
/// </summary>
public sealed class BugHunt24_AnnouncementService_4Tests
{
    private static RaceStanding Car(string transponder, int laps, long elapsedMs, bool blackFlagged = false) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
        Id = transponder,
        BlackFlagged = blackFlagged,
    };

    // ------------------------------------------------ provisional pole

    [Fact]
    public void A_black_flagged_car_with_the_quickest_run_is_not_on_provisional_pole()
    {
        var field = new[] { Car("101", 3, 30_000, blackFlagged: true), Car("102", 3, 31_000) };
        var laps = new Dictionary<string, List<long>>
        {
            ["101"] = new() { 9_000, 9_000, 9_000 },
            ["102"] = new() { 10_000, 10_000, 10_000 },
        };

        Assert.Equal("102", AnnouncementService.QualifyingLeader(field, laps, QualifyingMethod.BestConsecutiveLaps, 3));
    }

    [Fact]
    public void A_black_flagged_car_ahead_on_laps_is_not_on_provisional_pole()
    {
        var field = new[] { Car("101", 10, 60_000, blackFlagged: true), Car("102", 9, 59_000), Car("103", 8, 58_000) };

        Assert.Equal("102", AnnouncementService.QualifyingLeader(field, new Dictionary<string, List<long>>(), QualifyingMethod.TotalLaps, 3));
    }

    [Fact]
    public void A_field_of_only_black_flagged_cars_has_nobody_on_pole()
    {
        var field = new[] { Car("101", 10, 60_000, blackFlagged: true) };

        Assert.Null(AnnouncementService.QualifyingLeader(field, new Dictionary<string, List<long>>(), QualifyingMethod.TotalLaps, 3));
    }

    // ------------------------------------------------ close battle once per pair

    [Fact]
    public void A_close_battle_pair_has_one_mark_whichever_car_is_in_front()
    {
        Assert.Equal(AnnouncementService.BattlePairKey("101", "102"), AnnouncementService.BattlePairKey("102", "101"));
        Assert.Equal(AnnouncementService.BattlePairKey("abc", "XYZ"), AnnouncementService.BattlePairKey("XYZ", "abc"));
        Assert.NotEqual(AnnouncementService.BattlePairKey("101", "102"), AnnouncementService.BattlePairKey("101", "103"));
    }

    [Fact]
    public void The_close_battle_check_keys_its_pairs_by_the_shared_mark()
    {
        var laps = Source("AnnouncementService.LapCalls.cs");

        Assert.Contains("var key = BattlePairKey(front.Transponder, chaser.Transponder);", laps, StringComparison.Ordinal);
        Assert.DoesNotContain("var key = $\"{front.Transponder}|{chaser.Transponder}\";", laps, StringComparison.Ordinal);
    }

    // ------------------------------------------------ never-said lines come off the screens

    [Fact]
    public void A_waiting_fastest_lap_binned_by_a_quicker_lap_is_taken_off_the_screens()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var body = Between(queue, "private void BinWaitingFastLaps()", "GiveBackLost(giveBack);");

        Assert.Contains("BinQueuedLocked(item => item.Type == AnnouncementEventType.FastLap, ref giveBack, withdrawText: true);", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_cleared_to_make_way_for_an_instruction_are_taken_off_the_screens()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("&& !(type == AnnouncementEventType.Reminder && IsLapBoundCall(item.Type)), ref giveBack, withdrawText: true);", queue, StringComparison.Ordinal);

        // A line replaced by a newer one of the same kind still is not.
        Assert.Contains("BinQueuedLocked(item => item.Type == type && item.Transponder == transponder, ref giveBack);", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mute_or_a_stop_takes_the_queued_lines_it_bins_off_the_screens()
    {
        var body = Between(Source("AnnouncementService.cs"), "public void StopAllPendingAudio(bool keepPriority = true)", "GiveBackLost(giveBack);");

        Assert.Contains("BinQueuedLocked(item => !IsPriorityAnnouncement(item.Type), ref giveBack, withdrawText: true)", body, StringComparison.Ordinal);
        Assert.Contains("BinQueuedLocked(_ => true, ref giveBack, withdrawText: true)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prepared_line_dropped_at_the_speaker_comes_off_the_screens()
    {
        var body = Between(Source("AnnouncementService.cs"), "public void SpeakPrepared(", "displayText: prepared.DisplayText");

        foreach (var drop in new[]
        {
            "couldn't be said again",
            "a flag call interrupted while it waited for the speaker",
            "the countdown started while it waited for the speaker",
            "swapped while it waited for the speaker",
            "the announcer was muted",
        })
        {
            var at = body.IndexOf(drop, StringComparison.Ordinal);
            Assert.True(at >= 0, $"no drop saying \"{drop}\"");

            var returns = body.IndexOf("return;", at, StringComparison.Ordinal);
            var withdraws = body.IndexOf("WithdrawAnnouncementText(prepared.DisplayText);", at, StringComparison.Ordinal);
            Assert.True(withdraws >= 0 && withdraws < returns, $"the drop saying \"{drop}\" leaves its words on the screens");
        }
    }

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no \"{from}\"");

        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"no \"{to}\" after \"{from}\"");

        return text[start..end];
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
