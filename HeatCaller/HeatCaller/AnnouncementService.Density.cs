namespace HeatCaller;

/// <summary>
/// The field-wide pacing gate and the count of what it and the speaker
/// between them did not say.
///
/// Split out rather than added to AnnouncementService.VoiceQueue.cs
/// because it is one decision with a long reason behind it, and the reason
/// is in two other classes: see <see cref="AnnouncerDensity"/> for why a
/// per-driver cap left the field unpaced, and
/// <see cref="AnnouncerLoad"/> for why a fault that only ever wrote itself
/// to the console went unreported for the whole life of the app.
///
/// EVERY DECISION HERE IS ABOUT ROUTINE CALLS ONLY. The one caller asks
/// EnqueueVoiceWork's own two lists first - IsPriorityAnnouncement and
/// IsWorthWaitingFor - so an instruction, a race moment and the running
/// order never reach this file at all. That is deliberate and it is the
/// same arrangement ShowChatter.CallIsWorthIt has: a second list of
/// what-must-never-be-held kept here would be the one that went stale.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// How much room the club has asked the announcer to leave between
    /// routine calls - the key of one of <see cref="AnnouncerDensity.All"/>,
    /// or null/blank for Balanced.
    ///
    /// ASKED ON DEMAND, like <see cref="ChatterProvider"/> beside it and
    /// for the same reason: a club that widens the gap mid-race because
    /// the announcer is talking over itself expects the next crossing to
    /// be quieter, not the next race.
    /// </summary>
    public Func<string?>? DensityProvider { get; set; }

    /// <summary>The pacing dial's current setting, or blank for Balanced - see <see cref="DensityProvider"/>.</summary>
    private string? Density => DensityProvider?.Invoke();

    /// <summary>
    /// What the race in front of the announcer is worth, so the gap can be
    /// wide in practice and tight in the top main - see
    /// <see cref="AnnouncerDensity.BaseGapSeconds"/>.
    ///
    /// A CALLBACK AND NOT A FIELD, like every other thing this class knows
    /// about the race around it. The stature is read off the club's
    /// programme on a half-second tick while somebody may be editing it,
    /// and this class must not hold a copy that goes stale in the middle
    /// of a race.
    ///
    /// Null - a host that never sets it - is RaceStature.Unknown, which is
    /// paced as a heat. AnnouncerDensity.BaseGapSeconds says why that is
    /// not the "keep exactly what you had" default the rest of the stature
    /// code uses.
    /// </summary>
    public Func<RaceStature>? StatureProvider { get; set; }

    private RaceStature Stature => StatureProvider?.Invoke() ?? RaceStature.Unknown;

    /// <summary>
    /// How many timing calls the announcer tried to make this race and how
    /// many got out - what Race Control's strip and the Commentary page
    /// both read. See <see cref="AnnouncerLoad"/>.
    /// </summary>
    internal AnnouncerLoad Load { get; } = new();

    private readonly object _pacingLock = new();

    /// <summary>
    /// When the last routine call was accepted, and what it weighed.
    ///
    /// MinValue rather than "now" at the start of a race, so the first call
    /// of a race is never held: a gap is a gap between two things, and
    /// holding the first one back would mean every race opened with
    /// silence where the announcer had something to say.
    /// </summary>
    private DateTime _lastRoutineAcceptedUtc = DateTime.MinValue;

    private int _lastRoutineWeight;

    /// <summary>
    /// Whether a routine call gets the air, counting the answer either way.
    ///
    /// The order of the two refusals is the whole point of the change and
    /// is not interchangeable:
    ///
    ///  - SOMETHING IS ALREADY BEING SPOKEN. There is no queue for routine
    ///    calls and a late lap time is worse than no lap time, which is the
    ///    judgement the original drop-while-speaking rule made and it was
    ///    right. Counted as LOST - unless the call clearly outweighs the
    ///    line being spoken, in which case it is KEPT for the moment the
    ///    sentence ends (see <see cref="HeldOver{T}"/>, 15 Oct 2026). A
    ///    lead change no longer loses its slot to the lap time it arrived
    ///    behind.
    ///
    ///  - NOTHING IS BEING SPOKEN BUT THE GAP HAS NOT RUN. Counted as
    ///    HELD, because the club asked for it not to be said.
    ///
    /// The gap therefore CONVERTS losses into holds, which is the win and
    /// is why the two are counted apart: a club widening the gap watches
    /// Held rise and Lost fall, and can see the dial working on the one
    /// screen it is already looking at.
    ///
    /// ACROSS THE FIELD (15 Oct 2026). This used to say that across cars
    /// it was still whoever crossed the loop first. The per-lap batches
    /// now hand their winners to <see cref="OfferToTheField"/>, which
    /// collects the whole field's calls for a third of a second and offers
    /// them here best first - so the first call to reach this gate is the
    /// best one the field had, and the gap holds the rest.
    /// </summary>
    /// <param name="holdOver">
    /// How to offer this call again once the speaker is free, or null for a
    /// call that may not be kept - one that is itself coming back from
    /// being kept, which gets one more chance and not a second wait.
    /// </param>
    /// <param name="lost">
    /// What to run if this call is KEPT and then lost before it is said -
    /// pushed out by a bigger call, gone stale, cleared by a flag, or turned
    /// down when its turn comes. 20 Oct 2026, Bug Hunt 8: FirePairLine gives
    /// back the pair's once-per-race battle claim (and a sponsor's ration)
    /// for a line that was dropped at once, but nothing did it for a kept
    /// line lost later, so those two cars never got a battle call again
    /// that race. Must be safe to run more than once.
    /// </param>
    /// <param name="alreadyCounted">
    /// True for a call coming back from <see cref="_heldOver"/>, which was
    /// counted as offered the first time - so Offered still equals
    /// Accepted + Held + Dropped (AnnouncerLoad.Balances).
    /// </param>
    /// <param name="kept">
    /// True only when the call was refused for now but KEPT for the end of
    /// the line (<see cref="_heldOver"/>) - so a caller that remembers what
    /// it said (FirePairLine) does not take a kept call for a dropped one.
    /// 17 Oct 2026, Bug Hunt 8, A06-2: FirePairLine read the plain false as
    /// "dropped" and gave back the pair's once-per-race claim, so the kept
    /// battle was said AND offered again on a later standings update.
    /// </param>
    /// <param name="transponder">Who the call is about, for the lost-lines window (20 Oct 2026). Null for nobody in particular.</param>
    /// <param name="words">What the call would have said, for the lost-lines window. Null when the caller has no words yet.</param>
    /// <param name="paced">Run when the call is HELD by the pacing gap, as against dropped or kept (22 Oct 2026) - see EnqueueVoiceWork.</param>
    /// <param name="routineWeight">
    /// The call's weight when it is not the weight of its type - a rivalry
    /// line, queued as a LeaderChange (23 Oct 2026, bug hunt 10). Null for
    /// the type's own.
    /// </param>
    private bool RoutineCallGetsTheAir(AnnouncementEventType type, out bool kept, Func<bool>? holdOver = null, bool alreadyCounted = false, Action? lost = null, string? transponder = null, string? words = null, Action? paced = null, int? routineWeight = null)
    {
        kept = false;

        if (!alreadyCounted)
        {
            Load.NoteOffered();
        }

        // A change of lead is never held for pacing, whatever weight the
        // Voice page has it at - the pacing dial's own description promises
        // so (22 Oct 2026, bug hunt). It was only true at the shipped 100.
        //
        // WHY (23 Oct 2026, bug hunt 10): a rivalry line borrows LeaderChange
        // as its queue key, so it took that floor too and skipped the pacing
        // dial. It brings its own weight, and the floor is for a real lead
        // change only.
        var weight = routineWeight ?? (type == AnnouncementEventType.LeaderChange
            ? Math.Max(AnnouncerDensity.Commanding, WeightOf(type))
            : WeightOf(type));
        var gap = AnnouncerDensity.GapSeconds(Stature, Density);

        if (_isVoiceSpeaking)
        {
            // Kept, not lost, when it clearly outweighs what is being said
            // - and only with pacing on. Wall to wall is the announcer
            // exactly as it was, and it never kept anything.
            if (gap > 0 && holdOver is not null
                && _heldOver.Offer(new HeldCall(type, holdOver, lost, transponder, words), weight, WeightBeingSpoken(), out var displaced))
            {
                if (displaced is not null)
                {
                    Load.NoteDropped(Unsaid(UnsaidKind.Lost, displaced.Type, displaced.Transponder, displaced.Words,
                        $"It was waiting for the end of a line, then a {CallName(type)} that outranked it took its place."));
                    displaced.Lost?.Invoke();
                    StatusMessage?.Invoke($"[voice] Dropped {displaced.Type} - {type} outranks it for the slot after this line.");
                }

                StatusMessage?.Invoke($"[voice] Keeping {type} for the end of this line - it outranks what is being said.");

                kept = true;
                return false;
            }

            Load.NoteDropped(Unsaid(UnsaidKind.Lost, type, transponder, words,
                OnAirNow is { Length: > 0 } speaking
                    ? $"The announcer was still on another line ({speaking}), and this call wasn't worth waiting for."
                    : "The announcer was still saying the line before it, and this call wasn't worth waiting for."));

            // The wording this line has always had, kept exactly, because
            // it is what a club searching an old log will be looking for -
            // and because it is still precisely true.
            StatusMessage?.Invoke($"[voice] Dropped {type} announcement - still speaking the previous one.");

            return false;
        }

        if (gap <= 0)
        {
            // Wall to wall: the announcer exactly as it was before this
            // existed. Accepted without marking a clock, because there is
            // no gap for a clock to measure.
            Load.NoteAccepted();

            return true;
        }

        lock (_pacingLock)
        {
            var since = DateTime.UtcNow - _lastRoutineAcceptedUtc;

            if (_lastRoutineAcceptedUtc != DateTime.MinValue
                && since < TimeSpan.FromSeconds(gap)
                && !AnnouncerDensity.WorthBreakingIn(weight, _lastRoutineWeight))
            {
                Load.NoteHeld(Unsaid(UnsaidKind.Held, type, transponder, words,
                    $"Held back by the pacing dial: {AnnouncerDensity.For(Density).Name} leaves {AnnouncerDensity.Gap(gap)} "
                    + $"between calls, and the last one was only {AnnouncerDensity.Gap(since.TotalSeconds)} before."));

                StatusMessage?.Invoke(
                    $"[voice] Held {type} - {AnnouncerDensity.Gap(gap)} between calls at "
                    + $"{AnnouncerDensity.For(Density).Name}, and the last one was "
                    + $"{AnnouncerDensity.Gap(since.TotalSeconds)} ago.");

                paced?.Invoke();

                return false;
            }

            _lastRoutineAcceptedUtc = DateTime.UtcNow;
            _lastRoutineWeight = weight;
        }

        Load.NoteAccepted();

        return true;
    }

    /// <summary>The Weight the club gave a kind of call on the Voice page, or nought for one it has no entry for.</summary>
    private int WeightOf(AnnouncementEventType type) =>
        _voiceStore.Current.Events.GetValueOrDefault(type.ToString())?.Weight ?? 0;

    /// <summary>
    /// The Weight of the line on the speaker right now - what a call has to
    /// clearly outweigh to be kept for the end of it.
    ///
    /// A line with no kind on it, or an instruction (a flag, the finish, a
    /// release, the recap), counts as Commanding: only a call that would
    /// break into anything - a change of lead - is kept behind one of
    /// those. A flag clears the slot anyway (ClearTheDecksFor), so nothing
    /// kept behind a yellow survives it.
    /// </summary>
    private int WeightBeingSpoken()
    {
        AnnouncementEventType? call;

        lock (_speakingLock)
        {
            call = _onAirCall;
        }

        if (call is not { } type || IsPriorityAnnouncement(type) || IsWorthWaitingFor(type))
        {
            return AnnouncerDensity.Commanding;
        }

        return WeightOf(type);
    }

    /// <summary>
    /// A call kept for the end of the current line: what it was, how to
    /// offer it again (true when that offer was taken), and what to run if
    /// it is lost instead (20 Oct 2026, Bug Hunt 8 - see the lost parameter
    /// of RoutineCallGetsTheAir).
    /// </summary>
    private sealed record HeldCall(AnnouncementEventType Type, Func<bool> Reoffer, Action? Lost, string? Transponder = null, string? Words = null);

    /// <summary>
    /// The one call kept back while the speaker was busy - see
    /// <see cref="HeldOver{T}"/> for why it is one and not a queue.
    /// </summary>
    private readonly HeldOver<HeldCall> _heldOver = new();

    /// <summary>
    /// Called by the voice loop each time a line finishes: the kept call,
    /// if there is one and it is still fresh, is offered again - through
    /// the whole of EnqueueVoiceWork, so the race being over, a muted ghost
    /// and the pacing gap all still have their say.
    /// </summary>
    private void OfferTheHeldOverCall()
    {
        var call = _heldOver.Take(out var stale);

        if (stale is not null)
        {
            Load.NoteDropped(Unsaid(UnsaidKind.Lost, stale.Type, stale.Transponder, stale.Words,
                "It was waiting for the end of a line, but that line ran so long the moment had passed."));
            stale.Lost?.Invoke();
            StatusMessage?.Invoke($"[voice] Dropped {stale.Type} - kept for the end of a line, but the line ran too long and the moment has gone.");
        }

        // A kept call turned down on its second chance (speaker busy again,
        // the gap, a muted ghost, the race over) is lost too, and gives back
        // what it was holding (20 Oct 2026, Bug Hunt 8).
        if (call is not null && !call.Reoffer())
        {
            call.Lost?.Invoke();
        }
    }

    /// <summary>
    /// A call coming back from being kept that a rule before the gate then
    /// refused (a ghost now muted, the race now over) - counted as lost, or
    /// AnnouncerLoad would carry an Offered with nothing to match it for the
    /// rest of the race.
    /// </summary>
    /// <param name="type">The call, for the lost-lines window.</param>
    /// <param name="transponder">Who it was about.</param>
    /// <param name="words">What it would have said.</param>
    /// <param name="why">Why it went, finishing "It was waiting for the end of a line, then ...".</param>
    private void NoteKeptCallLost(bool heldOver, AnnouncementEventType type, string? transponder, string? words, string why)
    {
        if (heldOver)
        {
            Load.NoteDropped(Unsaid(UnsaidKind.Lost, type, transponder, words, $"It was waiting for the end of a line, then {why}."));
        }
    }

    /// <summary>
    /// A flag, the countdown or a new race: a kept call is no longer true
    /// and goes, counted as lost like the queued lines the flag bins.
    /// </summary>
    private void ForgetTheHeldOverCall(bool count)
    {
        if (_heldOver.Clear() is not { } gone)
        {
            return;
        }

        // Whatever count says - a kept call that will never be said gives
        // back what it was holding (20 Oct 2026, Bug Hunt 8). After a reset
        // the hand-backs themselves do nothing, so this is safe then too.
        gone.Lost?.Invoke();

        if (count)
        {
            Load.NoteDropped(Unsaid(UnsaidKind.Lost, gone.Type, gone.Transponder, gone.Words,
                "It was waiting for the end of a line, then a flag, the countdown or a new race cleared it."));
            StatusMessage?.Invoke($"[voice] Dropped {gone.Type} - it was waiting for the end of a line and the race moved on.");
        }
    }

    /// <summary>
    /// One unsaid call for Race Control's lost-lines window (20 Oct 2026):
    /// the call's name, the driver's, and its words with the delivery tags
    /// taken off - "[excited] Sam lowers the best sector" reads as a fault
    /// in a list somebody is reading.
    /// </summary>
    private UnsaidLine Unsaid(UnsaidKind kind, AnnouncementEventType type, string? transponder, string? words, string why)
    {
        string? driver = null;

        try
        {
            driver = string.IsNullOrWhiteSpace(transponder) ? null : DisplayNameFor(transponder);
        }
        catch
        {
            // A name is a nicety here; the line is still listed without one.
        }

        var said = string.IsNullOrWhiteSpace(words)
            ? null
            : VoiceProfile.StripAudioTags(words).Trim();

        return new UnsaidLine(DateTime.UtcNow, kind, CallName(type), string.IsNullOrWhiteSpace(driver) ? null : driver,
            string.IsNullOrWhiteSpace(said) ? null : said, why);
    }

    /// <summary>A call's name as the Voice page gives it, without the settings wording in brackets - "Fast lap", not "Fast lap (race best)". The same trim OnAirNow makes.</summary>
    internal static string CallName(AnnouncementEventType type)
    {
        var label = VoiceProfile.Describe(type).Label ?? type.ToString();
        var bracket = label.IndexOf(" (", StringComparison.Ordinal);

        return bracket > 0 ? label[..bracket] : label;
    }

    // ------------------------------------------------------------------
    //  The field-wide look-ahead
    // ------------------------------------------------------------------

    /// <summary>The field's per-lap winners, waiting for the window to close. Under _batchLock.</summary>
    private readonly List<(int Weight, Action Enqueue)> _fieldCalls = new();

    /// <summary>Whether a flush of <see cref="_fieldCalls"/> is already on its way. Under _batchLock.</summary>
    private bool _fieldFlushPending;

    /// <summary>
    /// Bumped whenever the waiting calls are binned (a flag, the countdown,
    /// a new race), so a flush already on its way knows its window belonged
    /// to a race that has moved on. Under _batchLock.
    /// </summary>
    private int _fieldGeneration;

    /// <summary>
    /// Hands one car's per-lap voice calls to the FIELD rather than
    /// straight to the speaker, so the best call across every car gets the
    /// air and not merely the first one to arrive.
    ///
    /// THE MISSING HALF OF THE PACING GAP (15 Oct 2026). The gap decides
    /// how OFTEN the announcer talks; this decides WHAT about. Before it,
    /// three cars crossing within half a second of each other were three
    /// calls offered in the order the loop saw them - a lap time from the
    /// car in fifth got the slot, and the pass for the lead a quarter of a
    /// second behind it was held by the gap it had just started. Now all
    /// three wait <see cref="AnnouncerDensity.FieldWindow"/> together and
    /// are offered biggest first, so the gap is spent on the pass and the
    /// lap time is what gets held.
    ///
    /// Nothing changes at Wall to wall, where the announcer is meant to
    /// behave exactly as it did before pacing: calls go straight through.
    /// </summary>
    private void OfferToTheField(IEnumerable<(int Weight, Action Enqueue)> calls)
    {
        if (AnnouncerDensity.GapSeconds(Stature, Density) <= 0)
        {
            foreach (var call in calls)
            {
                call.Enqueue();
            }

            return;
        }

        int generation;

        lock (_batchLock)
        {
            _fieldCalls.AddRange(calls);

            if (_fieldFlushPending)
            {
                return;
            }

            _fieldFlushPending = true;
            generation = _fieldGeneration;
        }

        _ = FlushTheFieldAfterTheWindowAsync(generation);
    }

    private async Task FlushTheFieldAfterTheWindowAsync(int generation)
    {
        // NOT ConfigureAwait(false): back on the thread that offered the
        // calls, which is the UI thread the per-lap batch flushes on - the
        // calls build their lines from the stores and raise the screens'
        // text, and did so from that thread before this window existed.
        await Task.Delay(AnnouncerDensity.FieldWindow);

        List<(int Weight, Action Enqueue)> calls;

        lock (_batchLock)
        {
            if (generation != _fieldGeneration)
            {
                // Binned by a flag or a new race while it waited - and the
                // flush that bin scheduled, if any, is not this one.
                return;
            }

            calls = _fieldCalls.ToList();
            _fieldCalls.Clear();
            _fieldFlushPending = false;
        }

        // Biggest first; equal weights in the order they crossed the loop,
        // which OrderByDescending keeps. The first through the gate starts
        // it, and the gate then holds the rest - unless one of them clearly
        // outweighs the first, which by this order none can.
        foreach (var call in calls.OrderByDescending(c => c.Weight))
        {
            try
            {
                call.Enqueue();
            }
            catch (Exception ex)
            {
                // One bad line must not cost the field the rest - this is a
                // fire-and-forget task, where nothing would ever see the
                // throw.
                StatusMessage?.Invoke($"[voice] A lap call failed to queue: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The field's waiting calls go - a flag, the countdown or a new race.
    /// Called under _batchLock by every place that already bins the
    /// per-lap batches (a flag, the countdown, Stop, a reset - 17 Oct 2026,
    /// Bug Hunt 8, A06-5), so the two can never disagree.
    /// </summary>
    private void BinTheFieldCallsUnderLock()
    {
        _fieldCalls.Clear();
        _fieldFlushPending = false;
        _fieldGeneration++;
    }

    /// <summary>
    /// A new race: the count goes and so does the pacing clock.
    ///
    /// CALLED FROM STAGING, which happens once per race before the
    /// countdown on both the real start sequence and the demo's copy of
    /// it. A club that presses green from Idle with no staging at all
    /// carries the last race's figures into this one - said here rather
    /// than guarded against, because this is a diagnostic a club glances
    /// at between races and not a record anything depends on.
    ///
    /// The clock is reset with it for the reason
    /// <see cref="_lastRoutineAcceptedUtc"/> gives: a race must not open
    /// with the announcer holding its first line back because of something
    /// it said in the last one.
    /// </summary>
    private void ResetAnnouncerLoad()
    {
        Load.Reset();

        // And where each line's time went, for the same race (10 Nov 2026).
        Timings.Reset();

        // Not counted: the count it would go into has just been cleared,
        // and a call kept from the last race is nothing this race offered.
        ForgetTheHeldOverCall(count: false);

        lock (_pacingLock)
        {
            _lastRoutineAcceptedUtc = DateTime.MinValue;
            _lastRoutineWeight = 0;
        }
    }
}
