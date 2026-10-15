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
        raceControl.RecapCarFacts = RecapCarFactsFor;

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

        // The night in four minutes - written, played once and kept in
        // tonight's folder. See NightRecap.
        commentary.PlayTonightsRecap = announcer.PlayTonightsRecapAsync;

        // The loaded audio is shared (ShowMusic), so this is really telling
        // the announcer screen to drop it - said explicitly rather than
        // relied on, because "the page clears a cache the announcer happens
        // to share" is exactly the kind of thing that stops being true.
        commentary.ShowMusicChanged = announcer.ReloadShowMusic;
    }

    /// <summary>
    /// Hooks up a driver's own night and the proof of a line (12 Oct 2026).
    ///
    /// NOT IN WireShowMusic above, which is static and so cannot see the
    /// stores: what counts as "tonight" is TonightAtTheTrack's question and
    /// it needs the history AND the rounds to answer it, and the one fact
    /// that needs a store rather than a result - a lap record - needs the
    /// layouts. All three live here.
    ///
    /// EVERY ONE READ FRESH WHEN IT IS ASKED, never captured, which is the
    /// rule every other reader on this page follows: a club runs races
    /// between opening the driver picker and opening it again.
    /// </summary>
    private void WireTheDriversNight(AnnouncerControl announcer, CommentaryControl commentary)
    {
        // TONIGHT'S RACES, through TonightAtTheTrack rather than filtered
        // again here. Its night runs 6am to 6am and it is the same answer
        // every pace figure on the page is read from; a second answer to
        // "which races were tonight" is the sort of thing that agrees all
        // season and then disagrees about a race that started at five to
        // midnight.
        // ONE ANSWER, AND IT LIVES IN ONE PLACE (13 Oct 2026). This used
        // to hold its own copy of the TonightAtTheTrack call, and then a
        // driver's night needed the same list on the relay side - so the
        // copy moved to MainForm.Relay.RacesTonight and both read it. Two
        // answers to "which races were tonight" is the sort of thing that
        // agrees all season and then disagrees about a race that started at
        // five to midnight.
        announcer.RacesTonight = RacesTonight;

        // The layouts, for a record falling. Null would mean no record is
        // claimed rather than a guess at one - see NightMoments.ForRace.
        announcer.Layouts = () => _trackLayouts;

        // WHAT THE NIGHT COST THAT THE CLUB CANNOT SEE (13 Oct 2026). Two
        // ledgers that were being kept and read by nobody. Both are hooks
        // because both live somewhere else - the write-ahead on Race
        // Control, beside the thing that writes the intros, and the
        // listening queue on the relay publisher - and an announcer page
        // that knew how to reach either would be a second path between
        // pages that already have one.
        //
        // Null-safe on the Race Control side because the pages are built in
        // an order this file does not fix: a recap asked for before Race
        // Control exists reads nothing rather than throwing.
        announcer.IntrosWrittenAhead = () => _raceControlControl?.IntrosWrittenAhead();
        announcer.ListenedAlong = () => _relayPublisher.Listening.Report();

        // WHO RACED TONIGHT, for the picker - in the order they last
        // raced, so the drivers from the main a club has just run are at
        // the top rather than whoever is alphabetically first. Named as
        // their results name them, because that is what the piece is
        // written from and a picker that says something else is a picker
        // that picks the wrong person.
        commentary.DriversTonight = () =>
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_rounds is null)
            {
                return Array.Empty<(string, string)>();
            }

            foreach (var (session, _) in TonightAtTheTrack
                         .Races(_sessionHistory.All, id => _rounds.Get(id), DateTime.Now)
                         .OrderByDescending(one => one.Session.StartedAtUtc))
            {
                foreach (var row in session.Results)
                {
                    if (string.IsNullOrWhiteSpace(row.Transponder) || GhostService.IsGhost(row.Transponder))
                    {
                        continue;
                    }

                    // Their profile's name where they have one, so the
                    // picker and the piece agree even for a driver whose
                    // result was saved under an older spelling.
                    var named = _profiles.Get(row.Transponder)?.DriverName is { Length: > 0 } onFile
                        ? onFile
                        : row.DriverName;

                    if (!string.IsNullOrWhiteSpace(named))
                    {
                        seen.TryAdd(row.Transponder, named.Trim());
                    }
                }
            }

            return seen.Select(one => (one.Key, one.Value)).ToList();
        };

        commentary.PlayADriversNight = announcer.PlayADriversNightAsync;

        // And why the announcer said what it last said - see
        // NightAccount.Proof.
        commentary.ProofOfTheLastLine = announcer.TheProofOfTheLastLine;

        // THE NIGHT BEFORE - see NightBefore, and WriteTheNightBeforeAsync
        // below for why the fact-gathering is here rather than on the page.
        commentary.WriteTheNightBefore = WriteTheNightBeforeAsync;
    }

    /// <summary>
    /// Writes a preview of the race day that is set up, and saves it as a
    /// text file the club can paste wherever they post things.
    ///
    /// HERE rather than on the Commentary page because a preview is built
    /// from the programme, the seasons, the club's own history and its
    /// track layouts - four stores that belong to this form. The page owns
    /// a button and a label; this owns the answer.
    ///
    /// A FILE ALWAYS, AND THE CLUB'S PAGE IF IT ASKED (13 Oct 2026). The
    /// file is the thing a club pastes into its own group, and it is
    /// written whether or not the relay is involved. The publish is a
    /// separate yes (AppSettings.PublishNightBeforeToTheRelay) because it
    /// is a separate act: a club that has been pressing this button all
    /// season for something to paste must not find that an update started
    /// putting the same words on a public address.
    ///
    /// And the part nobody would think of until it bit them: something has
    /// to take it DOWN, because a preview left up describes in the future
    /// tense a meeting that finished hours ago. Two things do - this app
    /// withdraws it when the first race is filed (WithdrawTheNightBefore),
    /// and the document carries its own expiry for the evening the laptop
    /// never comes back, which is the one this app cannot cover. See
    /// NightBefore.Withdrawn and NightBefore.StandsFor.
    ///
    /// Returns a sentence either way, because the caller is a button.
    /// </summary>
    private async Task<string?> WriteTheNightBeforeAsync()
    {
        if (_commentary is null || !_commentary.IsAvailable)
        {
            return "The announcer's brain isn't set up on this PC yet, so there is nothing to write with.";
        }

        if (_rounds is null)
        {
            return "There is no programme on this PC to preview.";
        }

        var day = _raceEvents.Active;

        // THE DAY THAT IS SET UP, not a guess at the next one. A race day
        // exists once the club has opened it, and its first race time is
        // the only time this preview will state - see NightBefore's note
        // on times. Its date is asked for below.
        if (day is null)
        {
            return "No race day is open, so there is no entry list to preview. Set the day up first.";
        }

        var toRun = _rounds.Unrecorded(_raceEvents.DayScope());

        if (toRun.Count == 0)
        {
            return "Every race on the programme has already been run, so there is nothing to preview.";
        }

        // THE DATE IS ASKED, NOT TAKEN FROM THE DAY. StartedUtc is when the
        // day was opened - the night before, for a preview written the
        // night before - so it named the wrong day on a public page and
        // set an expiry that had already passed. Nothing else on a race
        // day says which date it is for. See NightBefore.DaysItCouldBeFor.
        //
        // Before anything is awaited, so the question is on this thread
        // and the stores read below are read after it is answered.
        var nowLocal = DateTime.Now;
        var offered = NightBefore.DaysItCouldBeFor(nowLocal, day.FirstRaceLocalTime);

        var picked = AppDialog.Choose(
            this,
            "Which day is this preview for? It says the date on the page, and HeatCaller only knows the race day "
            + "is set up, not which day it will be run. If it is further off than these, write the preview nearer "
            + "the time.",
            NightBefore.Label,
            MessageBoxIcon.Question,
            NightBefore.DayCaption(offered[0], nowLocal),
            NightBefore.DayCaption(offered[1], nowLocal),
            "Not now");

        if (picked < 0 || picked >= offered.Count)
        {
            return "Nothing was written.";
        }

        var whenLocal = offered[picked].Date
            + (day.FirstRaceLocalTime ?? TimeSpan.Zero);

        var entered = NightBefore.EnteredIn(toRun);

        VenueRecord? venue = null;
        TitleStakes? stakes = null;

        // On this thread, where every one of these stores lives, and
        // before anything is awaited - the rule the round intro's own
        // readers follow. A reader that throws costs the preview that one
        // fact and nothing else.
        try
        {
            venue = CommentaryReaders.LayoutRecord(_trackLayouts, _sessionHistory, day.TrackLayoutName);

            // AGAINST THE ENTRY LIST, which is the whole care needed here:
            // the stakes name the drivers who can still win it, and
            // "only Bill and Sam can win it" about a Sam who is not
            // entered is a claim the meeting makes ridiculous. The reader
            // takes the grid for exactly this reason.
            stakes = CommentaryReaders.TitleOnTheLine(
                toRun[0], entered, _seasonStore, _rounds, _raceEvents, _sessionHistory, _profiles);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] The preview goes without the record and the standings - they could not be worked out: {ex.Message}");
        }

        var facts = NightBefore.FactsFor(day.Name, whenLocal, Spoken(day.FirstRaceLocalTime), toRun, venue, stakes);

        if (facts.Empty)
        {
            return "There was nothing in the programme worth previewing yet - add the entry lists first.";
        }

        var script = await _commentary.BuildNightBeforeAsync(facts).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(script))
        {
            return "The model did not write anything usable. Try again, or check the Connection card above.";
        }

        try
        {
            var folder = Rehearsal.DocumentsFolder("HeatCaller Previews");

            Directory.CreateDirectory(folder);

            var name = $"{whenLocal:yyyy-MM-dd} - {PlainFileName.SafePart(day.Name, "Race Day")} - preview.txt";
            var path = Path.Combine(folder, name);

            File.WriteAllText(path, script.Trim() + Environment.NewLine);

            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] {NightBefore.Label}: {script.Trim()}");

            // AFTER the file, and not instead of it: the file is the thing
            // the club asked for and it is already on disk by the time
            // anything touches the network.
            var published = PublishTheNightBefore(day.Name, whenLocal, day.FirstRaceLocalTime, script);

            return $"Saved to Documents\\HeatCaller Previews\\{name}{published} - and written out in the developer console.";
        }
        catch (Exception ex)
        {
            // The words are not lost: they went to the console above a
            // moment ago either way.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] {NightBefore.Label}: {script.Trim()}");

            return $"The preview was written but could not be saved: {ex.Message}. It is in the developer console.";
        }
    }

    /// <summary>
    /// Puts the preview on the club's own page, if the club asked for
    /// that.
    ///
    /// Returns what to add to the button's sentence, and empty when
    /// nothing was published - a club that has not ticked the box should
    /// read nothing about a relay.
    /// </summary>
    private string PublishTheNightBefore(
        string? dayName,
        DateTime whenLocal,
        TimeSpan? firstRaceLocal,
        string? script)
    {
        if (!_settings.PublishNightBeforeToTheRelay)
        {
            return string.Empty;
        }

        if (!_settings.RelayEnabled)
        {
            // Said rather than passed over. A club that ticked the box and
            // has no club website switched on would otherwise press this
            // every week and never wonder why nothing appears.
            return ", but not to your club's page - the club website is switched off on the Club Website page";
        }

        // A preview whose own expiry has passed is never drawn, so the club
        // is told rather than shown a page that stays empty.
        if (NightBefore.UntilUtc(whenLocal, firstRaceLocal) <= DateTime.UtcNow)
        {
            return ", but not to your club's page - the meeting it is for is already over";
        }

        try
        {
            _relayPublisher.PublishSnapshot(
                RelayChannel.Preview, NightBefore.Document(dayName, whenLocal, firstRaceLocal, script));

            // THE DAY IT IS FOR, remembered now, so the withdrawal knows
            // there is something of its own to take down - see
            // WithdrawTheNightBefore for why a flag is not enough.
            _nightBeforeOnThePage = _raceEvents.Active?.Id ?? string.Empty;

            return " and onto your club's page";
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] The preview could not be sent to the club's page: {ex.Message}");

            return ", but it could not be sent to your club's page - the developer console says why";
        }
    }

    /// <summary>
    /// The race day whose preview is on the club's page, or null for none.
    ///
    /// THE DAY AND NOT A BOOLEAN, so that a preview written for next
    /// Saturday is not taken down by tonight's first heat. A club that
    /// writes the preview on Tuesday for Saturday and then runs a practice
    /// evening on Wednesday would otherwise lose it.
    /// </summary>
    private string? _nightBeforeOnThePage;

    /// <summary>
    /// Takes the preview off the club's page, because the meeting it
    /// previews has started.
    ///
    /// CALLED ON THE FIRST FILED RESULT OF THE DAY, which is the first
    /// moment the app can be sure a meeting is actually under way rather
    /// than being set up - a green flag can be a test, a programme can be
    /// built and abandoned, but a filed session happened.
    ///
    /// Idempotent: it fires on every result and does nothing after the
    /// first, so the relay is not sent a withdrawal per heat.
    /// </summary>
    private void WithdrawTheNightBefore()
    {
        if (_nightBeforeOnThePage is not { } forDay)
        {
            return;
        }

        // Only for the day it was written for. A preview for Saturday
        // stands through Wednesday's practice.
        if (!string.Equals(forDay, _raceEvents.Active?.Id ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        // Cleared FIRST, so a publisher that throws does not leave this
        // trying again on every heat of the evening. The preview has an
        // expiry of its own for exactly this case.
        _nightBeforeOnThePage = null;

        try
        {
            _relayPublisher.PublishSnapshot(RelayChannel.Preview, NightBefore.WithdrawnDocument);

            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] The meeting has started, so the preview has come off your club's page.");
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] The preview could not be taken off your club's page: {ex.Message}. It comes down by itself a few hours after the first race.");
        }
    }

    /// <summary>
    /// Takes the preview off the club's page because the club has unticked
    /// putting it there.
    ///
    /// SENT WHETHER OR NOT THIS RUN PUBLISHED ONE. _nightBeforeOnThePage
    /// lives in memory, so a preview published before the app was last
    /// restarted is not in it - and the empty document costs nothing when
    /// there is nothing up: the page draws nothing either way, and the
    /// publisher drops it when the club website is off.
    /// </summary>
    private void TakeTheNightBeforeOffThePage()
    {
        _nightBeforeOnThePage = null;

        try
        {
            _relayPublisher.PublishSnapshot(RelayChannel.Preview, NightBefore.WithdrawnDocument);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [commentary] The preview could not be taken off your club's page: {ex.Message}. It comes down by itself a few hours after the first race.");
        }
    }

    /// <summary>
    /// A first-race time as a club would say it, or empty where the day
    /// has none set. Short time, so it reads "10:15" rather than
    /// "10:15:00".
    /// </summary>
    private static string Spoken(TimeSpan? firstRace) =>
        firstRace is { } at
            ? DateTime.Today.Add(at).ToString("t", System.Globalization.CultureInfo.CurrentCulture)
            : string.Empty;

    /// <summary>
    /// Puts a club who has never been asked onto the show music that ships
    /// with the app - once, on the way up.
    ///
    /// HERE rather than in ShowMusic's constructor, which is built twice
    /// (the page's and the announcer's) and is also built by a preview
    /// pass that has no business writing a club's settings. This runs once
    /// per start, from the one place that knows the app is actually
    /// starting. Everything about why it needs a flag is on
    /// ShowMusic.StartFromTheLibraryOnce.
    /// </summary>
    private void StartShowMusicFromTheLibrary(AppSettings settings)
    {
        try
        {
            var music = new ShowMusic(settings);

            music.Status += line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}");
            music.StartFromTheLibraryOnce();
        }
        catch (Exception ex)
        {
            // Decoration on the way up. A club with no show music is a club
            // whose shows have no music, which is exactly how every club
            // ran before the library existed - not a reason to fail a
            // start.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [show] Couldn't set up the show music that ships with HeatCaller: {ex.Message}");
        }
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

        // THE ONE READER THAT WALKS NO STORE OF FIGURES (12 Oct 2026). The
        // next thing about the NIGHT the club wants mentioned - a
        // dedication, a birthday, the raffle. Everything else on this list
        // is arithmetic over the timing; this is a sentence a human typed
        // because they know something the decoder cannot.
        //
        // The day goes in on the same ask, so last week's notes are gone
        // before one of them can be offered: ClubNotes.NewMeeting is cheap
        // and idempotent for the day it is already on, exactly like the
        // director's own. Done here rather than on a tick of its own
        // because this is asked on the tick anyway, and a note read out at
        // the wrong meeting - to the wrong room, about people who are not
        // there - is the one failure this feature must not have.
        director.ClubAside = () =>
        {
            _clubNotes.NewMeeting(_raceEvents.Active?.Id);

            return _clubNotes.ToSay();
        };

        // And marked said only once the segment carrying it has survived -
        // see CommentaryDirector._marks for why that is not the same
        // moment as being chosen.
        director.ClubAsideSaid = id => _clubNotes.Said(id);

        // WHAT THE ROOM RECKONED (12 Oct 2026) - the only segment in the
        // app that can be wrong, and the only one whose content the people
        // in the room made themselves.
        //
        // The day goes in on the same ask, exactly as the notes' does, so
        // last week's vote is gone before a verdict about it can be
        // offered. RoomVote answers null on a thin vote, a split room, a
        // race with no result filed yet and a vote already said - so
        // anything that comes back here is already worth saying.
        director.RoomsCall = () =>
        {
            _roomVote.NewMeeting(_raceEvents.Active?.Id);

            return _roomVote.CallWaiting();
        };

        // Marked said only once the segment carrying it has survived - see
        // CommentaryDirector._marks, same as the aside above.
        director.RoomsCallSaid = raceKey => _roomVote.Said(raceKey);

        // What the crowd screen shows while a sponsor's read goes out -
        // their picture, or their name where they have none. Asked per
        // read rather than captured, so a picture removed this afternoon
        // is not shown tonight. See SponsorOnAir.
        director.SponsorOnScreenFor = id => _sponsors.OnScreenFor(id);

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

        // AND THE SHAPE OF THEIR LAST FEW NIGHTS (11 Oct 2026) - the half
        // of a form guide the app did not have. A reader of its own and not
        // something taken off GridHistory above, because a night is a DATE
        // and the shape needs the nights a driver was absent from; neither
        // is in DriverForm. See CommentaryWeekFacts.
        //
        // Read fresh every time rather than captured, like the rest of
        // them, so a result corrected mid-evening moves this with it.
        director.RecentWeeks = () => _rounds is null
            ? Array.Empty<DriverWeeks>()
            : CommentaryReaders.RecentWeeks(
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

        // THE NIGHT'S THREAD - the one storyline the evening is told as,
        // set at the cold open and resolved at the sign-off.
        //
        // Read fresh on every pass like the rest of these, and for this
        // one that is the feature rather than a convenience. Nothing about
        // a thread is remembered anywhere in this app, so a thread the
        // night has contradicted cannot be offered: by the time the next
        // segment is weighed up this has already seen the heat that beat
        // the subject, and the storyline turns instead of repeating. A
        // thread held between passes would be the "can anyone stop Dave"
        // that keeps asking after Dave went out in heat three, which is
        // the one failure a room notices instantly and laughs at. See
        // NightThread.
        //
        // The same scope as NightProgress above and for its reasons:
        // tonight's rounds are who is out and whether the night is over,
        // and last week's programme is neither.
        director.NightStory = () => CommentaryReaders.NightStory(
            _rounds,
            _sessionHistory,
            _profiles,
            _raceEvents.ActiveRoundScope());

        // THE TWO BOOKENDS - the evening's shape at the top of the night
        // and its numbers on the way out (11 Oct 2026).
        //
        // THESE TWO ARE WHY THE WHOLE SHOW HAD NO BRACKET. The director has
        // declared NightAhead and NightDone since the bookends were built,
        // and nothing in the app had ever set either - so SegmentKind
        // ColdOpen and SignOffNumbers had a title, a tier, a budget and a
        // tick box on the Commentary page and were silent in the field
        // every night, for every club, for as long as they have existed.
        // Exactly the fault the halfway standings, this-day-at-the-club and
        // form guide had until 10 Oct 2026, and it outlasted theirs because
        // the note further down this method read as though the bookends had
        // been dealt with along with the continuity lines. They had not.
        //
        // The cold open reads the PROGRAMME and the sign-off reads the
        // RESULTS, which is the one thing to keep in mind when touching
        // either: the opening is said before a wheel has turned, so
        // anything it was gated on having recorded would silence it at the
        // only moment it exists for. See CommentaryReaders.NightAhead.
        director.NightAhead = () => CommentaryReaders.NightAhead(
            _rounds,

            // The same scope as NightStory and NightProgress, for their
            // reasons: tonight's rounds are tonight's shape, and last
            // week's programme is not.
            _raceEvents.ActiveRoundScope(),

            // The race day in hand, for the venue the club typed on it -
            // nothing else off it is read. Null when no day is open, and
            // then the opening is said without a place, which is what most
            // clubs want anyway: they race where they always race.
            _raceEvents.Active);

        // The sign-off takes no scope at all, and that is not an omission -
        // see the reader. A race the club ran tonight counts towards the
        // night's totals whether it was filed against one of tonight's
        // rounds, against another day's, or against no round at all,
        // because the sign-off is about what happened rather than about
        // what was planned.
        director.NightDone = () => CommentaryReaders.NightDone(
            _sessionHistory,
            _profiles);

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
        // B-main; still to come, the A-main" - and they were the reason
        // none of it could speak: CommentaryContinuity and ComingUpNext
        // both read them, nothing had ever wired them up, and so every
        // continuity line in the app was silent in the field.
        //
        // THE TWO BOOKENDS DO NOT READ THESE, whatever this comment used to
        // claim. The cold open reads NightAhead and the sign-off reads
        // NightDone, and those two readers were not written until 11 Oct
        // 2026 - CommentaryReaders.NightAhead and NightDone, wired above.
        // Until then both ends of the night had a tick box and no reader at
        // all, so wiring these never did anything for either of them, and
        // a note saying otherwise is how a fault stays in the field for a
        // fortnight with something in the source that reads like a fix.
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

        // Scored under the filed race's OWN round, not the active one (14
        // Oct 2026). Candidates leaves out the race filed under the round it
        // is given, so the filed race is not counted twice - but by the time
        // this runs the active round has often moved on to the next race,
        // and then the filed race stayed in the counted table AND was added
        // again as the "live" one, and its points went to air doubled.
        director.Championship = () =>
        {
            var filed = LastFiled();
            var filedRound = filed?.RoundId is { } roundId ? _rounds.Get(roundId) : null;

            return CommentaryReaders.Championship(
                ChampionshipLive.Compute(
                    LastFiledResults(),
                    filedRound,
                    _seasonStore,
                    _rounds,
                    _raceEvents,
                    _sessionHistory,
                    _profiles));
        };
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
    private IReadOnlyList<SessionResultEntry> LastFiledResults() =>
        LastFiled()?.Results ?? (IReadOnlyList<SessionResultEntry>)Array.Empty<SessionResultEntry>();

    /// <summary>The last race filed with results, or null - the record LastFiledResults reads, kept whole so its round can be asked too.</summary>
    //
    // SessionHistoryStore.All is newest FIRST, so this reads it from the
    // front (14 Oct 2026). It used to walk it from the end, which is the
    // oldest race the club ever filed - so "the last race filed" was the
    // first one, and the championship check talked about that.
    private SessionRecord? LastFiled() =>
        _sessionHistory.All.FirstOrDefault(s => s.SessionType != RaceSessionType.Practice && s.Results.Count > 0);

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
    /// What the garage and the club's own saved results know about the
    /// cars in a saved race, for the recap - see CommentaryReaders.Cars.
    /// Asked by Race Control on the UI thread, where the Garage's stores
    /// are read, exactly like the mechanic's brief above.
    ///
    /// A reader of its own and not an extension of the brief: that is the
    /// winner's SETUP, for the interview, and this is the finishers' cars,
    /// for the recap. The two must never say the same thing - the
    /// interview runs straight after the recap - which is why nothing in
    /// here touches the setup log. See RaceControlControl.RecapCarFacts.
    /// </summary>
    private IReadOnlyList<RaceCar> RecapCarFactsFor(SessionRecord record, RoundDefinition? round)
    {
        // The race day it was run on, for a car lent for that day: the
        // round's own day, or the day in hand for a race that isn't a
        // round. The same answer MechanicSetupFactsFor works out above,
        // and for the same reason - Car.DriverFor will not let last
        // Tuesday's loan answer for tonight, but only if it is told which
        // day to ask about.
        var eventId = (round is null ? null : _raceEvents.EventForRound(round.Id)?.Id) ?? _raceEvents.Active?.Id;

        return CommentaryReaders.Cars(
            record,
            round?.ClassName,
            eventId,
            _profiles,
            _sessionHistory,
            _rounds,

            // Every car the club has, retired ones included - the reader
            // drops those itself, so that judgement stays in one place
            // beside the rest of them.
            _cars.All);
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
