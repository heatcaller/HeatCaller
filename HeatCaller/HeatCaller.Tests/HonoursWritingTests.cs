namespace HeatCaller.Tests;

/// <summary>
/// The achievements read written by the AI (HonoursWriting, 7 Nov 2026) -
/// the facts the reader is given, and above all the check that holds the
/// model's read to who got what. A read that gives a badge to the wrong
/// person, in front of that person, is the one thing this segment must never
/// do; every way the check can be fooled is a row here.
/// </summary>
public sealed class HonoursWritingTests
{
    private static AchievementDefinition Def(string name, AchievementTier tier = AchievementTier.Bronze, string? id = null) =>
        new(id ?? $"test-{Guid.NewGuid():N}", name, $"Awarded for {name.ToLowerInvariant()}.", AchievementCategory.Race, "star", tier);

    private static AchievementUnlock Unlock(string driver, AchievementDefinition badge, string detail = "") =>
        new(driver, driver, badge, detail);

    private static readonly AchievementDefinition NailBiter = Def("Nail-Biter");
    private static readonly AchievementDefinition HatTrick = Def("Hat-Trick", AchievementTier.Gold);
    private static readonly AchievementDefinition PersonalBest = Def("Personal Best");
    private static readonly AchievementDefinition WeekendWarrior = Def("Weekend Warrior");

    /// <summary>Billy Bob for Nail-Biter; Jane Doe for Hat-Trick and Personal Best.</summary>
    private static IReadOnlyList<HonoursPick> Picks(params AchievementUnlock[] unlocks) => RaceHonours.Picked(unlocks);

    private static IReadOnlyList<HonoursPick> TwoDrivers() => Picks(
        Unlock("Billy Bob", NailBiter),
        Unlock("Jane Doe", HatTrick),
        Unlock("Jane Doe", PersonalBest));

    private static string? Check(string reply, IReadOnlyList<HonoursPick> picks, string facts = "", params string[] others) =>
        HonoursWriting.Check(reply, picks, others, facts);

    // ------------------------------------------------------------------
    // What passes
    // ------------------------------------------------------------------

    [Fact]
    public void A_read_that_gives_every_driver_their_own_badges_passes()
    {
        var read = Check(
            "Billy Bob takes the Nail-Biter, for a finish decided by a whisker. And Jane Doe - a Hat-Trick, the gold one, "
            + "and she adds Personal Best on top. Well done, Jane.",
            TwoDrivers());

        Assert.NotNull(read);
        Assert.StartsWith("Billy Bob takes", read);
    }

    [Fact]
    public void A_badge_two_drivers_share_is_given_to_both_when_both_are_named_first()
    {
        var picks = Picks(Unlock("Billy Bob", WeekendWarrior), Unlock("Jane Doe", WeekendWarrior));

        Assert.NotNull(Check("Billy Bob and Jane Doe both pick up Weekend Warrior - racing both days.", picks));
    }

    [Fact]
    public void Hyphens_case_and_a_possessive_do_not_matter()
    {
        Assert.NotNull(Check(
            "billy bob's night ends with a nail biter. Jane Doe collects hat trick and personal best.",
            TwoDrivers()));
    }

    [Fact]
    public void A_name_said_straight_against_a_badge_is_still_two_names()
    {
        Assert.NotNull(Check("Billy Bob, Nail-Biter. Jane Doe, Hat-Trick and Personal Best.", TwoDrivers()));
    }

    [Fact]
    public void A_speaker_label_brackets_and_quotes_are_taken_off_and_a_full_stop_added()
    {
        var read = Check(
            "READER: \"[excited] Billy Bob takes Nail-Biter, and Jane Doe adds Hat-Trick and Personal Best\"",
            TwoDrivers());

        Assert.Equal("Billy Bob takes Nail-Biter, and Jane Doe adds Hat-Trick and Personal Best.", read);
    }

    [Fact]
    public void A_number_from_the_facts_may_be_said()
    {
        var picks = Picks(Unlock("Billy Bob", Def("Century")));

        Assert.NotNull(Check("Billy Bob takes Century - 100 laps.", picks, facts: "What it was earned with: 100 laps."));
    }

    // ------------------------------------------------------------------
    // What is turned down - and why each one matters
    // ------------------------------------------------------------------

    [Fact]
    public void A_badge_given_to_the_wrong_driver_is_turned_down()
    {
        var notes = new List<string>();

        Assert.Null(HonoursWriting.Check(
            "Jane Doe takes Nail-Biter, and Billy Bob adds Hat-Trick and Personal Best.", TwoDrivers(), null, string.Empty, notes));
        Assert.Contains(notes, n => n.Contains("did not earn it", StringComparison.Ordinal));
    }

