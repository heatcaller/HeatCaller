namespace HeatCaller;

/// <summary>
/// MainForm's side of the recap's broadcast (27 Sep 2026): what the Garage
/// has on the winning car, for the mechanic's interview, and the voices the
/// Commentary page offers the mechanic. Here because both need stores and
/// voice clients only MainForm holds.
/// </summary>
public partial class MainForm
{
    /// <summary>A line for the Test Voice button beside the mechanic's voice - the kind of thing the mechanic says.</summary>
    private const string MechanicTestLine = "Thanks! We went up a tooth on the pinion before the main, and you could see it down the straight.";

    /// <summary>
    /// And one for the honours reader's button - the kind of thing THEY
    /// say, which is a different job.
    ///
    /// Its own line and not the mechanic's: a club is listening for whether
    /// this voice sounds right reading out names and badges, and a voice
    /// tested on a sentence about pinion gears tells them nothing about
    /// that. See RaceHonours for where the real wording comes from.
    /// </summary>
    private const string HonoursTestLine = "Billy Bob picks up Nail-Biter. Jane Doe takes Personal Best and Hat-Trick.";

    /// <summary>Hooks the recap's broadcast up to Race Control and the Commentary page. Called once, when both exist.</summary>
    private void WireRecapBroadcast(RaceControlControl raceControl, CommentaryControl commentary)
    {
        raceControl.MechanicSetupFacts = MechanicSetupFactsFor;

        commentary.EngineInUse = () => _voiceStore.Current.EffectiveEngine;
        commentary.LoadVoices = MechanicVoiceChoicesAsync;

        // Heard even while the announcer is muted - it was asked for by hand.
        commentary.SpeakAsMechanic = voice => _announcer.SpeakTest(MechanicTestLine, _voiceStore.Current.Speed, voice);
        commentary.SpeakAsHonours = voice => _announcer.SpeakTest(HonoursTestLine, _voiceStore.Current.Speed, voice);
    }

    /// <summary>
    /// Hooks the show's music up to the Commentary page.
    ///
    /// The page chooses the files; the announcer screen owns the treatment
    /// that plays them, because that is where a segment is spoken. Neither
    /// can reach the other, so both go through here.
    /// </summary>
    private static void WireShowMusic(AnnouncerControl announcer, CommentaryControl commentary)
    {
        commentary.TestShowMusic = announcer.TestShowMusicAsync;

        // The loaded audio is shared (ShowMusic), so this is really telling
        // the announcer screen to drop it - said explicitly rather than
        // relied on, because "the page clears a cache the announcer happens
        // to share" is exactly the kind of thing that stops being true.
        commentary.ShowMusicChanged = announcer.ReloadShowMusic;
    }

    /// <summary>
    /// Hands the relay's own count of the club's month to the budget that
    /// paces the night against it.
    ///
    /// The director is fetched when the count arrives rather than held: it
    /// is built on first use and may not exist when this is wired.
    /// </summary>
    private static void WireCommentaryAllowance(AnnouncerControl announcer, CommentaryControl commentary) =>
        commentary.AllowanceSeen += (spent, cap) => announcer.Director?.MonthlyUsage(spent, cap);

