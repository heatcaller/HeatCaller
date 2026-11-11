namespace HeatCaller;

/// <summary>
/// Personal bests lost behind a line, said together once it ends (10 Nov
/// 2026, voice speed plan, phase 2).
/// </summary>
/// <remarks>
/// WHY. A personal best is the call most often lost - eight in one race of
/// the 10 Nov demos, "still speaking the previous one" - because they come
/// in bunches: a field finding its pace improves together, and only one of
/// them can have the speaker. Each one is a driver who did something and
/// heard nothing. Said one by one they would be the backlog the drop rule
/// prevents; said as one line - "Personal bests for Dee Ploy and Al
/// gorerhythm!" - they cost one call's air and every driver hears their name.
///
/// ONLY WHAT IS STILL TRUE. A personal best lost while a line was speaking
/// is remembered with its lap's own deadline (LapCallSayWithinMs from the
/// crossing). As the line ends, the ones still inside it - and only if two
/// or more drivers are - become one call, with the earliest deadline among
/// them, offered through the whole of EnqueueVoiceWork like any routine call:
/// the pacing dial, the speaker being busy again and the deadline all still
/// have their say. Not offered when a call kept for the end of the line is
/// going next, which outranked them.
///
/// Its own row on the Voice page, "Personal bests together"
/// (PersonalBestsTogether) - the club's lines, ticks and speed - heard only
/// with the personal-best call on too.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Personal bests lost behind a line: who, their lap's deadline
    /// (TimingClock), and their name as said and as shown - taken when the
    /// call was lost, on the thread that owns the standings, since the
    /// combined line is made on the voice loop's. Under _combinedLock.
    /// </summary>
    private readonly List<(string Transponder, long SayByMs, string Said, string Shown)> _lostPersonalBests = new();

    private readonly object _combinedLock = new();

    /// <summary>The most names in one combined line - past three it is a list, not a call.</summary>
    internal const int MostCombined = 3;

    /// <summary>Noted from the drop-while-speaking rule: a personal best nobody heard.</summary>
    private void NoteLostPersonalBest(string? transponder)
    {
        if (transponder is null)
        {
            return;
        }

        var sayBy = LapSeenAt(transponder) + LapCallSayWithinMs;
        var said = _standings.TryGetValue(transponder, out var standing) ? SpokenNameFor(standing) : DisplayNameFor(transponder);
        var shown = DisplayNameFor(transponder);

        lock (_combinedLock)
        {
            _lostPersonalBests.RemoveAll(lost => string.Equals(lost.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
            _lostPersonalBests.Add((transponder, sayBy, said, shown));
        }
    }

    /// <summary>
    /// The drivers still worth a combined call at <paramref name="nowMs"/>:
    /// each once, inside their lap's deadline, oldest first, at most
    /// <see cref="MostCombined"/>. Fewer than two is no combined call.
    /// </summary>
    internal static IReadOnlyList<(string Transponder, long SayByMs, string Said, string Shown)> StillWorthCombining(
        IEnumerable<(string Transponder, long SayByMs, string Said, string Shown)> lost, long nowMs)
    {
        var fresh = lost.Where(l => l.SayByMs > nowMs)
            .GroupBy(l => l.Transponder, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(l => l.SayByMs)
            .Take(MostCombined)
            .ToList();

        return fresh.Count >= 2 ? fresh : Array.Empty<(string, long, string, string)>();
    }

    /// <summary>"Dee", "Dee and Al", "Dee, Al and Ana".</summary>
    internal static string TheirNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    /// <summary>
    /// Called by the voice loop as a line ends, after the kept call has had
    /// its turn: the personal bests lost behind it, as one call - see the
    /// remarks.
    /// </summary>
    private void OfferTheCombinedPersonalBests()
    {
        IReadOnlyList<(string Transponder, long SayByMs, string Said, string Shown)> together;

        lock (_combinedLock)
        {
            together = StillWorthCombining(_lostPersonalBests, TimingClock.NowMs);
            _lostPersonalBests.Clear();
        }

        if (together.Count == 0 || Muted || QueueHasAnything() || _heldOver.IsHolding)
        {
            return;
        }

        var events = _voiceStore.Current.Events;
        var personalBest = events.GetValueOrDefault(nameof(AnnouncementEventType.PersonalBestLap));
        var config = events.GetValueOrDefault(nameof(AnnouncementEventType.PersonalBestsTogether));

        // Its own row on the Voice page (Personal bests together), heard only
        // with Personal best lap on too.
        if (personalBest is null || !CallOn(personalBest) || HeldByCaution(AnnouncementEventType.PersonalBestLap, personalBest)
            || config is null || !CallOn(config) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        var names = together.Select(t => t.Said).ToList();
        var shown = together.Select(t => t.Shown).ToList();

        var template = TemplateBag.Pick(config.EffectiveTemplates);
        var text = template.Replace("{drivers}", TheirNames(names), StringComparison.OrdinalIgnoreCase);
        var display = VoiceProfile.StripAudioTags(template.Replace("{drivers}", TheirNames(shown), StringComparison.OrdinalIgnoreCase)).Trim();
        var speed = config.SpeedFor(_voiceStore.Current.Speed);
        var sayBy = together.Min(t => t.SayByMs);

        StatusMessage?.Invoke($"[voice] {together.Count} personal bests lost behind the last line, offered as one: \"{text}\"");

        EnqueueVoiceWork(AnnouncementEventType.PersonalBestLap, null,
            () => SynthesizeAndPlayAsync(text, "[voice] Personal bests", speedOverride: speed),
            displayText: display,
            sayByMs: sayBy,
            renderAhead: () => RenderLineAheadAsync(text, AnnouncementEventType.PersonalBestLap, speed));
    }

    /// <summary>A flag or a new race: lost personal bests are no longer news.</summary>
    private void ForgetLostPersonalBests()
    {
        lock (_combinedLock)
        {
            _lostPersonalBests.Clear();
        }
    }
}
