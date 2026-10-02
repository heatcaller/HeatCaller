namespace HeatCaller;

/// <summary>
/// Builds tiered mains from qualifying, and moves finishers up between
/// them.
///
/// The convention this follows is the usual club one: the fastest
/// qualifiers go in the A-main, the next group in the B-main, and so on
/// down. Lower mains run FIRST, so their top finishers can bump up into
/// the tier above before it runs.
///
/// That ordering is the whole reason tiers have to be modelled explicitly
/// rather than just labelled - the app needs to know which main feeds
/// which, and whether the feeder has actually been run yet.
/// </summary>
public static class MainsBuilder
{
    public sealed record SeedPlan(int TierCount, int MainSize, List<List<string>> Tiers)
    {
        /// <summary>A for tier 0, B for 1, and so on.</summary>
        public static string LetterFor(int tier) => tier < 26 ? ((char)('A' + tier)).ToString() : $"#{tier + 1}";
    }

    /// <summary>
    /// Splits ranked qualifiers into tiers of at most <paramref name="mainSize"/>.
    ///
    /// The A-main is filled first from the top of the order, so tier 0
    /// always holds the fastest qualifiers. If the field doesn't divide
    /// evenly the LAST (slowest) main is the short one, which is what
    /// clubs do - you don't want a half-empty A-main.
    /// </summary>
    public static SeedPlan PlanSeeding(IReadOnlyList<string> rankedTransponders, int mainSize)
    {
        var size = Math.Max(2, mainSize);
        var tiers = new List<List<string>>();

        for (var i = 0; i < rankedTransponders.Count; i += size)
        {
            tiers.Add(rankedTransponders.Skip(i).Take(size).ToList());
        }

        // Nobody qualified: still offer a single empty A-main to fill by hand.
        if (tiers.Count == 0)
        {
            tiers.Add(new List<string>());
        }

        return new SeedPlan(tiers.Count, size, tiers);
    }

    /// <summary>
    /// Creates the main rounds for a plan, ordered so the LOWEST tier runs
    /// first. Without that order a bump-up is impossible - the A-main would
    /// already have run by the time the B-main finished.
    /// </summary>
    public static List<RoundDefinition> BuildRounds(string className, SeedPlan plan, string trackLayoutName, int bumpUpCount)
    {
        var rounds = new List<RoundDefinition>();

        // Iterate from the lowest tier upward so Order ascends with run order.
        for (var tier = plan.TierCount - 1; tier >= 0; tier--)
        {
            rounds.Add(new RoundDefinition
            {
                ClassName = className,
                RoundLabel = $"{SeedPlan.LetterFor(tier)}-Main",
                Kind = RoundKind.Main,
                MainTier = tier,

                // The lowest tier has nothing below it to receive from.
                BumpUpCount = tier == plan.TierCount - 1 ? 0 : bumpUpCount,

                TrackLayoutName = trackLayoutName,
                AssignedTransponders = plan.Tiers[tier].ToList(),
            });
        }

        return rounds;
    }

    /// <summary>Why a bump-up can't be applied yet, or null if it can.</summary>
    public static string? DescribeBumpUpBlocker(RoundDefinition target, IReadOnlyList<RoundDefinition> allMains, SessionHistoryStore history)
    {
        if (target.Kind != RoundKind.Main || target.MainTier < 0)
        {
            return "That round isn't a tiered main.";
        }

        if (target.BumpUpCount <= 0)
        {
            return "This main isn't set up to receive bump-ups.";
        }

        if (target.BumpUpsApplied)
        {
            return "Bump-ups have already been applied to this main.";
        }

        if (target.IsRecorded)
        {
            return "This main has already been run - bump-ups have to happen before it runs.";
        }

        var feeder = allMains.FirstOrDefault(r => r.MainTier == target.MainTier + 1);
        if (feeder is null)
        {
            return "There's no main below this one to bump up from.";
        }

        if (!feeder.IsRecorded)
        {
            return $"{feeder.RoundLabel} hasn't been run yet.";
        }

        return history.Get(feeder.LinkedSessionId!) is null
            ? $"{feeder.RoundLabel}'s results couldn't be found."
            : null;
    }

    public sealed record BumpUpResult(List<string> Promoted, List<string> Demoted);

    /// <summary>One feeder finisher in a bump-up's top places, under the driver's MAIN transponder.</summary>
    public sealed record PlannedPromotion(string Transponder, int Position, string DriverName);

    /// <summary>
    /// Who a bump-up would move: the Movable go up, the AlreadyThere
    /// earned a place but are in the target main already (usually moved up
    /// by hand) and are left alone.
    /// </summary>
    public sealed record BumpUpPlan(List<PlannedPromotion> Movable, List<PlannedPromotion> AlreadyThere);