    /// <summary>
    /// Hands the director the readers it cannot reach on its own.
    ///
    /// Each one is read fresh when the segment is considered, not held:
    /// a driver's record changes as the night goes on, and a championship
    /// changes with every result filed. All of it is arithmetic over
    /// stores already open, so it costs nothing to ask and is only asked
    /// when a segment is actually being weighed up.
    ///
    /// A null reader means that segment says nothing - see
    /// CommentaryDirector - so this is also the list of what is wired.
    /// </summary>
    private void WireCommentaryReaders(CommentaryDirector? director)
    {
        if (director is null)
        {
            return;
        }

        // The sponsors who have been sold a read, and where the club wants
        // them read. Read fresh every time it is asked rather than captured,
        // so a sponsor sold at the sign-up desk is in the rotation by the
        // next gap - see SponsorStore.ReadPlan.
        director.SponsorReads = () => _sponsors.ReadPlan();

        // Who is on track, and the class their record should be counted
        // over. Taken from the session rather than the programme: it is
        // the drivers actually staged, which is what the intro is about.
        //
        // TWO SEGMENTS READ THIS ONE: the grid history and, since 10 Oct
        // 2026, the form guide. A form guide is "how the drivers in a main
        // have been going lately", which is the recent finishes this
        // already counts for this grid in this class - so a reader of its
        // own would walk the same history on the same tick for the same
        // numbers, and the two could disagree after a result was corrected
        // mid-evening. What differs is what each SAYS; see
        // CommentaryDirector's FormGuide case.
        director.GridHistory = () => CommentaryReaders.GridHistory(
            _race.GetOrderedStandings().Select(s => s.TransponderId).ToList(),
            _activeRound?.ClassName,
            _profiles,
            _sessionHistory,
            _rounds);

        // The club's own qualifying method, not the calculator's default -
        // a report that ranks by total laps at a club that qualifies on a
        // consecutive-lap window would read out the wrong grid.
        director.Qualifying = () => CommentaryReaders.Qualifying(
            _activeRound?.ClassName ?? string.Empty,
            _rounds,
            _sessionHistory,
            _profiles,
            ClassOnTrack(),
            // Tonight's rounds and no others, exactly as the programme tab
            // does it (RaceProgramControl.QualifyingRoundFilter). Left off,
            // the calculator's roundFilter is null - which to it means
            // "count every recorded round" - so the report ranked tonight's
            // drivers against every heat the class had ever run and put a
            // driver who set a quick one in August, and is not even here,
            // on provisional pole. The 25 Sep audit closed that everywhere
            // else and missed this call.
            _raceEvents.QualifyingScope());

        // Where the class on track stands partway through its heats.
        //
        // The halfway standings had a title, a tier, a budget and a tick
        // box on the Commentary page and no reader at all, so a club that
        // ticked it got silence for ever and nothing said why. Same scope
        // and same round filter as the qualifying report above, for the
        // same two reasons: tonight's rounds and no others (see the
        // August-pole note there), and the club's own qualifying method
        // rather than the calculator's default - a provisional order that
        // disagreed with the grid sheet by a place would be worse than no
        // segment at all.
        director.MidMeeting = () => CommentaryReaders.MidMeeting(
            _activeRound?.ClassName,
            _rounds,
            _sessionHistory,
            _profiles,
            _raceEvents.ActiveRoundScope(),
            ClassOnTrack(),
            _raceEvents.QualifyingScope());

        // What the club did on this date in other years - the third
        // segment that had a tick box and no reader.
        //
        // Deliberately NOT scoped to tonight's rounds: the whole point of
        // it is the other years. Read fresh like the rest of these, which
        // costs one walk of a history this laptop already has open, and
        // comes back empty for a club in its first year - which is most
        // clubs that have just installed this, and is the right answer.
        director.OnThisDay = () => CommentaryReaders.OnThisDay(_sessionHistory);

        // How far through tonight's programme we are, which is what the
        // budget releases the night's allotment against. Counted from the
        // rounds themselves rather than from a tally kept as the night
        // runs, so a laptop closed between the heats and the mains comes
        // back knowing where it was.
        director.NightProgress = () =>
        {
            // Null from ActiveRoundScope means "don't filter" - the legacy
            // holder, where every round is tonight's.
            var scope = _raceEvents.ActiveRoundScope();

            var tonight = scope is null
                ? _rounds.All
                : _rounds.All.Where(r => scope.Contains(r.Id)).ToList();

            return (tonight.Count(r => r.IsRecorded), tonight.Count);
        };

        // What is left on tonight's programme, and what has just run. These
        // two are what the show's continuity is built from - "that was the
        // B-main; still to come, the A-main" - and they were the reason none
        // of it could speak: CommentaryContinuity, ComingUpNext, the cold
        // open and the sign-off all read them, and nothing had ever wired
        // them up, so every one of those was silent in the field.
        //
        // The same scope as NightProgress above, for its reasons.
        director.Upcoming = () => CommentaryReaders.Upcoming(
            _rounds,
            _raceEvents.ActiveRoundScope(),

            // No estimate yet: there is no "minutes until the next race" in
            // this app to hand over, and CommentaryFacts.ComingUpNext's rule
            // is that the estimate is given, never worked out here. The
            // lines simply drop their "about four minutes away" tail, and a
            // block fits against no limit rather than a wrong one.
            inAbout: null,

            // The round in hand, once its race has finished, is skipped:
            // between the flag and the result being filed it is still
            // "unrecorded", and "still to come, Heat 2" about the heat the
            // room has just watched is the worst thing continuity can say.
            skipRoundId: _race.State == RaceState.Finished ? _activeRound?.Id : null);

        director.JustRan = () => CommentaryReaders.JustRan(
            _rounds,
            _sessionHistory,
            _raceEvents.ActiveRoundScope());

        director.Championship = () => CommentaryReaders.Championship(
            ChampionshipLive.Compute(
                LastFiledResults(),
                _activeRound,
                _seasonStore,
                _rounds,
                _raceEvents,
                _sessionHistory,
                _profiles));
    }

