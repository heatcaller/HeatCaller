namespace HeatCaller;

/// <summary>
/// The pass for the lead, made ready while it is still about to happen (11
/// Nov 2026, voice speed plan, phase 4).
/// </summary>
/// <remarks>
/// WHY. A change of lead is the call the room most wants straight away, and
/// when it came with the speaker free it was rendered then - most of a
/// second of silence before "Terry Byte takes the lead!", while the cars were
/// already through the first corner. The timing only knows the order at the
/// line, so the pass itself can't be seen coming between crossings - but a
/// car within half a second of the leader at the line is the car most likely
/// to be in front at the next one.
///
/// SO ITS WORDS ARE WORKED OUT NOW, EXACTLY. LeadTracker.Preview says what
/// the change would be (which change it is, who had it, how many have led);
/// the template, and the lead-change tail, are chosen now and kept for that
/// driver; and EnqueueVoice is run as a dry run (_dryRun) to build the line
/// from them - the same code the real call runs, so the same words. That
/// line is rendered in the background. When the lead does change to that
/// driver as predicted, the real call takes the kept template and tail, its
/// words come out the same, and the render is handed over (OfferRenderedAhead).
/// Anything different - another driver, another count, a name the time
/// changed - and the call is rendered then, as always.
///
/// A GUESS COSTS A RENDER. At most MostPredictionsPerRace a race, one per
/// driver per change, and the race's timing summary counts them with the
/// rest of the early renders. A prediction goes when the chaser falls back.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>How close to the leader at the line a car must be for its pass to be made ready.</summary>
    internal const long PredictWithinMs = 500;

    /// <summary>The most passes made ready in one race - each is a render, used or not.</summary>
    internal const int MostPredictionsPerRace = 6;

    /// <summary>Set while EnqueueVoice is being run only for the words it would say - see the remarks. One thread at a time.</summary>
    [ThreadStatic]
    private static DryRunWords? _dryRun;

    private sealed class DryRunWords
    {
        public string? Text { get; set; }

        public double Speed { get; set; }
    }

    /// <summary>A pass made ready: whose, the change it would be, the words chosen for it, and its render once started.</summary>
    private sealed record PredictedPass(
        string Transponder, LeadChange Change, string Template, string Tail, string? Text = null, string? Key = null, Task<SynthesisResult>? Render = null);

    private readonly object _passesLock = new();

    /// <summary>The passes made ready, by driver. Under _passesLock.</summary>
    private readonly Dictionary<string, PredictedPass> _predictedPasses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pass that has just happened as predicted, until its call takes its template. Under _passesLock.</summary>
    private PredictedPass? _passBeingCalled;

    private int _predictionsThisRace;

    /// <summary>
    /// The template kept for this driver's lead change, when there is one and
    /// it is still among the lines the call may pick from - else null. A real
    /// call takes it once; a dry run only looks.
    /// </summary>
    private string? KeptTemplateFor(AnnouncementEventType type, string? transponder, IReadOnlyList<string> candidates)
    {
        if (type != AnnouncementEventType.LeaderChange || transponder is null)
        {
            return null;
        }

        lock (_passesLock)
        {
            if (_passBeingCalled is { } calling && string.Equals(calling.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                if (_dryRun is null)
                {
                    _passBeingCalled = null;
                }

                return candidates.Contains(calling.Template) ? calling.Template : null;
            }

            return _dryRun is not null && _predictedPasses.TryGetValue(transponder, out var predicted) && candidates.Contains(predicted.Template)
                ? predicted.Template
                : null;
        }
    }

    /// <summary>
    /// Called on every standings update, after the lead has been looked at:
    /// makes the chaser's pass ready when they are within PredictWithinMs of
    /// the leader, and lets go of passes no longer close.
    /// </summary>
    private void PredictTheLeadChange(IReadOnlyList<RaceStanding> ordered)
    {
        try
        {
            if (IsPracticeMode || IsQualifyingMode || Muted || ordered.Count < 2)
            {
                return;
            }

            var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.LeaderChange));

            if (!VoiceCandidateCanSpeak(AnnouncementEventType.LeaderChange, config))
            {
                return;
            }

            var leader = ordered[0];
            var chaser = ordered[1];
            var gapMs = chaser.Laps >= 2 && GapBetween(chaser, leader) is { LapsDown: 0, GapMs: { } gap } ? gap : 0;
            var close = gapMs > 0 && gapMs <= PredictWithinMs;

            lock (_passesLock)
            {
                // Only the car right behind the leader now is worth keeping ready.
                foreach (var gone in _predictedPasses.Keys.Where(t => !close || !string.Equals(t, chaser.Transponder, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    _predictedPasses.Remove(gone);
                }
            }

            if (!close || _leads.Preview(chaser.Transponder) is not { } change)
            {
                return;
            }

            lock (_passesLock)
            {
                if (_predictedPasses.TryGetValue(chaser.Transponder, out var already) && already.Change == change)
                {
                    return;
                }

                if (_predictionsThisRace >= MostPredictionsPerRace)
                {
                    return;
                }
            }

            // The template and tail now, as EnqueueVoice would choose them now.
            var candidates = (IReadOnlyList<string>)config!.EffectiveTemplates;

            if (candidates.Count == 0)
            {
                return;
            }

            if (TrackIsBusy() && candidates.Count > 1)
            {
                candidates = BusyWording.ShorterHalf(candidates);
            }

            var template = candidates.Count == 1 ? candidates[0] : TemplateBag.Pick(candidates);
            var tail = config.IncludeLeadChangeCount ? LeadTracker.Tail(change) : string.Empty;

            lock (_passesLock)
            {
                _predictedPasses[chaser.Transponder] = new PredictedPass(chaser.Transponder, change, template, tail);
            }

            var words = new DryRunWords();
            _dryRun = words;

            try
            {
                EnqueueVoice(AnnouncementEventType.LeaderChange, chaser, null, extras: LeadChangeExtras(change),
                    autoTail: tail.Length > 0 ? ("{leadchanges}", tail) : null);
            }
            finally
            {
                _dryRun = null;
            }

            if (words.Text is not { } text)
            {
                lock (_passesLock)
                {
                    _predictedPasses.Remove(chaser.Transponder);
                }

                return;
            }

            lock (_passesLock)
            {
                _predictionsThisRace++;
                _predictedPasses[chaser.Transponder] = new PredictedPass(chaser.Transponder, change, template, tail, text);
            }

            var transponder = chaser.Transponder;
            var speed = words.Speed;

            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(() => MakeThePassReady(transponder, change, text, speed));
            }

            StatusMessage?.Invoke($"[voice] {DisplayNameFor(transponder)} is {VoiceLineTiming.S(gapMs)} behind the leader - their pass is being made ready.");
        }
        catch (Exception ex)
        {
            // A head start, never a reason a lap is lost.
            StatusMessage?.Invoke($"[voice] Couldn't make a pass ready ({ex.Message}).");
        }
    }

    /// <summary>Renders a predicted pass's words, and notes the render against it if it is still the one kept.</summary>
    private void MakeThePassReady(string transponder, LeadChange change, string text, double speed)
    {
        try
        {
            if (LineAsItWillBeAsked(text, AnnouncementEventType.LeaderChange, speed) is not { } line)
            {
                return;
            }

            var render = RenderLineAsync(line.Profile, line.Text);
            _ = render.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            Timings.NoteRenderedAhead();

            lock (_passesLock)
            {
                if (_predictedPasses.TryGetValue(transponder, out var kept) && kept.Change == change && kept.Text == text)
                {
                    _predictedPasses[transponder] = kept with { Key = line.Key, Render = render };
                }
                else if (_passBeingCalled is { } calling && calling.Transponder == transponder && calling.Change == change)
                {
                    // The pass came while this was starting: handed over now.
                    OfferRenderedAhead(line.Key, render);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] Couldn't make a pass ready ({ex.Message}).");
        }
    }

    /// <summary>
    /// The lead has just changed: the pass made ready for it, when it is this
    /// one - its render handed over, its template kept for the call - else
    /// null. Every other prediction goes.
    /// </summary>
    private PredictedPass? TakeThePredictedPass(string newLeader, LeadChange? change)
    {
        lock (_passesLock)
        {
            PredictedPass? hit = null;

            if (change is not null && _predictedPasses.TryGetValue(newLeader, out var predicted) && predicted.Change == change)
            {
                hit = predicted;
            }

            _predictedPasses.Clear();
            _passBeingCalled = hit;

            if (hit is { Key: { } key, Render: { } render })
            {
                OfferRenderedAhead(key, render);
            }

            return hit;
        }
    }

    /// <summary>A new race, a flag: no pass made ready is about what comes next.</summary>
    private void ForgetThePredictedPasses(bool newRace)
    {
        lock (_passesLock)
        {
            _predictedPasses.Clear();
            _passBeingCalled = null;

            if (newRace)
            {
                _predictionsThisRace = 0;
            }
        }
    }
}
