namespace HeatCaller;

/// <summary>
/// The calls that know a driver's history (11 Nov 2026, voice speed plan,
/// phase 4 #4, asked for by Asher): quicker than last time out, back on
/// form, and their milestones at the club. Race Control answers from the
/// saved races (RaceControlControl.DriverHistory); the announcer says it.
/// The rivals' battle is in CheckCloseBattles, and their quickest ever here
/// in CallTheBestEverHere.
/// </summary>
/// <remarks>
/// WHY. A call about the driver's own story - their best lap in weeks, their
/// hundredth race - is the one they repeat on the way home. With calls heard
/// about a second after the crossing, it lands while they are still on the
/// lap after it, not as a fact read out after the race.
///
/// SAID INSTEAD OF THE PERSONAL BEST, as Fastest ever here is: one lap, one
/// call. Their best ever here first, then back on form, then last time out.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Asked by Race Control's history (RaceControlControl.LapAgainstHistory)
    /// as a personal best comes in: back on form or quicker than last time
    /// out, with the mark it beat - else null. The third argument is whether
    /// a call is switched on, so one that is off is not used up for the night.
    /// </summary>
    public Func<string, long, Func<AnnouncementEventType, bool>, (AnnouncementEventType Type, long Previous)?>? LapAgainstHistory { get; set; }

    /// <summary>Asked by Race Control (RaceControlControl.CareerBefore): the driver's races and laps at the club before this race, or null.</summary>
    public Func<string, (int Races, int Laps)?>? CareerBefore { get; set; }

    /// <summary>
    /// Says the lap as back on form or quicker than last time out, when it is
    /// one and that call is on. True when it was called, so the ordinary
    /// personal best is not said for the same lap.
    /// </summary>
    private bool CallTheLapAgainstHistory(string transponderId, RaceStanding standing, long lapMs)
    {
        var events = _voiceStore.Current.Events;

        bool On(AnnouncementEventType type) =>
            events.GetValueOrDefault(type.ToString()) is { } config && CallOn(config) && config.EffectiveTemplates.Count > 0;

        if (!On(AnnouncementEventType.BackOnForm) && !On(AnnouncementEventType.BeatLastTimeOut))
        {
            return false;
        }

        (AnnouncementEventType Type, long Previous)? found;

        try
        {
            found = LapAgainstHistory?.Invoke(transponderId, lapMs, On);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] Couldn't check {transponderId}'s laps against their history ({ex.Message}) - called as a personal best.");
            return false;
        }

        if (found is not { } said || !On(said.Type))
        {
            return false;
        }

        var margin = (said.Previous - lapMs) / 1000.0;
        var extras = new Dictionary<string, (string Spoken, string Display)>(StringComparer.OrdinalIgnoreCase)
        {
            ["previous"] = (FormatSecondsForSpeech(said.Previous), FormatSecondsForDisplay(said.Previous)),
            ["margin"] = (RaceNumbers.MarginSpoken(margin), $"{margin:0.000}s"),
        };

        EnqueueVoice(said.Type, standing, null, lapMs, extras: extras);

        return true;
    }

    // ---------------------------------------------------------------- milestones

    /// <summary>The laps of a race on which a race milestone is offered: the second (lap one is a start, not a moment), and again two laps on if it was not heard.</summary>
    internal static readonly int[] RaceMilestoneLaps = { 2, 4 };

    /// <summary>"race|transponder" and "lap|transponder|N" once heard this race. Under _milestonesLock.</summary>
    private readonly HashSet<string> _milestonesHeard = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _milestonesLock = new();

    /// <summary>A new race: no milestone of the last one is this one's.</summary>
    private void ForgetMilestones()
    {
        lock (_milestonesLock)
        {
            _milestonesHeard.Clear();
        }
    }

    /// <summary>A round number said as the ordinal: "fiftieth", "hundredth", "two thousandth".</summary>
    internal static string MilestoneOrdinal(int n)
    {
        // RaceNumbers.Words writes digits from a thousand up.
        if (n >= 1000 && n % 1000 == 0)
        {
            return n == 1000 ? "thousandth" : $"{RaceNumbers.Words(n / 1000)} thousandth";
        }

        var words = RaceNumbers.Words(n);

        if (words.StartsWith("one ", StringComparison.Ordinal))
        {
            words = words[4..];
        }

        return words.EndsWith('y') ? words[..^1] + "ieth" : words + "th";
    }

    /// <summary>
    /// Called on every crossing: the driver's 50th or hundredth race at the
    /// club, offered early in it; or their thousandth lap, called on the lap
    /// itself. Each once, marked only when it is heard.
    /// </summary>
    private void CheckMilestones(RaceStanding crossed)
    {
        try
        {
            if (Muted || IsHandAdjustment(crossed) || CareerBefore is null)
            {
                return;
            }

            var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.Milestone));

            if (!VoiceCandidateCanSpeak(AnnouncementEventType.Milestone, config))
            {
                return;
            }

            if (CareerBefore(crossed.Transponder) is not { } career)
            {
                return;
            }

            string? key = null;
            string? spoken = null;
            string? shown = null;
            long? sayBy = null;

            if (DriverHistory.LapMilestoneReached(career.Laps, crossed.Laps) is { } laps)
            {
                key = $"lap|{crossed.Transponder}|{laps}";
                spoken = $"{MilestoneOrdinal(laps)} lap";
                shown = $"{laps:N0}th lap";

                // About this lap, so said while it is.
                sayBy = TimingClock.NowMs + SayWithinMsFor(AnnouncementEventType.FastLap);
            }
            else if (!IsPracticeMode && RaceMilestoneLaps.Contains(crossed.Laps) && DriverHistory.IsRaceMilestone(career.Races + 1))
            {
                var race = career.Races + 1;
                key = $"race|{crossed.Transponder}";
                spoken = $"{MilestoneOrdinal(race)} race";
                shown = $"{race:N0}th race";
            }

            if (key is null)
            {
                return;
            }

            lock (_milestonesLock)
            {
                if (_milestonesHeard.Contains(key))
                {
                    return;
                }
            }

            var extras = new Dictionary<string, (string Spoken, string Display)>(StringComparer.OrdinalIgnoreCase)
            {
                ["milestone"] = (spoken!, shown!),
            };

            var heard = key;

            ConsiderPerLapAction("voice", crossed.Transponder, config!.Weight, MaxVoicePerLap,
                () => EnqueueVoice(AnnouncementEventType.Milestone, crossed, null, extras: extras, sayByMs: sayBy, heard: () =>
                {
                    lock (_milestonesLock)
                    {
                        _milestonesHeard.Add(heard);
                    }
                }));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] Couldn't check {crossed.Transponder}'s milestones ({ex.Message}).");
        }
    }

    // ---------------------------------------------------------------- rivals

    /// <summary>
    /// Whether the two cars are a rivalry the club has set up on the
    /// Rivalries page (switched on) - so their battle is called as one. By
    /// name, as CheckRivalries matches them.
    /// </summary>
    private bool AreRivals(string transponderA, string transponderB)
    {
        if (_rivalries is null || _rivalries.All.Count == 0)
        {
            return false;
        }

        var nameA = _profiles.Get(transponderA)?.DriverName;
        var nameB = _profiles.Get(transponderB)?.DriverName;

        if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB))
        {
            return false;
        }

        return _rivalries.All.Any(r => r.Enabled
            && ((string.Equals(r.DriverA, nameA, StringComparison.OrdinalIgnoreCase) && string.Equals(r.DriverB, nameB, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(r.DriverA, nameB, StringComparison.OrdinalIgnoreCase) && string.Equals(r.DriverB, nameA, StringComparison.OrdinalIgnoreCase))));
    }
}