    /// <summary>The class the active round belongs to, for the qualifying method and its lap counts.</summary>
    private RaceClass? ClassOnTrack()
    {
        var name = _activeRound?.ClassName;

        return string.IsNullOrWhiteSpace(name)
            ? null
            : _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The results of the last race filed, which is what the championship
    /// check is about.
    ///
    /// The saved record rather than the live standings: the check runs
    /// after a class's last main, by which point the race is over and
    /// filed, and the live session may already have been reset for the
    /// next one.
    /// </summary>
    private IReadOnlyList<SessionResultEntry> LastFiledResults()
    {
        var all = _sessionHistory.All;

        for (var i = all.Count - 1; i >= 0; i--)
        {
            if (all[i].SessionType != RaceSessionType.Practice && all[i].Results.Count > 0)
            {
                return all[i].Results;
            }
        }

        return Array.Empty<SessionResultEntry>();
    }

    /// <summary>
    /// The setup lines for the winner of a saved race - see MechanicBrief.
    /// Asked by Race Control on the UI thread, where the Garage's stores
    /// are read.
    /// </summary>
    private IReadOnlyList<string> MechanicSetupFactsFor(SessionRecord record, RoundDefinition? round)
    {
        var winner = record.Results.OrderBy(r => r.Position).FirstOrDefault();

        if (winner is null)
        {
            return Array.Empty<string>();
        }

        var driverName = _profiles.Get(winner.Transponder)?.DriverName;

        if (string.IsNullOrWhiteSpace(driverName))
        {
            driverName = winner.DriverName;
        }

        // The race day it was run on, for a car lent for that day: the
        // round's own day, or the day in hand for a race that isn't a round.
        var eventId = (round is null ? null : _raceEvents.EventForRound(round.Id)?.Id) ?? _raceEvents.Active?.Id;

        return MechanicBrief.SetupLines(
            winner.Transponder,
            driverName ?? string.Empty,
            round?.ClassName,
            eventId,
            record.EndedAtUtc,
            _cars.All,
            _setupLog.All,
            _chassisTemplates.Get);
    }

    /// <summary>
    /// The voices an engine offers, for the mechanic's list on the
    /// Commentary page. Throws with a sentence the page can show when it
    /// can't list them.
    /// </summary>
    private async Task<IReadOnlyList<CommentaryControl.VoiceChoice>> MechanicVoiceChoicesAsync(VoiceEngine engine)
    {
        switch (engine)
        {
            case VoiceEngine.Kokoro:
            {
                // Read from the names of the files beside the exe - nothing
                // to download or ask for, so this can't fail.
                return KokoroSpeech.InstalledVoices()
                    .Select(v => new CommentaryControl.VoiceChoice(v.Name, v.Name, string.Empty, v.Display))
                    .ToList();
            }

            case VoiceEngine.HeatCaller:
            {
                var catalogue = await _heatCallerVoice.CatalogueAsync();

                if (!catalogue.Enabled)
                {
                    throw new InvalidOperationException("the HeatCaller voice isn't included for this club yet");
                }

                return catalogue.Voices
                    .Select(v => new CommentaryControl.VoiceChoice(v.Id, v.Name, v.Rev, v.ToString()))
                    .ToList();
            }

            default:
            {
                var key = _voiceStore.Current.ApiKey;

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("there's no ElevenLabs API key on the Voice Settings page");
                }

                var voices = await new ElevenLabsClient().GetVoicesAsync(key);

                return voices
                    .Select(v => new CommentaryControl.VoiceChoice(v.VoiceId, v.Name, string.Empty, v.Name))
                    .ToList();
            }
        }
    }
}
