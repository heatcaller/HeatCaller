namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 14 (7 Nov 2026): the after-the-flag sequence and the achievements
/// read. A recap written but never heard stayed on offer as the last race
/// summary (R1); the unofficial result at the flag named a DQ'd or no-lap car
/// (R2); the AI's hand back after the achievements reacted to a read that was
/// never said (H2); and the AI read's check missed first names (H1), a capped
/// count (H3) and numbers or club-first claims said in words (H4).
/// </summary>
public sealed class BugHunt14_AfterRaceTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    // --- R1: a recap never heard is forgotten ---------------------------------------------

    [Fact]
    public async Task A_recap_written_but_never_queued_is_forgotten_once_written()
    {
        var forgotten = new List<string?>();
        var writing = new TaskCompletionSource<string?>();

        var continuation = RaceControlControl.ForgetRecapUnlessQueued(writing.Task, queued: false, forgotten.Add);

        Assert.NotNull(continuation);
        Assert.Empty(forgotten);

        writing.SetResult("The recap nobody heard.");
        await continuation!;

        Assert.Equal(new[] { "The recap nobody heard." }, forgotten);
    }

    [Fact]
    public void A_recap_that_was_queued_is_never_forgotten()
    {
        var forgotten = new List<string?>();

        Assert.Null(RaceControlControl.ForgetRecapUnlessQueued(Task.FromResult<string?>("Heard."), queued: true, forgotten.Add));
        Assert.Null(RaceControlControl.ForgetRecapUnlessQueued(null, queued: false, forgotten.Add));
        Assert.Empty(forgotten);
    }

    [Fact]
    public void The_no_recap_way_out_and_the_finally_both_forget_an_unheard_recap()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        // The "no recap" branch: forgets, and says "spoken", not "written".
        var noRecap = source.IndexOf("if (recap.Spoken is null)", StringComparison.Ordinal);
        var honours = source.IndexOf("await HonoursOnTheirOwnAsync();", noRecap, StringComparison.Ordinal);
        var forget = source.IndexOf("ForgetUnheardRecap();", noRecap, StringComparison.Ordinal);

        Assert.True(noRecap > 0 && forget > noRecap && forget < honours,
            "The no-recap way out no longer forgets a recap that was written and never spoken.");
        Assert.Contains("[commentary] Recap called off after {tookSeconds:0.0}s - {CalledOff()} before it was spoken.", source, StringComparison.Ordinal);

        // Queued is noted straight after the recap was queued, and the
        // finally forgets anything that was not.
        var queued = source.IndexOf("recapWasQueued = true;", StringComparison.Ordinal);
        var dropped = source.IndexOf("if (!recapQueued)", StringComparison.Ordinal);
        var aired = source.IndexOf("Volatile.Write(ref _recapAiredGeneration, recapGeneration);", StringComparison.Ordinal);

        Assert.True(dropped > 0 && queued > dropped && aired > queued, "recapWasQueued is not set where the recap is queued.");
        Assert.Contains("ForgetRecapUnlessQueued(recapWriting, recapWasQueued, forgetting.ForgetRaceSummary);", source, StringComparison.Ordinal);
    }

    // --- R2: the result at the flag names finishers only ----------------------------------

    private static SessionResultEntry Row(int position, string transponder, int laps, string outcome = "", long fastLap = 0) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        Outcome = outcome,
        FastLapMs = fastLap,
        Status = "active",
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        var record = new SessionRecord { SessionType = RaceSessionType.Race, Label = "Stock A-Main" };
        record.Results.AddRange(rows);

        return record;
    }

    [Fact]
    public void A_disqualified_car_is_not_named_among_those_beaten()
    {
        var (podium, _) = RaceControlControl.FlagResultRows(Race(
            Row(1, "A", 12), Row(2, "B", 12), Row(3, "C", 11, outcome: "DQ"), Row(4, "D", 10)));

        Assert.Equal(new[] { "A", "B", "D" }, podium.Select(r => r.Transponder));
    }

    [Fact]
    public void A_race_whose_only_car_was_disqualified_has_no_winner_to_call()
    {
        var (podium, _) = RaceControlControl.FlagResultRows(Race(Row(1, "C", 11, outcome: "DQ")));

        Assert.Empty(podium);
    }

    [Fact]
    public void A_car_with_no_laps_is_not_named_and_cannot_be_the_winner()
    {
        Assert.Empty(RaceControlControl.FlagResultRows(Race(Row(1, "A", 0), Row(2, "B", 0))).Podium);

        var (podium, _) = RaceControlControl.FlagResultRows(Race(Row(1, "A", 5), Row(2, "B", 0)));

        Assert.Equal(new[] { "A" }, podium.Select(r => r.Transponder));
    }

    [Fact]
    public void The_fastest_lap_of_a_disqualified_car_is_not_credited_but_a_DNF_s_is()
    {
        var (_, fastest) = RaceControlControl.FlagResultRows(Race(
            Row(1, "A", 12, fastLap: 9000), Row(2, "B", 8, outcome: "DNF", fastLap: 8800), Row(3, "C", 12, outcome: "DQ", fastLap: 8500)));

        Assert.Equal("B", fastest?.Transponder);
    }

    [Fact]
    public void The_result_read_and_the_throws_take_their_winner_from_the_finishers()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains("var (podium, fastest) = FlagResultRows(record);", source, StringComparison.Ordinal);

        // Bug hunt 17: read once, on the UI thread, and handed to both throws
        // (the built-in one and the AI's brief) - see flagWinner.
        Assert.Equal(1, CountOf(source, "FlagResultRows(record).Podium is { Count: > 0 } flagPodium ? WinnerName(flagPodium[0])"));
        Assert.Contains("FlagHandOffBrief(flagWinner, resultRead)", source, StringComparison.Ordinal);
        Assert.Contains("RenderFlagThrowAsync(flagWinner, followOnWork.Token)", source, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;

        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // --- H2: the AI hand back goes with the read it was written for -----------------------

    [Fact]
    public void The_AI_hand_back_goes_with_the_AI_read_or_with_no_AI_read_at_all()
    {
        Assert.True(RaceControlControl.KeepsAiHandBack(null, aiReadSaid: false));
        Assert.True(RaceControlControl.KeepsAiHandBack("Billy Bob takes Nail-Biter.", aiReadSaid: true));

        // Written, but the built-in read went out: the hand back reacts to
        // words the room never heard.
        Assert.False(RaceControlControl.KeepsAiHandBack("Billy Bob takes Nail-Biter.", aiReadSaid: false));
    }

    [Fact]
    public void The_honours_speak_path_asks_before_using_the_AI_hand_back()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        var said = source.IndexOf("aiReadSaid = true;", StringComparison.Ordinal);
        var kept = source.IndexOf("KeepsAiHandBack(plan.Read is", StringComparison.Ordinal);
        var back = source.IndexOf("lines[2] = back;", StringComparison.Ordinal);

        Assert.True(said > 0 && kept > said && back > kept, "The AI hand back is used without asking whether its read was said.");
    }

    // --- H1: a pick called by first name --------------------------------------------------

    private static AchievementDefinition Def(string name, AchievementTier tier = AchievementTier.Bronze) =>
        new($"test-{Guid.NewGuid():N}", name, $"Awarded for {name.ToLowerInvariant()}.", AchievementCategory.Race, "star", tier);

    private static AchievementUnlock Unlock(string driver, AchievementDefinition badge) => new(driver, driver, badge, string.Empty);

    private static readonly AchievementDefinition NailBiter = Def("Nail-Biter");
    private static readonly AchievementDefinition HatTrick = Def("Hat-Trick", AchievementTier.Gold);
    private static readonly AchievementDefinition PersonalBest = Def("Personal Best");

    private static IReadOnlyList<HonoursPick> TwoDrivers() => RaceHonours.Picked(new[]
    {
        Unlock("Billy Bob", NailBiter), Unlock("Jane Doe", HatTrick), Unlock("Jane Doe", PersonalBest),
    });

    [Fact]
    public void A_badge_given_to_a_driver_called_by_first_name_who_did_not_earn_it_is_turned_down()
    {
        // Personal Best is Jane's; "Billy matches her" gave it to Billy in the
        // room's ears, while the check credited it to Jane.
        Assert.Null(HonoursWriting.Check(
            "Billy Bob takes Nail-Biter. Jane Doe takes Hat-Trick - and Billy matches her with Personal Best.",
            TwoDrivers(), null, string.Empty));
    }

    [Fact]
    public void A_first_name_may_stand_for_the_driver_who_earned_the_badge()
    {
        Assert.NotNull(HonoursWriting.Check(
            "Billy Bob takes Nail-Biter. Jane Doe takes Hat-Trick, and Jane adds Personal Best.",
            TwoDrivers(), null, string.Empty));
    }

    [Fact]
    public void A_lower_case_word_that_is_somebody_s_first_name_is_not_them()
    {
        var picks = RaceHonours.Picked(new[] { Unlock("Will Smith", NailBiter), Unlock("Jane Doe", HatTrick) });

        // "will" is a verb here, not Will Smith - Hat-Trick stays Jane's.
        Assert.NotNull(HonoursWriting.Check(
            "Will Smith takes Nail-Biter. Jane Doe takes Hat-Trick, and that will be remembered.",
            picks, null, string.Empty));
        Assert.NotNull(HonoursWriting.Check(
            "Will Smith takes Nail-Biter. Jane Doe, who will be pleased, takes Hat-Trick.",
            picks, null, string.Empty));
    }

    [Fact]
    public void A_first_name_shared_with_somebody_else_in_the_race_is_not_taken_as_the_pick()
    {
        // "Sam" could be Sam Jones, who raced and earned nothing - so it is
        // not counted as Sam Smith, and the check does not guess.
        var picks = RaceHonours.Picked(new[] { Unlock("Sam Smith", NailBiter) });

        Assert.NotNull(HonoursWriting.Check("Sam Smith takes Nail-Biter.", picks, new[] { "Sam Jones" }, string.Empty));
    }

    [Fact]
    public void Somebody_else_from_the_race_called_by_first_name_is_turned_down()
    {
        Assert.Null(HonoursWriting.Check(
            "Billy Bob takes Nail-Biter after a fight with Sam, and Jane Doe adds Hat-Trick and Personal Best.",
            TwoDrivers(), new[] { "Sam Smith" }, string.Empty));
    }

    // --- H3: the count is what was earned -------------------------------------------------

    [Fact]
    public void The_facts_count_every_achievement_a_driver_earned_not_just_the_three_read_out()
    {
        var unlocks = Enumerable.Range(1, 5).Select(i => Unlock("Jane Doe", Def($"Badge {i}"))).ToArray();
        var picks = RaceHonours.Picked(unlocks);

        Assert.True(picks[0].Badges.Count < 5, "The read no longer caps a driver's badges - this test needs a new shape.");

        var facts = string.Join("\n", HonoursWriting.Facts(picks, unlocks, null));

        Assert.Contains($"Jane Doe earned five achievements in this race, {HonoursWriting.Words(picks[0].Badges.Count)} of them read out here:", facts, StringComparison.Ordinal);
    }

    // --- H4: numbers and club-first claims said in words -----------------------------------

    private static IReadOnlyList<HonoursPick> Jane() => RaceHonours.Picked(new[] { Unlock("Jane Doe", HatTrick) });

    [Fact]
    public void A_count_said_in_words_that_is_not_in_the_facts_is_turned_down()
    {
        var notes = new List<string>();

        Assert.Null(HonoursWriting.Check(
            "Jane Doe takes Hat-Trick - her eighth achievement.", Jane(), null, "Jane Doe now has two achievements in all.", notes));
        Assert.Contains(notes, n => n.Contains("eighth", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ordinal_that_matches_the_facts_count_passes()
    {
        Assert.NotNull(HonoursWriting.Check(
            "Jane Doe takes Hat-Trick - her second achievement.", Jane(), null, "Jane Doe now has two achievements in all."));
    }

    [Fact]
    public void A_digit_for_a_count_the_facts_give_in_words_passes()
    {
        Assert.NotNull(HonoursWriting.Check(
            "Jane Doe takes Hat-Trick - that makes 2.", Jane(), null, "Jane Doe now has two achievements in all."));
    }

    [Fact]
    public void A_club_first_the_facts_do_not_give_is_turned_down()
    {
        const string facts = "Jane Doe earned one achievement in this race:\n- Hat-Trick. three drivers at the club hold it now.";

        Assert.Null(HonoursWriting.Check("Jane Doe takes Hat-Trick, the first at the club.", Jane(), null, facts));
        Assert.Null(HonoursWriting.Check("Jane Doe takes Hat-Trick - nobody had done it before.", Jane(), null, facts));
        Assert.Null(HonoursWriting.Check("Jane Doe takes Hat-Trick, her first achievement.", Jane(), null, facts));
    }

    [Fact]
    public void An_ordinal_the_badge_s_own_description_uses_passes()
    {
        // Review (7 Nov 2026): Photo Finish is "Finished within a second of
        // the car next to you." - a read quoting it was thrown away.
        var badge = AchievementCatalogue.Get(AchievementCatalogue.PhotoFinish)!;
        var unlocks = new[] { new AchievementUnlock("Jane Doe", "Jane Doe", badge, string.Empty) };
        var facts = string.Join("\n", HonoursWriting.Facts(RaceHonours.Picked(unlocks), unlocks, null));

        Assert.Contains("within a second", facts, StringComparison.Ordinal);
        Assert.NotNull(HonoursWriting.Check(
            "Jane Doe takes Photo Finish - within a second of the car next to her.", RaceHonours.Picked(unlocks), null, facts));
    }

    [Fact]
    public void A_count_the_facts_give_as_a_digit_may_be_said_in_words()
    {
        // The Bridesmaid's detail, as the evaluator writes it: "{n} second places".
        var badge = AchievementCatalogue.Get(AchievementCatalogue.Bridesmaid)!;
        var unlocks = new[] { new AchievementUnlock("Jane Doe", "Jane Doe", badge, "6 second places") };
        var picks = RaceHonours.Picked(unlocks);
        var facts = string.Join("\n", HonoursWriting.Facts(picks, unlocks, null));

        Assert.Contains("6 second places", facts, StringComparison.Ordinal);
        Assert.NotNull(HonoursWriting.Check($"Jane Doe takes {badge.Name} - six second places now.", picks, null, facts));
        Assert.Null(HonoursWriting.Check($"Jane Doe takes {badge.Name} - nine second places now.", picks, null, facts));
    }

    [Fact]
    public void A_first_name_opening_a_sentence_is_not_taken_for_the_driver()
    {
        // Review (7 Nov 2026): "Will anyone stop Jane Doe?" is a question, not
        // Will Smith.
        var picks = RaceHonours.Picked(new[]
        {
            Unlock("Will Smith", NailBiter), Unlock("Jane Doe", HatTrick), Unlock("Jane Doe", PersonalBest),
        });

        Assert.NotNull(HonoursWriting.Check(
            "Will Smith takes Nail-Biter. Jane Doe adds Hat-Trick and Personal Best. Will anyone stop Jane Doe?",
            picks, null, string.Empty));

        // The one that bit: taken for Will, the sentence-opening "Will" gave
        // Jane's Personal Best to him and threw an accurate read away.
        Assert.NotNull(HonoursWriting.Check(
            "Will Smith takes Nail-Biter. Jane Doe adds Hat-Trick. Will she stop there? No - Personal Best too.",
            picks, null, string.Empty));
    }

    [Fact]
    public void A_club_first_the_facts_give_passes()
    {
        const string facts = "Jane Doe earned one achievement in this race:\n- Hat-Trick. Nobody at the club had earned it before tonight.\n"
                             + "This is Jane Doe's first achievement ever.";

        Assert.NotNull(HonoursWriting.Check("Jane Doe takes Hat-Trick, the first at the club.", Jane(), null, facts));
        Assert.NotNull(HonoursWriting.Check("Jane Doe takes Hat-Trick, her first achievement ever.", Jane(), null, facts));
    }
}