    [Fact]
    public void A_badge_said_before_anybody_is_named_is_turned_down()
    {
        // "A Nail-Biter for Billy Bob" - the order the check cannot read, and
        // so must not guess at.
        Assert.Null(Check(
            "A Nail-Biter for Billy Bob, and Jane Doe takes Hat-Trick and Personal Best.", TwoDrivers()));
    }

    [Fact]
    public void A_badge_running_on_to_the_next_driver_is_turned_down()
    {
        // Personal Best after Jane's Hat-Trick, but said once Billy is the
        // subject again - it is being given to Billy.
        Assert.Null(Check(
            "Jane Doe takes Hat-Trick. Billy Bob takes Nail-Biter, and Personal Best too.", TwoDrivers()));
    }

    [Fact]
    public void A_driver_left_out_is_turned_down()
    {
        Assert.Null(Check("Jane Doe takes Hat-Trick and Personal Best.", TwoDrivers()));
    }

    [Fact]
    public void A_badge_left_out_is_turned_down()
    {
        Assert.Null(Check("Billy Bob takes Nail-Biter. Jane Doe takes Hat-Trick.", TwoDrivers()));
    }

    [Fact]
    public void Somebody_else_from_the_race_named_is_turned_down()
    {
        Assert.Null(Check(
            "Billy Bob takes Nail-Biter after a fight with Sam Smith, and Jane Doe adds Hat-Trick and Personal Best.",
            TwoDrivers(), string.Empty, "Sam Smith"));
    }

