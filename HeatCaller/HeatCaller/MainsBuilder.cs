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
    /// <param name="aMainLegs">
    /// 11 Nov 2026, Phase 5.3: how many legs the A-main runs as. Above 1 the
    /// A-main is that many rounds - "A-Main Leg 1", "Leg 2"... - each with the
    /// same seeded grid and each taking the bump-ups from the main below (its
    /// feeder is the same B-main, which runs before all of them), and run
    /// last, one after another. AMainLegs scores them together.
    /// </param>
    /// <param name="lowerMainsInLegs">
    /// 19 Nov 2026: the lower mains run in the same number of legs too -
    /// "B-Main Leg 1", "Leg 2"... - all of a tier's legs before the tier above,
    /// so a lower main's overall result is in before any bump-up goes up.
    /// </param>
    public static List<RoundDefinition> BuildRounds(string className, SeedPlan plan, string trackLayoutName, int bumpUpCount, int aMainLegs = 1, bool lowerMainsInLegs = false)
    {
        var rounds = new List<RoundDefinition>();
        var legs = Math.Clamp(aMainLegs, 1, AMainLegs.MaxLegs);

        // Iterate from the lowest tier upward so Order ascends with run order.
        for (var tier = plan.TierCount - 1; tier >= 0; tier--)
        {
            if (legs > 1 && (tier == 0 || lowerMainsInLegs))
            {
                for (var leg = 1; leg <= legs; leg++)
                {
                    rounds.Add(new RoundDefinition
                    {
                        ClassName = className,
                        RoundLabel = AMainLegs.LegLabel(tier, leg),
                        Kind = RoundKind.Main,
                        MainTier = tier,
                        MainLeg = leg,
                        MainLegCount = legs,
                        BumpUpCount = bumpUpCount,
                        TrackLayoutName = trackLayoutName,
                        AssignedTransponders = plan.Tiers[tier].ToList(),
                    });
                }

                continue;
            }

            rounds.Add(new RoundDefinition
            {
                ClassName = className,
                RoundLabel = $"{SeedPlan.LetterFor(tier)}-Main",
                Kind = RoundKind.Main,
                MainTier = tier,

                // Every tier gets the count, the lowest included - the same
                // rule AddMainByHand follows. The lowest used to get 0, so a
                // main added by hand below it later (late arrivals turning a
                // single A-main into A and B) left the old lowest refused
                // every bump-up as "not set up to receive" them. While it
                // really is the lowest the count is never read: the blocker
                // check stops first at "no main below this one".
                BumpUpCount = bumpUpCount,

                TrackLayoutName = trackLayoutName,
                AssignedTransponders = plan.Tiers[tier].ToList(),
            });
        }

        return rounds;
    }

    /// <summary>
    /// The label Add a Main by Hand offers: the lowest main tier not yet in
    /// use among <paramref name="classRoundsToday"/>.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A24-3: it was a count of the mains, so with
    /// A/B/C seeded and the B-Main deleted it offered "C-Main" - a second
    /// tier 2 that nothing refused, leaving the A-Main with no main below it.
    /// An untiered main ("Consolation") no longer shifts the letter either.
    /// </summary>
    internal static string DefaultHandMainLabel(IEnumerable<RoundDefinition> classRoundsToday)
    {
        var used = classRoundsToday
            .Where(r => r.Kind == RoundKind.Main && r.MainTier >= 0)
            .Select(r => r.MainTier)
            .ToHashSet();

        var tier = 0;
        while (used.Contains(tier))
        {
            tier++;
        }

        return $"{SeedPlan.LetterFor(tier)}-Main";
    }

    /// <summary>
    /// The main that feeds <paramref name="target"/>: one tier below it, and
    /// of those the latest in the running order that still comes before it.
    /// With none before it, the first one at that tier, as it always was -
    /// which keeps a reordered programme, and mains whose Order was never
    /// set, working.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A24-1: with no race day open the mains are
    /// every week's, and the first one at the tier below was last week's
    /// recorded B-Main. Tonight's A-Main took it as its feeder, passed every
    /// check, and the bump-up stripped last week's roster. Tonight's B-Main
    /// runs between last week's mains and tonight's A (lower tiers are put
    /// first), so it is the one picked now.
    /// </summary>
    public static RoundDefinition? FeederFor(RoundDefinition target, IReadOnlyList<RoundDefinition> allMains)
    {
        var below = allMains.Where(r => r.MainTier == target.MainTier + 1).ToList();

        return below
                   .Where(r => r.Order < target.Order)
                   .OrderByDescending(r => r.Order)
                   .FirstOrDefault()
               ?? below.FirstOrDefault();
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

        var feeder = FeederFor(target, allMains);
        if (feeder is null)
        {
            return "There's no main below this one to bump up from.";
        }

        // A lower main run in legs (19 Nov 2026) bumps up on its overall
        // result, so every one of its legs has to be in first.
        foreach (var leg in feeder.MainLeg > 0 ? AMainLegs.LegsOf(feeder, allMains) : new List<RoundDefinition> { feeder })
        {
            if (!leg.IsRecorded)
            {
                return $"{leg.RoundLabel} hasn't been run yet.";
            }

            if (history.Get(leg.LinkedSessionId!) is null)
            {
                return $"{leg.RoundLabel}'s results couldn't be found.";
            }
        }

        return null;
    }

    /// <summary>
    /// The result a bump-up from <paramref name="feeder"/> is read from: its
    /// own, or - for a main run in legs (19 Nov 2026) - its overall result
    /// across the legs (AMainLegs.Overall), as one result in that order, so
    /// PlanBumpUp moves the top of the overall standings. A driver who did
    /// not finish any leg is left out of it: like a DQ, that earns no place.
    /// Null while a leg is still to run, or its result can't be found.
    /// </summary>
    public static SessionRecord? FeederResults(
        RoundDefinition feeder,
        IReadOnlyList<RoundDefinition> allMains,
        Func<string, SessionRecord?> sessionById,
        int countedSetting,
        Func<string, string?>? primaryForAlternate = null)
    {
        if (feeder.MainLeg <= 0)
        {
            return feeder.LinkedSessionId is { } id ? sessionById(id) : null;
        }

        var legs = AMainLegs.LegsOf(feeder, allMains);
        var results = new List<SessionRecord>();

        foreach (var leg in legs)
        {
            if (leg.LinkedSessionId is not { } id || sessionById(id) is not { } result)
            {
                return null;
            }

            results.Add(result);
        }

        var legCount = Math.Max(legs.Count, legs.Max(l => l.MainLegCount));
        var table = AMainLegs.Overall(results, AMainLegs.LegsCounting(legCount, countedSetting), primaryForAlternate, legs.Select(l => l.MainLeg).ToList());

        return new SessionRecord
        {
            Id = results[^1].Id,
            SessionType = RaceSessionType.Race,
            Label = $"{feeder.TierLetter}-Main overall",
            RoundId = feeder.Id,
            StartedAtUtc = results[0].StartedAtUtc,
            EndedAtUtc = results[^1].EndedAtUtc,
            Results = table
                .Where(s => s.BestFinish > 0)
                .Select((s, i) => new SessionResultEntry
                {
                    Position = i + 1,
                    Transponder = s.Transponder,
                    DriverName = s.DriverName,
                    Laps = 1,
                    Status = "active",
                })
                .ToList(),
        };
    }

    /// <summary>
    /// The mains one bump-up goes into (11 Nov 2026): <paramref name="target"/>
    /// alone, or - when it is a leg of a double or triple A-main - every leg
    /// of that A-main still to run and still waiting for its bump-ups, in
    /// leg order. The legs share a grid and a feeder, so the director moves
    /// the B-main's top finishers up once rather than leg by leg. A leg
    /// already run, or already given its bump-ups, is left as it is.
    /// </summary>
    public static List<RoundDefinition> BumpUpTargets(RoundDefinition target, IReadOnlyList<RoundDefinition> allMains)
    {
        if (target.MainLeg <= 0)
        {
            return new List<RoundDefinition> { target };
        }

        var others = allMains
            .Where(r => !ReferenceEquals(r, target)
                        && !string.Equals(r.Id, target.Id, StringComparison.Ordinal)
                        && r.Kind == RoundKind.Main
                        && r.MainTier == target.MainTier
                        && r.MainLeg > 0
                        && string.Equals(r.ClassName, target.ClassName, StringComparison.OrdinalIgnoreCase)
                        && r.BumpUpCount > 0
                        && !r.BumpUpsApplied
                        && !r.IsRecorded
                        && ReferenceEquals(FeederFor(r, allMains), FeederFor(target, allMains)));

        return new[] { target }.Concat(others).OrderBy(r => r.MainLeg).ToList();
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
        // WHY (23 Nov 2026, bug hunt 24): a row with no laps is a car that
        // never crossed the line - a grid car put on the board by a
        // hand-released green and never let go, or one added by hand and
        // never tapped. It is filed numbered at the bottom, so in a short
        // feeder with fewer real finishers than the bump-up count it used to
        // be moved up into the main above without having raced its way in.
        // The same feeder run in legs already left it out (AMainLegs.Overall
        // skips Laps <= 0, and SeasonStore gives it no points), so now a
        // single feeder does too. A DNF with laps still counts in its place.
        foreach (var result in feederResults.Results
                     .Where(r => !string.IsNullOrWhiteSpace(r.Transponder)
                                 && !GhostService.IsGhost(r.Transponder)
                                 && r.Outcome != ResultEditor.Dq
                                 && r.Laps > 0)
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
