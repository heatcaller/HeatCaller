namespace HeatCaller;

/// <summary>
/// The gap for the lead, called every crossing at the end of a race (11 Nov
/// 2026, voice speed plan, phase 4).
/// </summary>
/// <remarks>
/// WHY. A close finish is the part of a race a room leans in for, and the
/// announcer said "Battle for the lead!" once - CloseBattle is once per pair
/// per race - and then nothing about it until the flag. With calls heard a
/// second and a half after the line, the gap can be called each time the
/// chaser crosses: "Point six!", "Point three - he's right there!".
///
/// AT THE LINE, NOT BETWEEN. The timing knows the gap when the chaser
/// crosses after the leader, so this is one call a lap, made the moment the
/// gap is known, and dropped unsaid if it can't start within
/// FinalLapsGapSayWithinMs - an old gap is a wrong one.
///
/// ONLY AT THE END. The leader's last FinalLaps laps of a lap race (the lap
/// limit comes from LapLimitProvider), or from the final minute or time-up
/// of a timed one - and only while P2 is on the lead lap within CloseMs.
/// Its own row on the Voice page, "Final laps gap" (FinalLapsGap).
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>How many of a lap race's last laps the countdown runs for.</summary>
    internal const int FinalLaps = 3;

    /// <summary>How close the chaser must be to be called.</summary>
    internal const long FinalLapsCloseMs = 1_000;

    /// <summary>The lap limit of the race on track, when it has one - read live. Null or nought for none.</summary>
    public Func<int?>? LapLimitProvider { get; set; }

    /// <summary>Set by the final minute or time-up of a timed race, cleared for a new one, staged or not (ForgetTheLastRace).</summary>
    private volatile bool _inTheFinalMinute;

    /// <summary>The timed race is into its last minute (NotifyFinalMinute, NotifyRaceTimeOver).</summary>
    private void NoteTheFinalMinute() => _inTheFinalMinute = true;

    /// <summary>Whether the race is in the stretch the countdown runs for, by the leader's laps.</summary>
    internal static bool InTheFinalLaps(int leaderLaps, int? lapLimit, bool finalMinute) =>
        finalMinute || (lapLimit is > 0 and var limit && leaderLaps >= limit - FinalLaps);

    /// <summary>
    /// Called on every standings update: when the car in second has just
    /// crossed, close to the leader, in the final laps, the gap is offered.
    /// </summary>
    private void CheckFinalLapsGap(IReadOnlyList<RaceStanding> ordered, RaceStanding crossed)
    {
        try
        {
            if (ordered.Count < 2 || IsPracticeMode || IsQualifyingMode || IsHandAdjustment(crossed))
            {
                return;
            }

            // In a heat classes share, the fight for the lead of the crossing
            // car's own class (bug hunt 20): the overall top two can be in
            // different classes, not racing each other at all.
            var field = TheLeadFightFor(ordered, crossed);

            if (field.Count < 2)
            {
                return;
            }

            var leader = field[0];
            var chaser = field[1];

            if (!string.Equals(chaser.Transponder, crossed.Transponder, StringComparison.OrdinalIgnoreCase)
                || chaser.Laps < 2
                || !InTheFinalLaps(ordered[0].Laps, LapLimitProvider?.Invoke(), _inTheFinalMinute))
            {
                return;
            }

            if (GapBetween(chaser, leader) is not { LapsDown: 0, GapMs: { } gapMs } || gapMs <= 0 || gapMs > FinalLapsCloseMs)
            {
                return;
            }

            var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.FinalLapsGap));

            if (!VoiceCandidateCanSpeak(AnnouncementEventType.FinalLapsGap, config))
            {
                return;
            }

            var tenths = Math.Max(1, RaceNumbers.TenthsOf(gapMs));
            var spoken = RaceNumbers.GapInTenths(tenths);
            var shown = RaceNumbers.TenthsShown(tenths);
            var rival = DisplayNameFor(leader.Transponder);
            var extras = new Dictionary<string, (string Spoken, string Display)>(StringComparer.OrdinalIgnoreCase)
            {
                ["gap"] = (spoken, shown),
                ["rival"] = (rival, rival),
            };

            // Tagged with the two cars (21 Nov 2026): "a tenth behind" said
            // after the chaser has got by is the wrong way round, so the line
            // is cut off or dropped, and the pass called, if they swap first -
            // see AnnouncementService.BattleLines. Inside the action: the
            // scope has to be open when the batch queues it.
            var pair = new BattlePair(leader.Transponder, chaser.Transponder, 1);

            ConsiderPerLapAction("voice", chaser.Transponder, config!.Weight, MaxVoicePerLap, () =>
            {
                using (AboutBattle(pair))
                {
                    EnqueueVoice(AnnouncementEventType.FinalLapsGap, chaser, null, extras: extras);
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] Couldn't call the gap for the lead ({ex.Message}).");
        }
    }

    /// <summary>
    /// The cars whose front two fight for the lead the gap call is about:
    /// the whole field, or - in a heat classes share - the cars of the
    /// crossing car's class, in board order.
    /// </summary>
    private IReadOnlyList<RaceStanding> TheLeadFightFor(IReadOnlyList<RaceStanding> ordered, RaceStanding crossed)
    {
        if (SharedHeatClasses?.Invoke() is not { Count: > 0 } classOf
            || !classOf.TryGetValue(crossed.Transponder.Trim(), out var cls)
            || string.IsNullOrWhiteSpace(cls))
        {
            return ordered;
        }

        return ordered
            .Where(standing => classOf.TryGetValue(standing.Transponder.Trim(), out var theirs)
                && string.Equals(theirs, cls, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