    [Fact]
    public void A_number_that_is_not_in_the_facts_is_turned_down()
    {
        var notes = new List<string>();

        Assert.Null(HonoursWriting.Check(
            "Billy Bob takes Nail-Biter by 0.2 seconds, and Jane Doe adds Hat-Trick and Personal Best.",
            TwoDrivers(), null, "Billy Bob earned one achievement.", notes));
        Assert.Contains(notes, n => n.Contains("0.2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_read_that_hands_back_itself_is_turned_down()
    {
        // The hand back is its own turn - said twice otherwise.
        Assert.Null(Check(
            "Billy Bob takes Nail-Biter, Jane Doe adds Hat-Trick and Personal Best. Back to you, Rick.", TwoDrivers()));
    }

    [Fact]
    public void A_read_that_runs_long_is_turned_down()
    {
        var padding = string.Join(" ", Enumerable.Repeat("really", HonoursWriting.MaxWords(2)));

        Assert.Null(Check($"Billy Bob takes Nail-Biter {padding}, Jane Doe adds Hat-Trick and Personal Best.", TwoDrivers()));
    }

    [Fact]
    public void Nothing_back_is_turned_down()
    {
        Assert.Null(Check("   ", TwoDrivers()));
        Assert.Null(Check("Billy Bob takes Nail-Biter.", Array.Empty<HonoursPick>()));
    }

    [Fact]
    public void A_driver_whose_name_sits_inside_another_is_not_mistaken_for_them()
    {
        // "Sam" raced too, and earned nothing; "Sam Smith" is being read out.
        // The longer name wins where they overlap, so this is not "naming Sam".
        var picks = Picks(Unlock("Sam Smith", NailBiter));

        Assert.NotNull(Check("Sam Smith takes Nail-Biter.", picks, string.Empty, "Sam"));
        Assert.Null(Check("Sam Smith takes Nail-Biter, just ahead of Sam.", picks, string.Empty, "Sam"));
    }

    // ------------------------------------------------------------------
    // The picks are the built-in read's picks
    // ------------------------------------------------------------------

    [Fact]
    public void The_AI_is_asked_about_exactly_the_drivers_and_badges_the_built_in_read_names()
    {
        var crowd = Enumerable.Range(1, RaceHonours.MostDrivers + 2)
            .SelectMany(i => new[] { Unlock($"Driver {i}", NailBiter), Unlock($"Driver {i}", HatTrick) })
            .ToArray();

        var picks = RaceHonours.Picked(crowd);
        var read = RaceHonours.Read(crowd)!.Value.Said;

        Assert.Equal(RaceHonours.MostDrivers, picks.Count);

        foreach (var pick in picks)
        {
            Assert.Contains(pick.Driver, read, StringComparison.Ordinal);
            Assert.Equal(pick.Badges.Count, pick.Unlocks.Count);
        }

        Assert.DoesNotContain($"Driver {RaceHonours.MostDrivers + 1}", read, StringComparison.Ordinal);
    }

    [Fact]
    public void The_built_in_read_passes_its_own_check()
    {
        // The read HeatCaller writes is the shape the check was built around -
        // if this ever failed, the check would be wrong, not the read.
        var unlocks = new[] { Unlock("Billy Bob", NailBiter), Unlock("Jane Doe", HatTrick), Unlock("Jane Doe", PersonalBest) };

        Assert.NotNull(Check(RaceHonours.Read(unlocks)!.Value.Said, RaceHonours.Picked(unlocks)));
    }

    // ------------------------------------------------------------------
    // The facts
    // ------------------------------------------------------------------

    [Fact]
    public void The_facts_say_what_a_badge_is_for_its_tier_and_that_it_is_a_club_first()
    {
        var store = AchievementStore.Load();
        var driver = $"hw-{Guid.NewGuid():N}";
        var badge = Def("Hat-Trick", AchievementTier.Gold);
        store.Award(driver, badge.Id);

        var unlocks = new[] { new AchievementUnlock(driver, "Jane Doe", badge, "three in a row") };
        var facts = string.Join("\n", HonoursWriting.Facts(RaceHonours.Picked(unlocks), unlocks, store));

        Assert.Contains("Jane Doe earned one achievement in this race:", facts, StringComparison.Ordinal);
        Assert.Contains("Hat-Trick: Awarded for hat-trick.", facts, StringComparison.Ordinal);
        Assert.Contains("gold-tier", facts, StringComparison.Ordinal);
        Assert.Contains("What it was earned with: three in a row.", facts, StringComparison.Ordinal);
        Assert.Contains("Nobody at the club had earned it before tonight.", facts, StringComparison.Ordinal);
        Assert.Contains("This is Jane Doe's first achievement ever.", facts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_facts_count_holders_and_a_drivers_total_when_it_is_not_a_first()
    {
        var store = AchievementStore.Load();
        var jane = $"hw-{Guid.NewGuid():N}";
        var others = Enumerable.Range(0, 2).Select(_ => $"hw-{Guid.NewGuid():N}").ToList();
        var badge = Def("Nail-Biter");
        var older = Def("Old Timer");

        foreach (var other in others)
        {
            store.Award(other, badge.Id);
        }

        store.Award(jane, older.Id);
        store.Award(jane, badge.Id);

        var unlocks = new[] { new AchievementUnlock(jane, "Jane Doe", badge, string.Empty) };
        var facts = string.Join("\n", HonoursWriting.Facts(RaceHonours.Picked(unlocks), unlocks, store));

        Assert.Contains("three drivers at the club hold it now.", facts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jane Doe now has two achievements in all.", facts, StringComparison.Ordinal);
        Assert.DoesNotContain("before tonight", facts, StringComparison.Ordinal);
        Assert.DoesNotContain("first achievement", facts, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_store_the_facts_still_say_what_each_badge_is_for()
    {
        var unlocks = new[] { Unlock("Billy Bob", NailBiter) };
        var facts = string.Join("\n", HonoursWriting.Facts(RaceHonours.Picked(unlocks), unlocks, null));

        Assert.Contains("Nail-Biter: Awarded for nail-biter.", facts, StringComparison.Ordinal);
        Assert.DoesNotContain("at the club", facts, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The wiring
    // ------------------------------------------------------------------

    private static string AfterRaceSource => File.ReadAllText(Path.Combine(SourceDir(), "RaceControlControl.AfterRace.cs"));

    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
    }

    [Fact]
    public void The_read_is_asked_for_after_the_recap_and_ahead_of_the_booth_interview()
    {
        var source = AfterRaceSource;

        var generation = source.IndexOf("var generation = commentaryAvailable", StringComparison.Ordinal);
        var read = source.IndexOf("WriteHonoursRead(honours, generation, followOnWork.Token)", StringComparison.Ordinal);
        var principal = source.IndexOf("var principalTask = ", StringComparison.Ordinal);

        Assert.True(generation > 0 && read > generation && principal > read,
            "The achievements read is no longer asked for between the recap and the Team Principal.");
        Assert.Contains("(Task?)honoursRead?.Written ?? generation, generation, record", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_AI_read_goes_out_only_once_checked_and_in_hand()
    {
        var source = AfterRaceSource;

        Assert.Contains("HonoursReadInTimeAsync(readJob, nextRace)", source, StringComparison.Ordinal);
        Assert.Contains("[1] = aiRead", source, StringComparison.Ordinal);

        var service = File.ReadAllText(Path.Combine(SourceDir(), "CommentaryService.cs"));
        // Bug hunt 14 (7 Nov 2026), H4: checked against this race's facts
        // only, not the prompt's earlier reads.
        Assert.Contains("HonoursWriting.Check(StripAudioTags(reply), picks, others, numbersFrom, notes)", service, StringComparison.Ordinal);
    }
}