    /// <summary>
    /// Works out who a bump-up would promote, without changing anything.
    ///
    /// The one place that decides, so the confirmation and ApplyBumpUp can
    /// never disagree. They used to pick separately - the confirmation
    /// counted ghosts and matched transponders by exact case, the apply did
    /// neither - so the list asked about was not always the list moved.
    ///
    /// <paramref name="profiles"/> maps a result on a driver's ALTERNATE
    /// transponder to his main one. A driver who ran the feeder on his
    /// spare car arrived in the results under the spare's id: that id was
    /// promoted, so the main above had an entry no hand-release, handicap or
    /// roster recognised, and the same driver could take two places when he
    /// had been listed under both. Null leaves ids as they are.
    /// </summary>
    public static BumpUpPlan PlanBumpUp(RoundDefinition target, SessionRecord feederResults, ProfileStore? profiles)
    {
        var top = new List<PlannedPromotion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A ghost is a pace reference, not an entrant - one that finished
        // near the top of the feeder took a bump-up place, so a real driver
        // who earned it stayed down and the main above got a car nobody
        // could bring to it. Left out before the places are counted, so the
        // next real finisher moves up instead.
        // Position 0 is the app's "place unknown" (see PaceAnalysis and
        // RaceReplayForm, which sort it last too). Ordered as a plain number
        // it came FIRST, so a row with no place took the winner's bump-up.
        // A driver disqualified in Edit Result is left out too. The editor
        // keeps him in the results, sorted to the bottom but still numbered
        // (a 3-car feeder with one DQ shows him as P3), so in a small feeder
        // he used to be promoted into the main above. A DQ never earns a
        // place - QualifyingCalculator skips it for the same reason - so the
        // next eligible finisher moves up instead, or nobody does. A DNF
        // still counts in its place: that is a club's call, not a penalty.
        foreach (var result in feederResults.Results
                     .Where(r => !string.IsNullOrWhiteSpace(r.Transponder)
                                 && !GhostService.IsGhost(r.Transponder)
                                 && r.Outcome != ResultEditor.Dq)
                     .OrderBy(r => r.Position <= 0 ? int.MaxValue : r.Position))
        {
            if (top.Count >= target.BumpUpCount)
            {
                break;
            }

            var main = profiles?.PrimaryForAlternate(result.Transponder) ?? result.Transponder;

            // One driver, one place: a second line for the same driver (his
            // main and his spare both in the results) does not use up a
            // place another driver earned.
            if (!seen.Add(main))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(result.DriverName) ? main : result.DriverName;
            top.Add(new PlannedPromotion(main, result.Position, name));
        }

        // Read under each driver's main transponder, as the places above
        // are: a driver put in the target main by hand under his ALTERNATE
        // id is already there, and used to be moved up a second time.
        bool InTarget(string transponder) =>
            target.AssignedTransponders.Any(t =>
                string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(profiles?.PrimaryForAlternate(t), transponder, StringComparison.OrdinalIgnoreCase));

        return new BumpUpPlan(
            top.Where(p => !InTarget(p.Transponder)).ToList(),
            top.Where(p => InTarget(p.Transponder)).ToList());
    }

    /// <summary>
    /// Moves the top finishers from the tier below into the target main -
    /// exactly the drivers <see cref="PlanBumpUp"/> lists as movable.
    ///
    /// The target GROWS by the bump-up count; nobody is moved down. It used
    /// to keep the target at its seeded size by dropping its slowest
    /// qualifiers into the feeder - but the feeder is, by definition, the
    /// main that has already RUN (DescribeBumpUpBlocker insists on it). The
    /// drivers moved down went into a race that was over, and never raced a
    /// main at all. Growing the main is how bump-ups are normally run: a
    /// main that takes bump-ups races with its seeded size plus the
    /// bump-up count, and nothing in the app caps a grid below that.
    ///
    /// Demoted is always empty now. It is kept on the result so callers
    /// written against it still compile and still read correctly.
    /// </summary>
    public static BumpUpResult ApplyBumpUp(RoundDefinition target, RoundDefinition feeder, SessionRecord feederResults, ProfileStore? profiles)
    {
        var plan = PlanBumpUp(target, feederResults, profiles);

        var promoted = plan.Movable.Select(p => p.Transponder).ToList();

        // Nobody drops out - see the summary. The list stays so the result
        // keeps its shape.
        var demoted = new List<string>();

        foreach (var transponder in promoted)
        {
            // Promoted drivers join at the back - they came up from below,
            // so they start behind the drivers already seeded here.
            target.AssignedTransponders.Add(transponder);

            // Out of the feeder under his main id and under any alternate
            // of his it was seeded with, ignoring case either way.
            feeder.AssignedTransponders.RemoveAll(t =>
                string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(profiles?.PrimaryForAlternate(t), transponder, StringComparison.OrdinalIgnoreCase));
        }

        target.BumpUpsApplied = true;
        return new BumpUpResult(promoted, demoted);
    }
}
