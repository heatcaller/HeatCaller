namespace HeatCaller;

/// <summary>
/// The caller the record announcements have been waiting for.
///
/// WHAT WAS WRONG (15 Oct 2026)
///
/// <c>NotifyTrackRecord</c> and <c>NotifyRecordPace</c> have existed for
/// as long as the voice settings have. Both have a tick box on the Voice
/// page, a weight, a set of templates, and a description that promises
/// exactly when they fire. NEITHER WAS EVER CALLED BY ANYTHING - a grep of
/// the whole app and the whole test project finds no caller for either.
///
/// So "Track record" and "On record pace" were two switches a club could
/// turn on and off that did nothing at all, and the biggest live moment
/// the app can detect - the all-time record for the track going - has
/// never once been announced.
///
/// NotifyRecordPace's own doc comment says why: "the caller has already
/// decided this is worth saying (once per driver per race, and not before
/// their third counted lap)". That caller is what was missing, and the
/// rules it describes were only ever written down there. This file is that
/// caller, and <see cref="RecordWatch"/> is where those rules now live for
/// real.
///
/// The same gap runs one layer up: PaceWatch's LayoutRecord, NightBest and
/// PersonalBestBefore callbacks are assigned ONLY in PaceWatchTests, never
/// in the app, so the AI commentary cannot say what a lap beat or who held
/// it either. Wiring those is the matching half of this and is written up
/// in the ROADMAP's open issues.
///
/// WHY IT IS HERE AND NOT ON THE DIRECTOR. This is a per-event call off
/// the timing loop, like the fast lap it sits beside in HandleGatePassed -
/// not a written segment. It costs nothing, needs no model, and must land
/// within a second of the crossing. The director's record SEGMENT (a
/// paid-for line with history in it) is a different thing and still to
/// come.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// The all-time record for the layout the race is on, and who holds
    /// it, or null when no layout is selected or none has been set.
    ///
    /// A CALLBACK, like every other thing this class knows about the race
    /// around it - see ChatterProvider. The record lives in
    /// TrackLayout.RecordLapMs, the selected layout is a name in settings
    /// that a club can change between races, and this class has no
    /// business knowing either.
    ///
    /// Null means NO RECORD IS CLAIMED rather than a guess at one, which
    /// is the rule NightMoments.ForRace states for the same figure: a
    /// layout with no mark standing on it has nothing to be close to, and
    /// the two calls below simply never fire.
    /// </summary>
    public Func<(double Seconds, string Holder)?>? LayoutRecordProvider { get; set; }

    private RecordWatch? _records;

    /// <summary>
    /// Built on first use rather than in a field initializer, because the
    /// watch's own callback has to close over this instance and a field
    /// initializer cannot.
    /// </summary>
    private RecordWatch Records =>
        _records ??= new RecordWatch { LayoutRecord = () => LayoutRecordProvider?.Invoke() };

    /// <summary>
    /// Which drivers have already had the record called for them this race
    /// - see <see cref="CheckTheRecord"/> on why the guard is needed at
    /// all.
    ///
    /// MARKED BEFORE THE LINE IS KNOWN TO BE ACCEPTED, unlike the pace
    /// call beside it, and the difference is in the queue's own lists:
    /// TrackRecord is on IsWorthWaitingFor, so it waits its turn rather
    /// than being dropped behind chatter or held by the pacing gap. A flag
    /// clearing the queue can still lose one, and then it is not called
    /// again - accepted deliberately, because the alternative is the
    /// eight-times-a-race fault this guard exists to stop, and because a
    /// record announced four laps late is worse than one missed.
    /// </summary>
    private readonly HashSet<string> _recordCalledFor = new(StringComparer.Ordinal);


    /// <summary>
    /// A lap has come in. Calls the record if it has gone, or the build-up
    /// to it if this driver's PACE says it might.
    ///
    /// ONCE PER DRIVER PER RACE, FOR BOTH, and the guard is not belt and
    /// braces. The club's stored record does not change mid-race - it is
    /// written when the result is filed - so a driver two tenths under it
    /// beats the STORED figure again on every lap for the rest of the
    /// race. Without the guard the first record of the night would be
    /// announced eight times.
    ///
    /// THE PACE CALL NEEDS THE DOORSTEP, not merely closing. RecordWatch
    /// reports three levels; this route takes the top two. The Voice page
    /// promises a call "within two percent" of the record, and
    /// RecordWatch's doorstep band is one and a half percent - so the
    /// promise is kept, and the mildest level (five percent and still
    /// improving) is left for a written segment that can afford to be
    /// chattier than a PA call.
    ///
    /// AND IT KEEPS NO GUARD OF ITS OWN, which is a correction to the
    /// first draft of this file. That draft marked the driver as called
    /// the moment the watch reported a threat - before knowing whether the
    /// line was accepted. A pace call weighs 88, which is under
    /// AnnouncerDensity.Commanding, so the pacing gap can hold one; held,
    /// it would have been marked as said and never offered again. That is
    /// exactly the fault CheckCloseBattles records for itself: "it used to
    /// be recorded first, and the line could then say nothing at all...
    /// that pair was then never called again."
    ///
    /// So RecordWatch's own escalation is the only guard, which means at
    /// most TWO pace calls for a driver in a race - once when their pace
    /// reaches the record's doorstep, and once if they then match it. Two
    /// genuinely different sentences, and the Voice page's description says
    /// so rather than promising one.
    ///
    /// AND NOT FOR A GHOST, like every other line about a car that is not
    /// there.
    /// </summary>
    private void CheckTheRecord(string? transponder, RaceStanding? standing, long? lapTimeMs)
    {
        if (transponder is null || standing is null || lapTimeMs is not > 0 || MutedGhost(transponder))
        {
            return;
        }

        if (LayoutRecordProvider?.Invoke() is not { Seconds: > 0 } mark)
        {
            return;
        }

        var seconds = lapTimeMs.Value / 1000.0;

        // Fed either way, and BEFORE the record-has-gone branch returns, so
        // the watch stands this driver down for the rest of the race - the
        // story from there is the record itself, not the build-up to it.
        var threat = Records.Saw(new LapSeen(transponder, seconds));

        if (seconds < mark.Seconds)
        {
            if (_recordCalledFor.Add(transponder)
                && VoiceCandidateCanSpeak(
                    AnnouncementEventType.TrackRecord,
                    _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.TrackRecord))))
            {
                StatusMessage?.Invoke(
                    $"[record] {RaceNumbers.LapSpoken(lapTimeMs.Value)} beats the layout record of "
                    + $"{RaceNumbers.LapSpoken((long)Math.Round(mark.Seconds * 1000))}"
                    + $"{(string.IsNullOrWhiteSpace(mark.Holder) ? string.Empty : $", held by {mark.Holder}")}.");

                NotifyTrackRecord(standing);
            }

            return;
        }

        if (threat is not { Level: RecordThreatLevel.OnTheDoorstep or RecordThreatLevel.OneMoreLikeThat })
        {
            return;
        }

        if (!VoiceCandidateCanSpeak(
                AnnouncementEventType.RecordPace,
                _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.RecordPace))))
        {
            return;
        }

        // The watch's own sentence in the log, which is the richer one: it
        // names what the pace is against and what is still to find, where
        // the spoken template has only the lap time to work with. The
        // written segment this will eventually feed is what gets to say it
        // out loud.
        StatusMessage?.Invoke($"[record] {RecordWatch.Say(threat, DisplayNameFor(transponder))}");

        NotifyRecordPace(standing, lapTimeMs.Value);
    }

    /// <summary>
    /// A new race. The guard and the watch go.
    ///
    /// The watch keeps nothing about the layout - that is read fresh
    /// through the callback every time, and belongs to the store behind it.
    /// </summary>
    private void ResetRecordWatch()
    {
        _records?.Reset();
        _recordCalledFor.Clear();
    }
}
