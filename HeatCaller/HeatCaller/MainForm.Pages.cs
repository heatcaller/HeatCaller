using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// PopulateTabs: builds every page and sub-tab and wires them to the stores
/// and services. Moved here from MainForm.cs on 26 Sep 2026 when the file
/// was split into parts, and split into one method per page on 22 Oct 2026 -
/// PopulateTabs calls them in the order the pages were always built.
/// </summary>
public partial class MainForm
{
    /// <summary>Times the window's building, part by part, for the session log's "[startup] Pages built in" line - null once it is built.</summary>
    private System.Diagnostics.Stopwatch? _startupClock;

    /// <summary>Each part of the building and how long it took - see StartupMark.</summary>
    private readonly List<(string Part, double Seconds)> _startupMarks = new();

    /// <summary>
    /// Notes how long the part of the building that has just ended took,
    /// for the session log - so a slow start says which part was slow
    /// rather than leaving it to be guessed (Asher, 30 Sep: over a minute;
    /// 1 Oct: 14s). Nothing once the window is built.
    /// </summary>
    private void StartupMark(string part)
    {
        if (_startupClock is not { } clock)
        {
            return;
        }

        _startupMarks.Add((part, clock.Elapsed.TotalSeconds));
        clock.Restart();
    }

    /// <summary>
    /// Creates one instance of each converted screen and docks it into its
    /// tab. Each used to be its own Form, opened fresh (and disposed) every
    /// time its button was clicked; now they're built once, here, and live
    /// for the app's whole lifetime as ordinary tab content - which is also
    /// why RaceControlControl no longer needs the open/Activate/null-out
    /// dance a separate window required.
    /// </summary>
    private void PopulateTabs()
    {
        var raceControl = BuildRaceControlPage();

        BuildDriverHubPage();

        BuildClassesPage();

        StartupMark("Race Control, Driver Hub and Classes");

        var (raceProgram, achievements) = BuildRaceDayAndChampionshipPages(raceControl);

        var pageGhosts = BuildPracticeGhostsAndDemoPages(raceControl, raceProgram, achievements);

        StartupMark("Practice, Ghost Cars and Demo Mode");

        var announcer = BuildVoicePage(raceControl);

        StartupMark("Voice");

        BuildSoundsPage(announcer);

        BuildThemesPage();

        StartupMark("Sounds and Themes");

        BuildSponsorsPage();

        BuildLocalScreensPage();

        BuildClubWebsitePage();

        BuildVenuesPage(raceControl);

        BuildBackupPage();

        // The session row's Round picker can go stale while another page
        // adds or removes rounds, so it is refreshed every time Race Control
        // becomes the active page.
        //
        // This was keyed to "Connection", from when the session row lived on
        // the Connection page. It moved to Race Control and this did not
        // move with it, so the row was refreshed on a visit to a page it was
        // no longer on and never on a visit to the one it was. A track made
        // on Track Layouts, or a programme built on Race Day, did not appear
        // in it until the app restarted.
        //
        // The track picker is not refreshed here any more: it follows the
        // layout store directly (OnTrackLayoutsChanged), which catches a
        // change made while Race Control is already showing, and this could
        // not. Safe mid-race: the round list is rebuilt around the running
        // round, and RefreshFormat only writes the race limits while the
        // race is idle or finished.
        _navRefreshHooks["Race Control"] = RefreshActiveRoundCombo;

        BuildNavItems(pageGhosts);

        txtNavSearch.TextChanged += (_, _) => RebuildNav();
        // ~20fps is plenty for a slow-drifting grid, and keeps the cost
        // negligible on a laptop at a track.
        _headerAnimationTimer.Tick += (_, _) =>
        {
            AppTheme.AnimationPhase = (AppTheme.AnimationPhase + 0.004f) % 1f;
            lblAppTitle.Invalidate();
        };

        // The club logo, if there is one and it is wanted there - which
        // also starts the header animation, or leaves it off while a logo
        // is showing.
        RefreshHeaderLogo();

        btnManageNav.Click += BtnManageNav_Click;

        // The chevron in the page-search row: the sidebar down to its icons
        // and back - see SetNavIconsOnly. Ctrl+B does the same from anywhere.
        btnCollapseNav.Click += (_, _) => ToggleNavIconsOnly();

        _announcer.Muted = _settings.AnnouncerMuted;

        // The icon and the tooltip both default to "not muted" until told
        // otherwise (SetUpFooterButton runs before this setting is known) -
        // this is what makes a club that starts up already muted show it.
        UpdateMuteButtonText();

        WireUndoButton();

        StartupMark("the other pages");

        // The sidebar narrowed to its icons, if that is how the club left it
        // (AppSettings.NavIconsOnly) - BEFORE the window is laid out rather
        // than after, so it comes up narrow from the first frame instead of
        // being seen to fold a moment after the window appears. Nothing to do
        // when it is not: the designer builds the open sidebar, and
        // ApplyFontScale below gives it its width either way (PlaceSidebar).
        if (_settings.NavIconsOnly)
        {
            SetNavIconsOnly(true, save: false);
        }

        // The theme pass, once: ApplyFontScale runs it (with the sidebar's
        // chrome put back after it), in the window's own face - which it
        // sets first. It used to be run here as well, just before, which
        // was the whole window themed twice at every start.
        ApplyFontScale();

        StartupMark("text size and theme");

        btnMuteToggle.Click += (_, _) => SetAllSoundMuted(!_announcer.Muted);

        lstNav.SelectionChanged += LstNav_SelectionChanged;
        lstNav.ItemsReordered += LstNav_ItemsReordered;

        // Essentials and club tools, each under a quiet heading - see NavTiers.
        // Asked per row on every repaint, so it has to read the same flag the
        // ordering above does - see _navRaceNight.
        // In Simple mode, what a search turns up that is hidden goes under
        // its own heading at the foot - see AdvancedMatching.
        lstNav.SectionOf = name => !SimpleMode.ShowAll && (name == SimpleMode.SearchRow || SimpleMode.HiddenPages.Contains(name))
            ? SimpleMode.SearchHeading
            : NavTiers.SectionOf(name, _navRaceNight);

        // Before the first build, not after: a club that closed the app
        // mid-race-day reopens it with that day still current, and the sidebar
        // should already be in race-night order rather than rearranging itself
        // a moment later.
        _navRaceNight = RaceNightWanted;

        RebuildNav();

        StartupMark("first page");

        // After the window is actually up: the pages' handles are made in
        // the window's, which does not exist while it is still being built.
        // Timed for the session log.
        Shown += (_, _) =>
        {
            var warming = System.Diagnostics.Stopwatch.StartNew();

            // What the windows made meanwhile cost, when it is slow - see
            // WhereTimeGoes.
            WhereTimeGoes.StartOnShownWindow("Making the pages ready");
            string? timing;
            try
            {
                WarmPages();
            }
            finally
            {
                // 17 Oct 2026, Bug Hunt 8, A30-6: a page throwing during warm-up
                // (caught by ThreadException) left the thread-wide hooks counting
                // every UI-thread message.
                timing = WhereTimeGoes.Take();
            }

            OnClientRawMessage($"{AppTime.LogStampNow()}  [startup] Every page and tab made ready in {warming.Elapsed.TotalSeconds:0.0}s."
                + (warming.Elapsed.TotalSeconds >= 1 && timing is not null ? $" {timing}" : string.Empty));
        };
    }

    /// <summary>
    /// Race Control: the page, the stores and hooks it is handed, and the session row it carries.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private RaceControlControl BuildRaceControlPage()
    {
        var raceControl = new RaceControlControl(_race, _announcer, _profiles, _raceClasses, _trackLayouts, _sessionHistory, _settings, _undo, _daySchedule, _sponsors, _rounds, () => _activeRound, _commentary, _transponderWatch, _raceEvents, _checkInStore, _seasonStore, techInspections: _techInspections, filing: _raceFiling) { Dock = DockStyle.Fill };
        _raceControlControl = raceControl;

        // Pack, on a row's right-click - see RaceControlControl.Packs.
        raceControl.BatteryPacks = _batteryPacks;
        raceControl.StatusMessage += OnClientRawMessage;
        raceControl.AttachUnknownTransponders(_unknownTransponders);

        // The TIMING line on the Next Up bar, worked out every race tick in
        // UpdateTimingWatch whichever page is showing.
        raceControl.TimingProblem = () => _timingProblemText;

        // What the phone remote says it is controlling - on the UI thread,
        // where the LAN copy of the live state is built.
        //
        // These three are this page's to answer, so threaded - where the live
        // state is built off the UI - each is a frozen feed, answered on the
        // UI tick (24 Oct 2026, timing layer, P8; see MainForm.LiveFeed).
        // Inline LiveFeed hands back the callback itself.
        _webServer.RaceInHand = LiveFeed<string>(raceControl.RaceInHandTitle);

        // The races still to run, for phones following a driver - when is
        // their next race and where do they grid. See PhoneFollow.
        _webServer.UpcomingRaces = LiveFeed<IReadOnlyList<UpcomingRace>>(() => raceControl.UpcomingRaces(8));

        // The championship as it would stand if the race ended now, for the
        // Live Results page - see ChampionshipLive.
        _webServer.IfItEndedNow = LiveFeed<IfItEndedNow?>(raceControl.ChampionshipIfItEndedNow);

        // The live track map, for the Big Screen this page opens - see MainForm.TrackMap.
        raceControl.TrackMap = TrackMapNow;

        // A finished race changes race-program state, so refresh the visible
        // page rather than leaving a heat showing "Scheduled" until the user
        // happens to navigate away and back.
        // The flag row's Results and Edit Result after the flag (25 Oct 2026).
        raceControl.EditResultRequested += () => ShowLatestRaceInHistory(edit: true);

        // The Desk button on the flag row (7 Nov 2026): Driver Requests,
        // where the waiting requests are approved or declined.
        raceControl.DeskRequested += ShowDriverRequests;

        raceControl.SessionSaved += () =>
        {
            // The heat just recorded comes off the timetable here, open or
            // shut. Only the Timetable page used to tick lines off, as it
            // opened, so with it shut the Big Screen's ON DECK, the phones
            // and the online schedule stayed on the first heat all night.
            TickOffRecordedHeats(_daySchedule, _rounds);

            try
            {
                _daySchedulePage?.RefreshAll();
            }
            catch
            {
                // Raised before the race's journal is let go (RaceFiling.Commit):
                // redrawing a page must never stop that.
            }

            RefreshActiveRoundCombo();
            RefreshCurrentPage();
        };
        // Session setup moved here from the connection page: it's what you
        // set immediately before starting a race, so it belongs beside the
        // race controls rather than next to the decoder settings.
        //
        // ON the Race Control page, as its first part, above the Next Up
        // bar - see RaceControlControl.ShowSessionRow. It used to be docked
        // above the page in a strip of its own, which then had to be told
        // how wide Race Control's scrollbar was at every moment to keep to
        // the same two edges as everything under it. On the page it simply
        // has them.
        raceControl.ShowSessionRow(grpSession);

        // Track Layout and Session Label are no longer SHOWN here - Race
        // Control has the visible pair - but session saving still reads
        // these fields, so they're mirrored from the ones on screen.
        // Without this a saved session would record a blank track.
        // Session Setup is the single place these are set; Race Control's
        // copies are hidden and follow along. The flow used to be the other
        // way round, which is why both were visible and either could be
        // changed independently.
        cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            if (cmbTrackLayout.SelectedItem is string name)
            {
                raceControl.SetTrackLayout(name);
            }
        };

        txtSessionLabel.TextChanged += (_, _) => raceControl.SetSessionLabel(txtSessionLabel.Text);

        pageRaceControl.Controls.Add(raceControl);

        return raceControl;
    }

    /// <summary>
    /// Driver Hub, with the stores its driver card and garage read.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildDriverHubPage()
    {
        // The stores the driver card reads its numbers from - results,
        // championships, rivalries, personal-best resets and the
        // race-day paperwork. Every one of them already lives here.
        var driverStatsSources = new DriverStatsSources
        {
            History = _sessionHistory,
            Seasons = _seasonStore,
            Rounds = _rounds,
            Events = _raceEvents,
            Rivalries = _rivalryStore,
            PersonalBestResets = _pbResets,
            Handicaps = _handicaps,
            TechInspections = _techInspections,
            CheckIns = _checkInStore,
            SetupLog = _setupLog,
            ChassisTemplates = _chassisTemplates,
            TrackLayouts = _trackLayouts,
        };

        // A driver's cars, transponders and setups on their own page - the
        // same stores and the same car window as the Garage, so a car is
        // changed in one place whichever page it was opened from.
        var driverGarage = new DriverGarageSources
        {
            Cars = _cars,
            Setups = _setupLog,
            Templates = _chassisTemplates,
            MakeGarage = () => new GarageControl(
                _cars, _chassisTypes, _profiles, _raceClasses, _setupLog, _sessionHistory,
                _trackLayouts, _chassisTemplates, _undo, _raceEvents, forOneDriver: true, batteries: _batteryPacks),
            OpenCar = (car, owner) =>
            {
                using var form = new CarDetailForm(
                    car, _cars, _chassisTypes, _profiles, _raceClasses, _setupLog, _sessionHistory,
                    _trackLayouts, _chassisTemplates, _undo, _raceEvents);
                form.ShowDialog(owner);
            },
            Batteries = _batteryPacks,
            History = _sessionHistory,
            SplitSeenRecently = () => _splitLoop?.UnknownRecently(TimingClock.NowMs) ?? Array.Empty<string>(),
        };

        var driverProfiles = new ProfileManagerControl(_profiles, _raceClasses, _undo, driverStatsSources, _settings, driverGarage) { Dock = DockStyle.Fill };

        // Tonight's report card for a driver - a right-click on the roster
        // (22 Oct 2026). See MainForm.Coach.
        driverProfiles.ReportCardFor = transponder => CoachReportFor(transponder, publicNames: false, includeSetup: true);
        pageDriverProfiles.Controls.Add(driverProfiles);
        _navRefreshHooks["Driver Hub"] = driverProfiles.RefreshAll;
        _driverHubSetupsRefresh = driverProfiles.RefreshSetups;
        driverProfiles.AttachUnknownTransponders(_unknownTransponders);

        // How "Hear It" and "Check Names..." on Driver Hub actually speak.
        // The page is given a way to say a line rather than the announcer
        // itself: it has no business with the voice queue, and what it wants
        // is the one thing SpeakTest already does - say this text, now, in
        // the voice the club is set up with, without touching the race's own
        // queue. Without this line the page lists the names and says the
        // voice is not available, and Hear It is not built at all, which is
        // deliberate: a dead button reads as a broken feature.
        driverProfiles.SpeakName = text => _announcer.SpeakTest(text, _voiceStore.Current.Speed);
        WireUnknownTransponders();
    }

    /// <summary>
    /// Classes.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildClassesPage()
    {
        // Next to Driver Hub in the sidebar: the two together are "who
        // races and under what rules", and they are the two pages somebody
        // sets up before a first race day.
        ConfigurePage(pageClasses);

        var classes = new RaceClassesControl(_raceClasses, _undo) { Dock = DockStyle.Fill };
        pageClasses.Controls.Add(classes);

        // Refreshed on entry, not only at construction: a class can be
        // edited from the dialog on Driver Hub while this page sits in
        // the background.
        _navRefreshHooks["Classes"] = classes.RefreshAll;
    }

    /// <summary>
    /// Race Day and Championship, and the pages built alongside them - Rentals, Handicaps, Track Layouts, Race History, Achievements, Garage and Race Analytics. The program and the badges page are handed back: Practice and Demo Mode need them.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private (RaceProgramControl RaceProgram, AchievementsControl Achievements) BuildRaceDayAndChampionshipPages(RaceControlControl raceControl)
    {
        // --- Race Day and Championship ---
        //
        // Six nav items became two pages, and the split is by time scale:
        // Championship is the season (the series, its calendar, its
        // standings), Race Day is TODAY (the event, who is here, the
        // program, the run sheet). The two are linked underneath - an
        // event belongs to a championship, and everything on Race Day is
        // filed against the current event - but on race day nobody should
        // be navigating a season to find the check-in sheet.
        //
        // Race Day's tab order is the order the day runs in, left to
        // right: settle the event, get people through the door, build the
        // program, then run the sheet. The tabs ARE the flow.
        ConfigurePage(pageRaceDay);
        ConfigurePage(pageChampionship);

        // Plain English, and the day's own order.
        //
        // The five nouns these used to be - Event, Entries, Program, Run
        // Sheet, Tech Log - were one per store rather than one per job, and
        // a club that does not already know the app's vocabulary could not
        // tell from them where anything was. "Entries" was the worst of
        // them: it had no way to enter anyone.
        //
        // Today leads, because it is the only tab that says what to do
        // next. Everything behind it is the detail drawer for one part of
        // the day.
        var subPageToday = new TabPage("Next Steps");
        var subPageEvent = new TabPage("Setup");
        var subPageEntries = new TabPage("Who's Racing");

        // There is no "Heats & Mains" tab any more. It held a next-step card
        // that said, word for word, what Today's card says - both ask
        // ProgramNextStep - and a programme grid that Today had no way to
        // show. The grid has moved under Today's card, where it belongs, and
        // the tab has gone. See the Today section further down.
        var subPageStandings = new TabPage("Standings");
        var subPageCalendar = new TabPage("Calendar");

        var raceProgram = new RaceProgramControl(_raceClasses, _profiles, _rounds, _trackLayouts, _sessionHistory, _checkInStore, _settings, _undo, _raceEvents, _daySchedule) { Dock = DockStyle.Fill };
        _raceProgram = raceProgram;
        // "Use for Next Session" is gone from the Program tab, and with it
        // the second way to put a round on track. There is one now -
        // RoundStartRequested, below - because two paths to the same
        // outcome is exactly what made that page hard to read.
        // Parented further down, into the lower half of Today. Nothing owns
        // it here any more.
        raceProgram.StatusMessage += OnClientRawMessage;

        // The heat out on track, so undoing a build or a seeding does not
        // delete it from under the race. Anything but Idle counts - a
        // finished race is still waiting for its result to be saved.
        raceProgram.RoundOnTrackId = () => _race.State == RaceState.Idle ? null : _activeRound?.Id;

        // The same heat, so deleting its race day - from The Day or the
        // Calendar - does not take it from under the race.
        RaceDayDeleter.RoundOnTrackId = () => _race.State == RaceState.Idle ? null : _activeRound?.Id;

        raceProgram.RoundStartRequested += round =>
        {
            // Checked before the round is set up or its grid call spoken -
            // practice is on the same session, and starting over it would
            // wipe the practice laps.
            if (_raceControlControl.RefuseIfPracticeRunningForRaceStart("start a race"))
            {
                return;
            }

            // Asked here, before the round picker moves or the grid call is
            // spoken: asking at Stage instead would have announced a heat
            // that then does not run, and left the pickers on it. True
            // when nothing is running. The answer is passed on below so
            // Stage does not ask a second time.
            if (!_raceControlControl.ConfirmAbandonRunningRace($"start \"{round.RoundLabel}\""))
            {
                return;
            }

            StartRoundNow(round, confirmedOverRunningRace: true, showRaceControl: true,
                $"[program] Starting \"{round.RoundLabel}\" from the race program.");
        };

        // The Next Up bar's Start Next - the same path again, minus the
        // page switch, because the person pressing it is already there.
        raceControl.NextRoundStartRequested += round =>
        {
            if (_raceControlControl.RefuseIfPracticeRunningForRaceStart("start a race"))
            {
                return;
            }

            // Asked before the round moves or its grid call is spoken, as
            // for the programme above - and the answer passed on so Stage
            // does not ask again.
            if (!_raceControlControl.ConfirmAbandonRunningRace($"start \"{round.RoundLabel}\""))
            {
                return;
            }

            StartRoundNow(round, confirmedOverRunningRace: true, showRaceControl: false,
                $"[day] Starting \"{round.RoundLabel}\" from the Next Up bar.");
        };

        // Built once and kept, even though it lives in a drawer now.
        //
        // Not a fresh control per opening: the undo entries this page
        // captures call RefreshAll on the instance that made them, and a new
        // one each time would leave those holding a page that had been
        // thrown away - the same fault the scrutineering log was fixed for.
        // It is parented into the drawer's window on the way in and taken
        // back out in FormClosed, or that window would dispose it.
        var daySchedule = new DayScheduleControl(_daySchedule, _raceClasses, _announcer, _undo, _rounds, _raceEvents) { Dock = DockStyle.Fill, Settings = _settings };
        _daySchedulePage = daySchedule;

        // Who's Racing IS the merged roster: present, tech and inspect
        // per row. The Tech Log tab keeps the scrutineer's full record
        // list; the gate itself is one sheet.
        var checkIn = new CheckInControl(_profiles, _checkInStore, _raceClasses, _settings, _undo, _techInspections, _webServer, _raceEvents, _batteryPacks, _payments) { Dock = DockStyle.Fill };
        _checkInControl = checkIn;

        // The banner this page shows when check-ins have nowhere to go.
        checkIn.StartRaceDayRequested += StartRaceDayFromBanner;

        // The desk's changes are timed, so a slower phone tap made before
        // them cannot undo them - see ApplyRelayCheckIn.
        checkIn.CheckInChangedAtDesk += deskTransponder => _checkInChangedUtc[deskTransponder] = DateTime.UtcNow;

        // Online sign-ups: the button that opens them says how many are
        // waiting, and so does the sidebar, so a sign-up is noticed without
        // anyone having to go looking.
        checkIn.SignupsRequested += ShowSignups;
        _signupDesk.Changed += UpdateSignupCounts;
        subPageEntries.Controls.Add(checkIn);
        _subTabRefresh[subPageEntries] = checkIn.RefreshAll;

        ConfigurePage(pageRentals);
        ConfigurePage(pageHandicaps);

        BuildRentalsPage();

        WireScrutineeringLog(checkIn);

        BuildHandicapsPage();

        var eventsPage = new EventsControl(_raceEvents, _rounds, _sessionHistory, _undo, _seasonStore, _trackLayouts) { Dock = DockStyle.Fill };
        subPageCalendar.Controls.Add(eventsPage);
        _subTabRefresh[subPageCalendar] = eventsPage.RefreshAll;

        BuildTrackLayoutsAndRaceHistoryPages();

        var achievements = BuildAchievementsPage();

        BuildGaragePage();

        StartupMark("Race Day to Garage");

        BuildAnalyticsPage();

        var seasons = new SeasonsControl(_seasonStore, _sessionHistory, _undo, _rounds, _raceClasses, _raceEvents, _profiles) { Dock = DockStyle.Fill };
        subPageStandings.Controls.Add(seasons);
        _subTabRefresh[subPageStandings] = seasons.RefreshAll;

        // --- Assemble the two pages, now that every tab has its control ---
        var eventTab = new RaceDayEventControl(
            _raceEvents, _venueStore, _trackLayouts, _seasonStore, _checkInStore, _rounds, _daySchedule, _raceClasses, _undo,
            _sessionHistory, _settings);

        subPageEvent.Controls.Add(eventTab);
        _subTabRefresh[subPageEvent] = eventTab.RefreshAll;

        // The setup wizard, opened from The Day. Built here rather than
        // there because it drives three pages' commands, not one - and
        // every one of them is handed in, so the wizard never grows its
        // own version of starting a day or building a programme. See
        // RaceDaySetupHost.
        // Opened at a step: 0 is the welcome page, and the Ready to Race bar
        // on Next Steps asks for the first step that is not done yet.
        _openRaceDaySetup = startStep =>
        {
            using var wizard = new RaceDaySetupWizard(
                _settings, _raceClasses, _profiles, _trackLayouts, _venueStore, _raceEvents, _checkInStore,
                new RaceDaySetupHost(
                    StartDay: eventTab.StartNewDayFromWizard,
                    ChangeDay: eventTab.ChangeDayFromElsewhere,
                    SetClassesRacing: eventTab.SetRacingClassesFromElsewhere,
                    PlanBuild: () => _raceProgram.PlanQualifyingBuild(),
                    Build: () => _raceProgram.BuildQualifying(string.Empty),
                    Sound: MakeSoundOfferHost(),
                    SetCheckedIn: SetCheckedInFromWizard,
                    Undo: _undo),
                startStep);

            wizard.ShowDialog(this);

            // Everything the wizard did went through the stores, so the
            // pages re-read rather than being told what changed - and its
            // "Don't ask about the sound again" may have been ticked.
            eventTab.RefreshAll();
            _themesControl?.ShowAskTick();
            _raceProgram.RefreshAll();
            _todayControl?.RefreshAll();
            RefreshCurrentPage();

            if (wizard.GoToPageOnClose is { } page)
            {
                GoToPage(page);
            }
        };

        eventTab.SetUpTonightRequested += () => _openRaceDaySetup?.Invoke(0);

        // Starting a race day from anywhere else - the Today checklist, the
        // banner on Who's Racing - goes through the Day tab's own command,
        // so there is one implementation with one undo entry rather than
        // three that drift.
        //
        // "Start a Race Day" - the next-step card on Next Steps, the banner on
        // Who's Racing - opens the Set Up Tonight wizard now, not the
        // two-question dialog. The wizard's day step starts the day through
        // this same tab's command (RaceDaySetupHost.StartDay), so there is
        // still one implementation; the wizard just walks on from there to
        // the classes, check-in and the programme instead of leaving them to
        // be found. Opened at the first step not done yet - the step the
        // Ready to Race bar's own button would open.
        //
        // The Calendar's New Race Day below still uses the dialog: from
        // there somebody is filing a day, not setting up tonight.
        _startRaceDay = () =>
        {
            var step = _todayControl?.FirstSetupStep() ?? RaceDaySetupWizard.StepDay;
            _openRaceDaySetup?.Invoke(step);
        };

        // The Calendar's New Race Day is that same command - one dialog,
        // one undo entry, one set of defaults carried forward - but it
        // stays on the Calendar afterwards rather than marching the window
        // to The Day. The day arrives with its shape, its classes and the
        // last day's venue and format already on it, so there is nothing
        // waiting to be typed.
        eventsPage.StartRaceDayRequested += () =>
        {
            if (!eventTab.StartNewDayFromElsewhere())
            {
                return;
            }

            eventsPage.RefreshAll();
            RefreshCurrentPage();
        };

        var tabRaceDay = new TabControl { Dock = DockStyle.Fill };
        tabRaceDay.TabPages.Add(subPageToday);
        tabRaceDay.TabPages.Add(subPageEvent);
        tabRaceDay.TabPages.Add(subPageEntries);
        AppTheme.StyleTabControl(tabRaceDay);

        var tabChampionship = new TabControl { Dock = DockStyle.Fill };
        tabChampionship.TabPages.Add(subPageStandings);
        tabChampionship.TabPages.Add(subPageCalendar);
        AppTheme.StyleTabControl(tabChampionship);

        void RefreshSubTab(TabControl tabs)
        {
            if (tabs.SelectedTab is { } tab && _subTabRefresh.TryGetValue(tab, out var refresh))
            {
                refresh();
            }
        }

        // Held so anything outside this method can put the page on a
        // particular tab - see _startRaceDay.
        _goToDayTab = () =>
        {
            GoToPage("Race Day");
            tabRaceDay.SelectedTab = subPageEvent;
        };

        tabRaceDay.SelectedIndexChanged += (_, _) => RefreshSubTab(tabRaceDay);
        tabChampionship.SelectedIndexChanged += (_, _) => RefreshSubTab(tabChampionship);

        // The Program tab's banner sends the director somewhere else when
        // the day is not set up yet - there is no class list to build a
        // programme from, so saying "go and make one" and taking them
        // there beats a button that explains why it cannot work.
        //
        // Wired HERE rather than where raceProgram is built, because these
        // need the tab control, and that does not exist until the tabs
        // have all been made.
        raceProgram.GoToEventTab += () => tabRaceDay.SelectedTab = subPageEvent;
        raceProgram.ManageDriversRequested += () => GoToPage("Driver Hub");

        // The two empty states the banner could not tell apart before, and
        // so could not send anywhere useful - see ProgramNextStep.
        raceProgram.GoToEntriesTab += () => tabRaceDay.SelectedTab = subPageEntries;
        raceProgram.SetUpClassesRequested += () => GoToPage("Classes");
        raceProgram.StartRaceDayRequested += StartRaceDayFromBanner;

        // Race Control asks the programme what is owed before the next
        // race, rather than working "next" out on its own - which is how
        // its Next Up bar came to offer Start Next on a main whose grid
        // was about to change. One authority, three screens.
        // Filling in the times writes a time against every line AND the
        // day's first-race time, so the two pages that read those - Today's
        // Due column, and The Day's field - are stale until they re-read.
        daySchedule.TimesFilled += () =>
        {
            _todayControl?.RefreshAll();
            _raceProgram.RefreshAll();
            RefreshCurrentPage();
        };

        // Move Up on a round's line moves the ROUND. There is one running
        // order and the programme owns it; the timetable redraws afterwards
        // because it reads that order, not because it was told to.
        daySchedule.MoveRoundRequested += (roundId, direction) => raceProgram.MoveRound(roundId, direction);

        // The timetable follows the programme instead of waiting to be
        // rebuilt by hand.
        //
        // Done HERE rather than on the page, because the page is a drawer
        // now and is shut for most of an evening. Hung off the control, the
        // sheet would only catch up at the moment somebody opened it - and
        // the printed run sheet, the countdown and the drift would all have
        // been read off a stale order until then. It is a data operation;
        // it belongs with the data.
        raceProgram.ProgrammeChanged += SyncTimetableToProgramme;

        // And Today, because the programme grid is the lower half of that tab
        // now. Building or seeding from one of the grid's own buttons changes
        // what the card above it should be saying, and before this the card
        // sat on the stale answer until the tab was left and come back to.
        raceProgram.ProgrammeChanged += () => _todayControl?.RefreshAll();

        raceControl.NextStepProvider = () => raceProgram.NextStep();

        // The night planned (11 Nov 2026, race-night build plan, Phase 2):
        // "We're done after this round" is told to the room, and the end of
        // the night lists what is left before lights out.
        raceProgram.NightCutShort += text =>
        {
            _announcer.SpeakReminder(text, showOnScreens: true, onAir: "Notice");
            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Announced: \"{text}\"");
        };
        raceProgram.ClosingFacts = NightClosingFacts;
        raceControl.RunProgramStepRequested += step => raceProgram.RunStep(step);

        // --- Today ---
        //
        // Built last of the sub-tabs because it reads the programme tab
        // rather than the stores: the one next step, and the command that
        // runs it, stay in one place that way.
        var today = new TodayControl(
            raceProgram,
            _raceEvents,
            _profiles,
            _raceClasses,
            _checkInStore,
            _daySchedule,
            // The line decoder, whichever it is - a split loop connected on
            // its own times no laps.
            LineDecoderConnected) { Dock = DockStyle.Fill };

        _todayControl = today;

        // The Ready to Race bar's one button: the Set Up Tonight wizard, at
        // the first step still to do.
        today.SetUpRequested += step => _openRaceDaySetup?.Invoke(step);

        // --- The timetable drawer ---
        //
        // The sheet is a few-times-an-evening job: set the times, add doors
        // and lunch, print it, arm the countdown. That is a drawer, the same
        // as the scrutineering log - and since the running order started
        // deriving from the programme, a tab for it was a third copy of a
        // list already on two screens.
        //
        // The one instance is moved into this window and taken back out on
        // the way, never rebuilt: its undo entries call RefreshAll on the
        // instance that captured them.
        Form? timetableHost = null;

        void ShowTimetable()
        {
            // Already open - bring it forward rather than stacking a second.
            if (timetableHost is { IsDisposed: false } already)
            {
                already.Activate();
                return;
            }

            // Moved in here: the page filling a window of its own with Close
            // under it, and Close or Esc to shut it - see PageWindow.
            using var host = PageWindow.Timetable(daySchedule);

            timetableHost = host;

            // Out of the window before it is disposed, or the page goes with
            // it and every undo entry it captured is left holding a control
            // that has been thrown away.
            host.FormClosed += (_, _) =>
            {
                host.GiveBackPage();
                timetableHost = null;
            };

            daySchedule.RefreshAll();
            host.ShowDialog(this);
        }

        // On the programme's footer now, with the other day tools.
        raceProgram.TimetableRequested += ShowTimetable;

        // "There is no programme yet - go and build one." Was a tab switch;
        // from inside a drawer it has to shut the drawer first, or the
        // window the director is being sent to is behind the one they are
        // looking at. Wired here rather than beside the page's other events
        // because it is the drawer's window it closes.
        daySchedule.GoToProgramTab += () =>
        {
            timetableHost?.Close();
            tabRaceDay.SelectedTab = subPageToday;
        };

        today.ConnectTimerRequested += () => GoToPage("Connection");

        BuildTodayWithTheProgramme(subPageToday, raceProgram, today);

        // Both halves, because they are one tab now. Today reads the
        // programme control for its next step, so the programme is refreshed
        // first - otherwise the card above the grid is a refresh behind it.
        _subTabRefresh[subPageToday] = () =>
        {
            raceProgram.RefreshAll();
            today.RefreshAll();
        };

        pageRaceDay.Controls.Add(tabRaceDay);
        pageChampionship.Controls.Add(tabChampionship);

        _navRefreshHooks["Race Day"] = () => RefreshSubTab(tabRaceDay);
        _navRefreshHooks["Championship"] = () => RefreshSubTab(tabChampionship);

        // Switching the current event switches what every scoped store
        // answers, so whatever page is showing re-reads. Wired once, here,
        // rather than each page having to know events exist.
        _raceEvents.CurrentChanged += (_, _) =>
        {
            if (!IsDisposed)
            {
                RefreshCurrentPage();

                // Today names the day in its heading and counts against
                // it, so it re-reads whether or not it is the tab being
                // looked at - switching the day while standing on Race
                // Control should not leave yesterday's name waiting on
                // Race Day.
                _todayControl?.RefreshAll();

                // The Track box follows the day that just opened, rather
                // than staying on whatever was used last. See
                // SyncTrackLayoutToRaceDay for what that was costing.
                //
                // Not while a race or practice is under way: the box is the
                // track the running race is filed under, and switching it to
                // the other day's layout moved the race and its records to a
                // track it was never run on. (The running round is kept the
                // same way - see RefreshActiveRoundCombo.)
                if (!RaceUnderWay)
                {
                    SyncTrackLayoutToRaceDay();
                }

                // And the sidebar, if this was a day opening or the last one
                // closing. A no-op when the grouping is unchanged.
                SyncRaceNightNav();
            }
        };

        // The day now open having its own settings saved - the Set Up
        // Tonight wizard writing the track it picked, The Day's layout box.
        // See OnCurrentDayEdited.
        _raceEvents.CurrentEdited += (_, _) =>
        {
            if (InvokeRequired)
            {
                TryPost(OnCurrentDayEdited);
            }
            else
            {
                OnCurrentDayEdited();
            }
        };

        return (raceProgram, achievements);
    }

    /// <summary>
    /// Rentals, and its badge in the sidebar.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildRentalsPage()
    {
        var rentals = new RentalTransponderControl(_rentals, _profiles, _undo) { Dock = DockStyle.Fill };
        pageRentals.Controls.Add(rentals);
        _navRefreshHooks["Rentals"] = rentals.RefreshAll;

        // Keep the sidebar count in step with the page, so outstanding units
        // are visible from wherever you happen to be working.
        rentals.OutstandingCountChanged += count => lstNav.SetBadge("Rentals", count);

        // WHY (23 Oct 2026, bug hunt 10): an online sign-up approval (or its undo)
        // lends or takes back a unit straight through the store; refresh the page
        // so it and the sidebar badge follow.
        _signupDesk.Changed += rentals.RefreshAll;

        // Set once up front as well - otherwise a unit left out overnight
        // wouldn't show a badge until someone happened to open the page.
        lstNav.SetBadge("Rentals", _rentals.OutNow.Count);
    }

    /// <summary>
    /// The scrutineering log drawer and Inspect, both off Who's Racing.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void WireScrutineeringLog(CheckInControl checkIn)
    {
        // Scrutineering is no longer a tab. What it held was the
        // scrutineer's record list, which a scrutineer opens a few times an
        // evening and nobody else opens at all; the gate itself has always
        // been the Tech column on Who's Racing. So it is a drawer off that
        // page now - "Scrutineering Log..." - and Race Day is three tabs.
        //
        // A fresh page per opening, and the one currently open is held in
        // techLog so the two places that have to redraw it can find it.
        //
        // It was one long-lived control moved from window to window, which is a
        // worse idea than it looks in WinForms: a themed scrollbar parks itself
        // as a SIBLING of the control it scrolls, so it belongs to the window,
        // and closing that window disposed a bar the page was still
        // subscribed to. The page's next ParentChanged then added a disposed
        // control to the next window, and showing it threw ObjectDisposedException
        // out of the message pump. ThinScrollBar refuses that now - a disposed
        // bar no longer re-parents, and a dead entry no longer blocks a new one -
        // but a page that never outlives its window cannot get into that state
        // in the first place.
        TechInspectionControl? techLog = null;

        void ShowScrutineeringLog()
        {
            var log = new TechInspectionControl(_techInspections, _profiles, _raceClasses, _undo)
            {
                Dock = DockStyle.Fill,
            };

            // The page filling a window of its own with Close under it, and
            // Close or Esc to shut it - see PageWindow.
            using var host = PageWindow.ScrutineeringLog(log);

            techLog = log;

            // Cleared on the way out, so nothing refreshes a page that has
            // gone. Both readers below are null-conditional for the ordinary
            // case: the log is shut, and there is nothing to redraw.
            host.FormClosed += (_, _) => techLog = null;

            host.ShowDialog(this);
        }

        checkIn.TechLogRequested += ShowScrutineeringLog;

        // Jumping from Check-In to record or edit a tech inspection without
        // leaving the check-in table - the whole point of surfacing tech
        // status there instead of merging the two records together.
        checkIn.InspectRequested += profile =>
        {
            // The class the desk's Tech column is showing, not always their
            // main one: a driver through tech in Stock and not in Modified
            // used to have their Stock pass opened, and switching it to
            // Modified replaced it.
            var className = checkIn.InspectClassFor(profile);
            var latest = _techInspections.Today(profile.Transponder, className);

            // Only today's inspection is opened for editing. A race day can
            // span a weekend, and the inspection that counts on Sunday
            // morning can be Saturday's - editing that one overwrote
            // Saturday's record with Sunday's look at the car, instead of
            // filing a new one for Sunday. The Tech Log still edits any
            // record picked from its list. "Today" also takes in the last
            // few hours, so a club night running past midnight still edits
            // the inspection made before twelve rather than starting over.
            var existing = BulkTech.EditableToday(latest, DateTime.UtcNow);

            using var form = existing is not null
                ? new TechInspectionEditForm(existing, _profiles, _raceClasses)
                : new TechInspectionEditForm(null, _profiles, _raceClasses, profile.Transponder, className);

            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            // Just this one record, not the whole log: undoing it must not
            // also throw away inspections recorded since.
            var before = existing is null ? null : _techInspections.CopyOf(existing.Id);
            var resultId = form.Result.Id;
            _undo.Capture($"{(existing is null ? "Record" : "Edit")} tech inspection for {profile.DriverName}", () =>
            {
                // Said out loud when it has been deleted since, rather than
                // logging "Undid" over a step that put nothing back.
                var putBack = _techInspections.UndoUpsert(resultId, before);
                techLog?.RefreshAll();
                checkIn.RefreshAll();

                if (!putBack)
                {
                    throw new InvalidOperationException("that inspection has been deleted since, so there is nothing to put back.");
                }
            });

            _techInspections.Upsert(form.Result);

            // Both pages show tech state, so both need to pick up the change
            // immediately rather than whichever one you happen to revisit -
            // the log only when it is actually open behind this dialog.
            techLog?.RefreshAll();
            checkIn.RefreshAll();
        };

        // One inspection for several cars at once (1 Nov 2026): the drivers
        // picked on Who's Racing with Ctrl or Shift. Each driver is inspected
        // in every class their Tech cell is waiting on (2 Nov 2026 - it was
        // only the first, so a two-class driver still read "-"), and today's
        // inspection of each car, when there is one, is updated as the
        // one-car Inspect above would - see BulkTech.Build.
        checkIn.InspectManyRequested += drivers =>
        {
            var nowUtc = DateTime.UtcNow;
            var cars = drivers
                .SelectMany(profile => checkIn.InspectClassesFor(profile)
                    .Select(className => new BulkTech.Car(profile, className,
                        BulkTech.EditableToday(_techInspections.Today(profile.Transponder, className), nowUtc))))
                .ToList();

            var names = drivers
                .Select(p => string.IsNullOrWhiteSpace(p.DriverName) ? p.Transponder : p.DriverName.Trim())
                .ToList();

            // Drivers with an inspection already today, since the window counts drivers.
            var alreadyToday = cars
                .Where(car => car.Existing is not null)
                .Select(car => car.Profile.Transponder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            using var form = new BulkInspectionForm(names, alreadyToday);

            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var records = BulkTech.Build(
                cars,
                form.Result,
                form.Inspector,
                form.Notes,
                className => _raceClasses.All
                    .FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase))?.TechCheckItems
                    ?? new List<string>(),
                DateTime.UtcNow);

            if (records.Count == 0)
            {
                return;
            }

            // Just these records, not the whole log: undoing them must not
            // also throw away inspections recorded since. One undo step for
            // the lot, as they were one press.
            var befores = records.Select(record => (record.Id, Before: _techInspections.CopyOf(record.Id))).ToList();

            // Drivers, not records: a two-class driver files two.
            var driverCount = records.Select(r => r.Transponder).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            _undo.Capture($"Record tech inspection for {Plural.Of(driverCount, "driver")}", () =>
            {
                var gone = 0;

                foreach (var (id, before) in Enumerable.Reverse(befores))
                {
                    if (!_techInspections.UndoUpsert(id, before))
                    {
                        gone++;
                    }
                }

                techLog?.RefreshAll();
                checkIn.RefreshAll();

                if (gone > 0)
                {
                    throw new InvalidOperationException(
                        $"{Plural.Of(gone, "of those inspections has", "of those inspections have")} been deleted since, so there is nothing to put back for {(gone == 1 ? "it" : "them")}.");
                }
            });

            foreach (var record in records)
            {
                _techInspections.Upsert(record);
            }

            OnClientRawMessage($"{AppTime.LogStampNow()}  [tech] Recorded \"{records[0].ResultText}\" for {Plural.Of(driverCount, "driver")} at once.");

            techLog?.RefreshAll();
            checkIn.RefreshAll();
        };
    }

    /// <summary>
    /// Handicaps.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildHandicapsPage()
    {
        // 17 Oct 2026, Bug Hunt 8, A26-1: the programme lets suggested
        // handicaps and "Recent best lap" use only the selected class's races.
        var handicaps = new HandicapControl(_handicaps, _profiles, _raceClasses, _sessionHistory, _undo, _rounds) { Dock = DockStyle.Fill };
        pageHandicaps.Controls.Add(handicaps);
        _navRefreshHooks["Handicaps"] = handicaps.RefreshAll;
    }

    /// <summary>
    /// Track Layouts and Race History.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    /// <summary>Race History, once built - for Race Control's Results and Edit Result.</summary>
    private RaceHistoryControl? _raceHistoryControl;

    /// <summary>
    /// The race just run - the one that finished last - in Race History, and
    /// with Edit Result open on it when <paramref name="edit"/> is set. Race
    /// Control's flag row after the flag (25 Oct 2026). Says so in the
    /// console when there is no race to show yet.
    /// </summary>
    private void ShowLatestRaceInHistory(bool edit)
    {
        if (_raceHistoryControl is null || _sessionHistory.All.MaxBy(s => s.EndedAtUtc) is not { } latest)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [results] No race in Race History to show yet.");
            return;
        }

        GoToPage("Race History");
        _raceHistoryControl.ShowSession(latest.Id, edit);
    }

    private void BuildTrackLayoutsAndRaceHistoryPages()
    {
        var trackLayouts = new TrackLayoutManagerControl(_trackLayouts, _settings, _undo, _sessionHistory) { Dock = DockStyle.Fill };
        pageTrackLayouts.Controls.Add(trackLayouts);
        _navRefreshHooks["Track Layouts"] = trackLayouts.RefreshAll;

        var raceHistory = new RaceHistoryControl(_sessionHistory, _undo, _settings, _rounds, _raceClasses, _profiles, _trackLayouts, _techInspections, _rentals, _handicaps, _raceEvents) { Dock = DockStyle.Fill };
        pageRaceHistory.Controls.Add(raceHistory);
        _raceHistoryControl = raceHistory;
        _navRefreshHooks["Race History"] = raceHistory.RefreshAll;

        // A driver's report card for the night of the race shown - the
        // right-click on a result (22 Oct 2026). See MainForm.Coach.
        // WHY (22 Oct 2026): a past night's setup line used to decide whose
        // car was whose by the race day open NOW, so tonight's loan could
        // move a setup change to the wrong driver. That is fixed where the
        // line is worked out (CoachSetupChange asks about the race day of the
        // night shown), so past nights keep their setup line here.
        raceHistory.ReportCardFor = (transponder, night) =>
            CoachReportFor(transponder, publicNames: false, includeSetup: true, night: night);
    }

    /// <summary>
    /// Achievements - handed back, because Practice awards badges through it.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private AchievementsControl BuildAchievementsPage()
    {
        var achievements = new AchievementsControl(_profiles, _settings) { Dock = DockStyle.Fill };

        // Where you race changed: the club website's badge list is sent
        // again on the next pass, with what is on offer now.
        achievements.RacingAtChanged += () => _lastRelayDocuments.Remove(RelayChannel.Badges);
        pageAchievements.Controls.Add(achievements);

        // Refreshed on entry, not just at construction: Race Control awards
        // badges while this page sits in the background, so a page built
        // once would show yesterday's picture forever.
        _navRefreshHooks["Achievements"] = achievements.RefreshAll;

        return achievements;
    }

    /// <summary>
    /// The Garage.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildGaragePage()
    {
        // The Garage, in the page Tuning used to have. Tuning logged a
        // setup against a driver's name and a typed car string; the Garage
        // is that with the car made real, and the setup log is the lower
        // half of it - so there is one page about a car rather than two
        // that half-know about each other.
        var garage = new GarageControl(
            _cars, _chassisTypes, _profiles, _raceClasses, _setupLog, _sessionHistory,
            _trackLayouts, _chassisTemplates, _undo, _raceEvents, batteries: _batteryPacks) { Dock = DockStyle.Fill };

        pageSetupLog.Controls.Add(garage);
        _navRefreshHooks["Garage"] = garage.RefreshAll;
    }

    /// <summary>
    /// Race Analytics and its six tabs.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildAnalyticsPage()
    {
        // --- Race Analytics ---
        //
        // Four separate nav items became one page with tabs, the same shape
        // as Sounds. They were always one job - "what does the history say"
        // - split four ways down the sidebar, and a question like "is he
        // quicker than me, and is he improving?" meant two round trips
        // through the nav to answer.
        _tabAnalyticsSubNav = new TabControl { Dock = DockStyle.Fill };

        // First tab: the one nobody has to be told to look at. Every other
        // tab answers a question you arrived with; this one answers the
        // question on the wall of every club - what is the record here, and
        // who holds it.
        var subPageOverview = new TabPage("Overview");
        // 17 Oct 2026, Bug Hunt 8, A30-2: the profiles, so the overview leaves
        // out the drivers the club has hidden, not only ghosts.
        var overview = new AnalyticsOverviewControl(_sessionHistory, _profiles);
        subPageOverview.Controls.Add(overview);

        var subPagePersonalBests = new TabPage("Personal Bests");
        var personalBests = new PersonalBestsControl(_sessionHistory, _pbResets, _undo) { Dock = DockStyle.Fill };
        subPagePersonalBests.Controls.Add(personalBests);

        var subPageLapTimes = new TabPage("Lap Times");
        var lapChart = new LapChartControl(_sessionHistory, _settings) { Dock = DockStyle.Fill };
        subPageLapTimes.Controls.Add(lapChart);

        var subPageHeadToHead = new TabPage("Head to Head");
        var headToHead = new HeadToHeadControl(_sessionHistory) { Dock = DockStyle.Fill };
        subPageHeadToHead.Controls.Add(headToHead);

        var subPageTrends = new TabPage("Trends");
        var trend = new TrendChartControl(_sessionHistory, _settings) { Dock = DockStyle.Fill };
        subPageTrends.Controls.Add(trend);

        // The same table Race Control opens after a race, with a picker on
        // it. Reading pace is not only a thing you do in the ten minutes
        // after a heat - "why was I slower last week" is the question this
        // answers best, and it needed a way in that was not "find the race
        // in History and open it".
        var subPagePace = new TabPage("Pace");
        var pace = new PaceAnalysisControl(_sessionHistory);
        subPagePace.Controls.Add(pace);

        _tabAnalyticsSubNav.TabPages.Add(subPageOverview);
        _tabAnalyticsSubNav.TabPages.Add(subPagePersonalBests);
        _tabAnalyticsSubNav.TabPages.Add(subPageLapTimes);
        _tabAnalyticsSubNav.TabPages.Add(subPageHeadToHead);
        _tabAnalyticsSubNav.TabPages.Add(subPageTrends);
        _tabAnalyticsSubNav.TabPages.Add(subPagePace);
        AppTheme.StyleTabControl(_tabAnalyticsSubNav);

        // Every one of these reads the session history, and races finish
        // while this page is in the background - so the tab being opened is
        // what refreshes it, not the page being built once at startup.
        void RefreshAnalyticsTab()
        {
            switch (_tabAnalyticsSubNav.SelectedTab)
            {
                case { } tab when tab == subPageOverview:
                    overview.RefreshAll();
                    break;
                case { } tab when tab == subPagePersonalBests:
                    personalBests.RefreshAll();
                    break;
                case { } tab when tab == subPageLapTimes:
                    lapChart.RefreshAll();
                    break;
                case { } tab when tab == subPageHeadToHead:
                    headToHead.RefreshAll();
                    break;
                case { } tab when tab == subPageTrends:
                    trend.RefreshAll();
                    break;
                case { } tab when tab == subPagePace:
                    pace.RefreshAll();
                    break;
            }
        }

        _tabAnalyticsSubNav.SelectedIndexChanged += (_, _) => RefreshAnalyticsTab();

        pageAnalytics.Controls.Add(_tabAnalyticsSubNav);

        // Opening the page refreshes whichever tab is showing, so arriving
        // from the sidebar is as current as switching tabs once here.
        _navRefreshHooks["Race Analytics"] = RefreshAnalyticsTab;
    }

    /// <summary>
    /// Next Steps: the day's card above the programme, split by a line the director can drag.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildTodayWithTheProgramme(TabPage subPageToday, RaceProgramControl raceProgram, TodayControl today)
    {
        // --- Today, with the programme under it ---
        //
        // This is the fold of the old Heats & Mains tab. That page had two
        // halves: a next-step card that printed the same sentence Today's
        // card prints, because both ask ProgramNextStep, and a grid of the
        // rounds with the commands that act on one. The card was duplication;
        // the grid was the only place in the app that showed the running
        // order and let somebody start a round from it. So the card comes off
        // and the grid moves here, under the card that was already saying
        // what to do next.
        //
        // A splitter rather than a fixed stack, because which half matters
        // changes through the evening. Before the first heat the checklist is
        // the page and there is no programme to look at; by the third round
        // the checklist is six ticks and the grid is the page. Rather than
        // guess, the line is the director's to drag, and where they leave it
        // is remembered.
        //
        // The programme page is built for this half now - no next-step banner
        // of its own, no height floor - so there is nothing to switch off.

        // How much of the page each half wants. Measured rather than written
        // down, because what "enough to be useful" costs in pixels depends
        // entirely on the text-size setting and the width.
        //
        // The two are not equals. The program's least is a floor: its class
        // filter, the headings and three rounds, and the buttons under them
        // (RaceProgramControl.LeastPaneHeight) - any shorter and the buttons
        // go below the fold, and the program is the half a race night is
        // run from. The top half gives way instead, and scrolls: on a small
        // screen or at a large text size, what it cannot show is under its
        // own scrollbar rather than under the splitter. The top's least is
        // only where it OPENS when there is room for both - the day's name,
        // the card's headline and its button.
        //
        // These are NOT Panel1MinSize and Panel2MinSize, and that is the whole
        // point of this comment. Assigning those properties validates
        // immediately against the size the control happens to be at the time,
        // and a SplitContainer that has not been laid out yet is 150x100 - so
        // setting a 228px Panel2MinSize in the initializer threw
        // "SplitterDistance must be between Panel1MinSize and Width -
        // Panel2MinSize" out of the constructor, before the window ever
        // appeared. They are applied by moving the splitter instead, once
        // there is room for them, and the panels keep WinForms' own 25px
        // minimums - which are small enough to survive any window size, and
        // still stop either half being dragged away to nothing.
        //
        // Asked each time rather than worked out once, so they follow the
        // text size and the width as the two halves do.
        static int TodayTopMin() =>
            AppTheme.TitleFont.Height + AppTheme.BodyFont.Height
            + AppTheme.HeadingFont.Height + UiLayout.ButtonHeight
            + UiLayout.SectionGap * 2;

        var todaySplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,

            // Panel1 keeps its height when the window is resized and the grid
            // takes the slack. That is the right way round - a taller window
            // does not want a taller checklist - and it also means resizing
            // never moves the splitter, so nothing overwrites the saved
            // position behind the director's back.
            FixedPanel = FixedPanel.Panel1,

            SplitterWidth = 6,
        };

        // The bar between the two halves. The SplitContainer itself is not a
        // Panel, so the recursive theming pass never reaches its BackColor -
        // left alone it draws the system grey, which is a bright line across
        // the page in either dark theme.
        todaySplit.BackColor = AppTheme.Border;

        // Both halves are built on the layout pieces, and nothing else moves
        // them. The layout repair pass that used to run over every page (it
        // was deleted on 30 Sep) once had to be told to keep out of this
        // split in particular - a SplitContainer is a scrollable control, and
        // caught mid-resize with a half hanging past its edge, the pass
        // turned scrolling on for the WHOLE split, which put a second pair of
        // scrollbars round the entire tab.

        todaySplit.Panel1.Controls.Add(today);
        todaySplit.Panel2.Controls.Add(raceProgram);

        // The program's least, at the width the split gives it - the lower
        // half is as wide as the split - and never under the panel's own
        // 25px, which the arithmetic below relies on to stay in range.
        int TodayBottomMin() => Math.Max(todaySplit.Panel2MinSize, raceProgram.LeastPaneHeight(todaySplit.Width));

        // The most the top half can have and still leave the program its
        // least. Under the top half's own 25px when the window is too short
        // for both, which the callers check for.
        int TodayTopMost() => todaySplit.Height - todaySplit.SplitterWidth - TodayBottomMin();

        // Placed on the first resize that has room for it, not here: a
        // SplitterDistance assigned before the control has a height is either
        // ignored or throws, depending on how far out it is.
        var todaySplitPlaced = false;

        // Set while the splitter is moved from here rather than by hand, so
        // only a position the director chose is remembered. Moved to fit -
        // the window made smaller for a minute, the text size tried out - it
        // would otherwise overwrite the one they dragged it to.
        var todaySplitFitting = false;

        void MoveTodaySplitter(int distance)
        {
            if (todaySplit.SplitterDistance == distance)
            {
                return;
            }

            todaySplitFitting = true;

            try
            {
                todaySplit.SplitterDistance = distance;
            }
            finally
            {
                todaySplitFitting = false;
            }
        }

        void PlaceTodaySplitter()
        {
            if (todaySplitPlaced)
            {
                return;
            }

            var most = TodayTopMost();

            // Not enough room for the program's least and a strip of the top
            // half over it: leave WinForms to it and try again on the next
            // resize. Nothing is assigned until the arithmetic below is known
            // to be in range.
            if (most < todaySplit.Panel1MinSize)
            {
                return;
            }

            // Until the director has dragged the line, as tall as the top
            // half's content (see FitTodayToContent) - or 55% when that is
            // not known yet, measured rather than written down: the
            // summary's real height depends on the text-size setting, and any
            // pixel count decided here would be right at one setting only.
            // Never so far down that the program loses its least; never
            // higher than the top's least while there is room for that.
            var room = todaySplit.Height - todaySplit.SplitterWidth;
            var saved = _settings.TodayProgrammeSplit;
            var wanted = saved > 0 ? saved : today.ContentHeight > 0 ? today.ContentHeight : room * 55 / 100;

            MoveTodaySplitter(Math.Clamp(wanted, Math.Min(TodayTopMin(), most), most));

            todaySplitPlaced = true;

            FitTodayToContent();
        }

        // The program is always tall enough for its buttons: when it is not
        // - the window made smaller, the buttons wrapping onto a second line,
        // the line dragged too far down - the top half gives way, and
        // scrolls. Down to its own 25px and no further, which only a window
        // too short for anything reaches.
        void KeepProgramInView()
        {
            if (!todaySplitPlaced || todaySplit.IsDisposed)
            {
                return;
            }

            var most = Math.Max(todaySplit.Panel1MinSize, TodayTopMost());

            if (todaySplit.SplitterDistance > most && todaySplit.Height - todaySplit.SplitterWidth - todaySplit.Panel2MinSize >= most)
            {
                MoveTodaySplitter(most);
            }
        }

        // The top half is tall enough to show all of Next Steps - the card,
        // Ready to Race and the tiles - without scrolling, whenever that
        // leaves the program its least.
        //
        // Until the director drags the line, it is exactly that tall: the
        // program gets everything else, rather than a blank band opening
        // under the tiles at 55% of a tall window (layout pictures, 29 Sep).
        // Once they have, it only ever GROWS the half, so a director who
        // drags the line down to give the program less room keeps that;
        // dragging it up past the content is what is undone, the next time
        // the content changes or the window is resized.
        void FitTodayToContent()
        {
            if (!todaySplitPlaced || todaySplit.IsDisposed)
            {
                return;
            }

            var most = TodayTopMost();

            if (most >= todaySplit.Panel1MinSize && today.ContentHeight > 0)
            {
                var wanted = Math.Clamp(today.ContentHeight, todaySplit.Panel1MinSize, most);
                var dragged = _settings.TodayProgrammeSplit > 0;

                if (dragged ? todaySplit.SplitterDistance < wanted : todaySplit.SplitterDistance != wanted)
                {
                    MoveTodaySplitter(wanted);
                }
            }

            KeepProgramInView();
        }

        today.ContentHeightChanged += FitTodayToContent;
        raceProgram.LeastPaneHeightChanged += FitTodayToContent;

        todaySplit.Resize += (_, _) =>
        {
            PlaceTodaySplitter();
            FitTodayToContent();
        };

        // And when the page is first shown, in case the sidebar page it lives
        // on was never laid out at a real height while it was hidden. Both
        // hooks are harmless once the splitter has been placed - the method
        // returns immediately after that.
        todaySplit.VisibleChanged += (_, _) => PlaceTodaySplitter();

        todaySplit.SplitterMoved += (_, _) =>
        {
            if (!todaySplitPlaced || todaySplitFitting)
            {
                return;
            }

            _settings.TodayProgrammeSplit = todaySplit.SplitterDistance;
            _settings.Save();

            // Dragged so far down that the program's buttons would go below
            // the fold: back up to where they show, once the drag is over -
            // not from inside the splitter's own event. WinForms moves the
            // splitter itself too, when the window is made too short for the
            // top half, and that can be before the window has a handle.
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(KeepProgramInView));
            }
        };

        subPageToday.Controls.Add(todaySplit);
    }

    /// <summary>
    /// Ghost Cars, Practice and Demo Mode. The Ghost Cars page is made here rather than in the designer, so it is handed back for the sidebar's list.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private Panel BuildPracticeGhostsAndDemoPages(RaceControlControl raceControl, RaceProgramControl raceProgram, AchievementsControl achievements)
    {
        var pageGhosts = new Panel();
        ConfigurePage(pageGhosts);
        StartupMark("Race Analytics, Championship and Next Steps");

        var ghostControl = new GhostControl(_ghostStore, _profiles, _sessionHistory, _undo) { Dock = DockStyle.Fill };
        pageGhosts.Controls.Add(ghostControl);

        // Re-read each time the page opens: the Practice page switches ghosts
        // on and off in the same store (its "Race ghost cars" box and the
        // tick beside each ghost), and its "Manage..." button brings the
        // club straight here, so without this the page showed the states
        // from when it was built. OnGhostStoreSaved runs every hook after
        // each ghost save as well, this page's own saves included; that is
        // harmless, because RefreshAll copies the stored values onto boxes
        // that already hold them (no change event) and redraws the grid the
        // page was about to redraw anyway.
        _navRefreshHooks["Ghost Cars"] = ghostControl.RefreshAll;

        var practice = new PracticeModeControl(_race, _profiles, _sessionHistory, _trackLayouts, _settings, _ghostStore, _announcer, _daySchedule, _sponsors, undo: _undo,
            // The Race page's projection and badges, so a Big Screen opened
            // from Practice keeps up with the races run since. Read through
            // the property each time: the badge store is replaced on unlock.
            projector: raceControl.Projector, achievements: () => raceControl.Achievements) { Dock = DockStyle.Fill };

        // What the home and garage badges read, for races and (at a home
        // track) practice alike; and each driver's pack in the car recorded
        // on a saved practice, as Race Control records it on a race.
        var badgeStores = new AchievementContext
        {
            Setups = _setupLog,
            Cars = _cars,
            Packs = _batteryPacks,
            GhostCars = () => _ghostStore.Current.Ghosts.ToList(),
            GhostsJoinedLate = GhostsSwitchedOnMidRace,
        };

        raceControl.AchievementStores = badgeStores;
        practice.AchievementStores = badgeStores;
        practice.HasUnfiledRaces = () => _raceFiling.HasUnfiledRaces;
        practice.BatteryPacks = _batteryPacks;
        practice.StatusMessage += OnClientRawMessage;
        practice.BadgesAwarded += raceControl.ReloadAchievements;
        // Ghost profiles kept in step as ghosts are added, edited and
        // deleted - so a new ghost is immediately pickable everywhere.
        _ghostStore.Saved += _ => OnGhostStoreSaved();

        // Run once at startup as well, so ghosts saved before this existed
        // get their profiles without needing to be edited first.
        EnsureGhostProfiles();

        raceControl.DeepDuckRequested += deep =>
        {
            // The release is always honored. The setting only decides
            // whether a recap gets the deeper duck in the first place: it
            // used to gate both ends, so unticking it on the Commentary page
            // while a recap was playing dropped the release, and the recap's
            // depth stayed on for every announcement until the app restarted.
            // Clearing an override that was never set is harmless.
            if (!deep)
            {
                _ambience.SetDuckDepthOverride(null);
                _music.SetDuckDepthOverride(null);
                return;
            }

            if (!_settings.SummaryDeepDuck)
            {
                return;
            }

            var fraction = Math.Clamp(_settings.SummaryDuckPercent, 2, 100) / 100f;

            _ambience.SetDuckDepthOverride(fraction);
            _music.SetDuckDepthOverride(fraction);
        };

        // Reset stops everything (26 Oct 2026): Race Control stops what it
        // puts out, and the music and the crowd - which live here - go with
        // it. See StopMusicAndCrowd.
        raceControl.EverythingStopped += () =>
        {
            if (InvokeRequired)
            {
                TryPost(() => StopMusicAndCrowd("Race reset"));
                return;
            }

            StopMusicAndCrowd("Race reset");
        };

        raceControl.SessionAudioFinished += () =>
        {
            if (InvokeRequired)
            {
                TryPost(HandleSessionAudioFinished);
                return;
            }

            HandleSessionAudioFinished();
        };

        practice.GhostsPageRequested += () => GoToPage("Ghost Cars");

        // Practice writes its own lap and time targets into the shared
        // session while it runs and hands the old ones back when it stops.
        // Race Control's format line is re-read then, so it shows what the
        // next race will actually use rather than what was there before.
        practice.PracticeStopped += _raceControlControl.RefreshFormat;

        // Threaded, the timing thread's gate-pass plan follows the page at
        // once (24 Oct 2026, timing layer, P7) - see RefreshPracticeAutoStartPlan.
        practice.AutoStartConditionsChanged += RefreshPracticeAutoStartPlan;
        _practiceControl = practice;
        pagePractice.Controls.Add(practice);
        _navRefreshHooks["Practice"] = practice.RefreshAll;

        var demoMode = new DemoModeControl(_race, _announcer, _profiles, _trackLayouts, _settings, _rounds, _raceEvents, _ghostStore) { Dock = DockStyle.Fill };

        // A demo stages the session itself, so Race Control's show before
        // the start is run from here (18 Oct 2026) - see
        // RaceControlControl.ShowAtDemoStaging.
        demoMode.ShowAtStaging = cars => _raceControlControl.ShowAtDemoStaging(cars);
        _demoControl = demoMode;

        // Race Control ends a running demo through this before it stages,
        // starts or resets over it, so the simulator lets go of the session
        // and its queued lap calls instead of carrying on inside the real race.
        _raceControlControl.StopRunningDemo = demoMode.StopForTakeover;

        // A demo stopped by hand gives up its claim on the round at once
        // (20 Oct 2026), so the format chip and Next Up go back to what Race
        // Control would really run - see DemoModeControl.DemoStopped.
        demoMode.DemoStopped += () =>
        {
            _raceControlControl.DemoRound = null;
            _raceControlControl.RefreshFormat();
        };
        pageDemoMode.Controls.Add(demoMode);
        _navRefreshHooks["Demo Mode"] = demoMode.RefreshAvailableData;

        // The Demo Mode page's own Start, through On Air like the Race
        // Program's "Run as Demo" below (17 Oct 2026) - it used to be the
        // one demo that skipped it. Straight to the demo page's own start
        // over a real race staged or running, which refuses it in its own
        // words; On Air would otherwise play its jingle over that race
        // first. See RaceControlControl.GoOnAirThenAsync.
        demoMode.GoOnAirThen = async (round, start) =>
        {
            // The Demo page's own demo: its round when one is picked, or none
            // for a demo of the checked drivers - which also lets go of any
            // round an earlier demo left behind. See RaceControlControl.DemoRound.
            _raceControlControl.DemoRound = round;
            _raceControlControl.RefreshFormat();

            if (!_race.IsDemoMode && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                start();
            }
            else
            {
                await _raceControlControl.GoOnAirThenAsync(round, start);
            }

            // A demo that never started lets its round go (22 Oct 2026, bug
            // hunt), as Run as Demo does below: refused, called off or
            // refused over a real race, it left the demo's format on Race
            // Control's chip, Next Up and Edit Format until the next Stage.
            if (!_race.IsDemoMode)
            {
                _raceControlControl.DemoRound = null;
                _raceControlControl.RefreshFormat();
            }
        };

        // Wired here rather than beside RoundStartRequested above, because
        // demoMode doesn't exist yet at that point in this method.
        raceProgram.RoundDemoRequested += async round =>
        {
            // A demo drives the same session practice is running on, and
            // would wipe the practice laps just as a real start would.
            if (_raceControlControl.RefuseIfPracticeRunningForRaceStart("run a demo"))
            {
                return;
            }

            // Same for a race being started or run: the demo page refuses
            // it too, but only after the pickers below had already been
            // moved off the round the race is using. Checked here first,
            // with the demo page's own words. A demo already running is
            // not refused - the simulator stops and restarts it.
            if (!_race.IsDemoMode && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                AppDialog.Show(this, "A race is running. Starting a demo would wipe it. Finish it or reset it on Race Control first.",
                    "Can't start a demo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // WHETHER THE DEMO CONSUMES THE ROUND IS THE CLUB'S CALL NOW
            // (13 Oct 2026).
            //
            // This used to drop to freeform always, so the demo's results
            // could never save against a real heat. That is right for a demo
            // run on its own - and it is exactly what made a whole night
            // impossible to rehearse, because a heat that never marks itself
            // run gives qualifying nothing to read and the mains nothing to
            // seed from.
            //
            // With the box ticked the round stays selected and the night
            // fills in as a real one would. It still scores nothing: every
            // record is guarded by IsDemoSession where it is computed, and
            // RaceFiling refuses to let a demo land on a heat that really
            // ran. See AppSettings.DemoFillsTheProgramme.
            if (!_settings.DemoFillsTheProgramme)
            {
                cmbActiveRound.SelectedIndex = 0;
            }
            else
            {
                // Selected the same way every other restore in MainForm
                // does it - by round id over RoundComboItem, because the
                // index means nothing once the list is rebuilt.
                //
                // The box only holds rounds that have NOT been run, so a
                // round being demoed a second time is not in it. Freeform
                // then, and said plainly: the alternative is a demo that
                // silently saves against whatever happens to be selected.
                var picked = false;

                for (var i = 0; i < cmbActiveRound.Items.Count; i++)
                {
                    if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == round.Id)
                    {
                        cmbActiveRound.SelectedIndex = i;
                        picked = true;
                        break;
                    }
                }

                if (!picked)
                {
                    cmbActiveRound.SelectedIndex = 0;

                    OnClientRawMessage($"{AppTime.LogStampNow()}  [program] \"{round.RoundLabel}\" has already been "
                        + "run, so this demo runs it again - same drivers, format and show - and keeps its earlier "
                        + "result as it is.");
                }
            }

            // Refresh first: the driver list has to know about this round's
            // drivers before StartDemoForRound can tick them.
            demoMode.RefreshAvailableData();

            // Race Control rather than the Demo Mode page - a demo drives
            // the same session, leaderboard and announcer as a real race,
            // so that's where someone actually wants to be watching.
            GoToPage("Race Control");

            // THROUGH ON AIR (16 Oct 2026), not straight to staging: the
            // jingle or sweeper, the opening for this round and a bumper,
            // and then the demo starts and stages itself - the same top of
            // the race a real one gets from the On Air button. Straight to
            // the demo when Start races On Air is unticked (17 Oct 2026). See
            // RaceControlControl.GoOnAirThenAsync.
            // THE DEMO IS THAT RACE AGAIN (18 Oct 2026), even with the Round
            // box on freeform - a round already run, or "Demo races fill the
            // program" unticked: its format, its grid and its show (round
            // intro, driver introductions, Team Principal, Technical Lead)
            // all come from the round. Only the filing stays freeform, which
            // is what keeps a demo off a heat that really ran. See
            // RaceControlControl.DemoRound and ShowRound.
            _raceControlControl.DemoRound = round;
            _raceControlControl.RefreshFormat();

            if (!string.IsNullOrWhiteSpace(round.TrackLayoutName)
                && cmbTrackLayout.Items.IndexOf(round.TrackLayoutName) is var layoutAt and >= 0
                && cmbTrackLayout.Enabled)
            {
                cmbTrackLayout.SelectedIndex = layoutAt;
            }

            await _raceControlControl.GoOnAirThenAsync(round, () =>
            {
                // Again, in case anything on the way through On Air ended an
                // earlier demo and let go of it.
                _raceControlControl.DemoRound = round;
                _raceControlControl.RefreshFormat();

                demoMode.StartDemoForRound(round);

                OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Running \"{round.RoundLabel}\" as a demo.");
            });

            // Called off, or refused: no demo is running for this round, so
            // nothing after it should be run as it.
            if (!_race.IsDemoMode)
            {
                _raceControlControl.DemoRound = null;
                _raceControlControl.RefreshFormat();
            }
        };

        return pageGhosts;
    }

    // The demo that has the commentary's night, and how many demos have
    // had one since the app opened - see DemoRunForTheNight.
    private int _demoRuns;
    private bool _onADemoNight;
    private bool _wasADemo;

    /// <summary>
    /// Which demo has the commentary's night, or null for the real race
    /// day (3 Nov 2026) - see CommentaryDirector.NewMeeting.
    ///
    /// A demo starts a fresh night of its own, and the night stays the
    /// demo's through its recap and the gap after it, until a real race is
    /// staged. Read on the announcer's tick, like RaceDayId.
    /// </summary>
    private int? DemoRunForTheNight()
    {
        // A programme demo is set before On Air and cleared at its recap;
        // a Demo page race only flags the session while it runs.
        var demo = _race.IsDemoMode || _raceControlControl.DemoRound is not null;

        if (demo && !_wasADemo)
        {
            _demoRuns++;
            _onADemoNight = true;
        }
        else if (!demo && _onADemoNight && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            _onADemoNight = false;
        }

        _wasADemo = demo;

        return _onADemoNight ? _demoRuns : null;
    }

    /// <summary>
    /// Voice and its four tabs, and the announcer's wiring to Race Control.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private AnnouncerControl BuildVoicePage(RaceControlControl raceControl)
    {
        // Voice Announcements hosts a small internal sub-nav: general
        // voice settings, and Rivalries (custom lines for specific driver
        // matchups) - grouped together since rivalries are fundamentally
        // a voice-announcement feature, not a standalone concern.
        // Everything to do with the app SPEAKING lives here: the voice
        // itself, the AI that writes lines for it, the live announcer view,
        // and per-driver rivalry lines. Announcer and Commentary were each
        // a top-level nav item, which scattered one concern across three
        // places in the sidebar.
        //
        // THE VOICE REGROUP (17 Oct 2026): who speaks and what they say, in
        // four tabs - the voice itself, the cast (everyone else with a name
        // and a voice), the race calls (the announcement rows, how often,
        // and the rivalries, which were a tab of their own), and what the
        // AI writes. The names are the tabs' captions; NavSearchTabAliases
        // matches them exactly.
        _tabVoiceSubNav = new TabControl { Dock = DockStyle.Fill };
        var subPageVoiceSettings = new TabPage("Voice");
        var subPageCast = new TabPage("Cast");

        // The thirty announcement rows used to be the last group at the
        // bottom of Voice Settings - below the engine, the connection, the
        // tuning and the line library - so the longest thing on the page was
        // also the furthest below the fold. "Announcements" until 17 Oct 2026.
        var subPageAnnouncements = new TabPage("Race Calls");
        var subPageCommentary = new TabPage("AI Commentary");
        _tabVoiceSubNav.TabPages.Add(subPageVoiceSettings);
        _tabVoiceSubNav.TabPages.Add(subPageCast);
        _tabVoiceSubNav.TabPages.Add(subPageAnnouncements);
        _tabVoiceSubNav.TabPages.Add(subPageCommentary);
        AppTheme.StyleTabControl(_tabVoiceSubNav);

        // Each tab as wide as its own caption, as the Sounds row is -
        // "AI Commentary" is the longest caption the row has had.
        AppTheme.SizeTabsToCaptions(_tabVoiceSubNav);

        // Simple mode (1 Nov 2026) hides Race Calls; the Race calls switches
        // on the Voice tab turn them all on or off (VoiceProfile.RaceCallsInRaces).
        SimpleMode.HideTab(_tabVoiceSubNav, subPageAnnouncements);

        // The rivalries: the last section of Race Calls since 17 Oct 2026,
        // put there below once the announcement rows exist. Built here and
        // kept for the life of the app - the Race Calls page around them is
        // rebuilt after a soundscape is applied, and they are moved across
        // (RebuildVoiceAndSoundTabs).
        var rivalries = new RivalriesControl(_rivalryStore, _profiles, _undo)
        {
            Dock = DockStyle.Fill,

            // Rivalry lines are spoken by the announcer, so the HeatCaller
            // voice fixes them as it fixes the ones on the Voice tab.
            WordingIsFixed = () => _voiceStore.Current.EffectiveEngine == VoiceEngine.HeatCaller,
        };

        // Built once and kept for the life of the app, but NOT parented
        // anywhere yet: it lives in a window of its own now, opened from
        // Race Control. Built here all the same, because the talking-point
        // feed it gathers has no store behind it - a control built when the
        // window opens would start every race with an empty page and lose
        // the lot each time the window was closed.
        // WHY (23 Oct 2026, bug hunt 10): the director's personal-best check honours the club's resets.
        _announcerControl = new AnnouncerControl(_race, _profiles, _sessionHistory, _daySchedule, _announcer, _commentary, _settings, _undo) { Dock = DockStyle.Fill, PersonalBestResets = _pbResets };
        var announcer = _announcerControl;

        // The broadcast segments' own console lines. Every refusal says
        // why, which is the only thing that makes a quiet race night
        // explainable afterwards.
        announcer.StatusMessage += OnClientRawMessage;

        // Which race day the night's spend and its facts belong to. Read
        // on every tick rather than once, so starting a new day mid-session
        // starts a new night rather than carrying the last one's budget.
        announcer.RaceDayId = () => _raceEvents.Active?.Id ?? string.Empty;
        announcer.DemoRun = DemoRunForTheNight;

        // And the gap after the recap show, when On Air opens the next race:
        // the director stands down in it rather than adding a hand-over of
        // its own (17 Oct 2026). See RaceControlControl.OnAirFollowsTheRecap.
        announcer.OnAirOpensTheNextRace = () => _raceControlControl.OnAirFollowsTheRecap;

        // And the other way: Race Control shows what the announcer is
        // about to say, and holds it when the race director needs the mic.
        // Wired here because this is the first point at which both exist.
        _raceControlControl.Commentary = () => announcer.Director;

        // And the show's music, for the recap: Race Control runs that
        // sequence itself, so it opens and ends the show rather than
        // handing a line to the director.
        _raceControlControl.ShowMusic = () => announcer.ShowMusicTreatment;

        // And the session's music and crowd, opened silent when On Air is
        // pressed rather than at staging, where opening them skipped the
        // broadcast - see ReadySessionAudioForOnAir.
        _raceControlControl.OnAirStarting = ReadySessionAudioForOnAir;

        // And tonight's club notes, for the Notes button on the announcer
        // strip - the approval list for anything the room sent in. The
        // same single instance the announcer's director reads from, so a
        // note approved in that window reaches the announcer rather than a
        // second copy of the file.
        _raceControlControl.TonightsNotes = () => _clubNotes;

        // And tonight's room vote, off the Votes button beside it. One
        // instance again: the window opens and closes the poll the phones
        // are voting in and the announcer reads the verdict from.
        _raceControlControl.TonightsVote = () => _roomVote;

        announcer.RaceClassName = () => _activeRound?.ClassName;

        // And how big that round is, which is what paces the whole
        // broadcast - quieter in practice and the heats, everything it held
        // back spent on the top main. Read fresh on each tick rather than
        // captured, so seeding the mains mid-evening re-sizes the rest of
        // the night. See RaceStature. Scoped to the open race day, so a
        // round set up for another day is never tonight's last.
        announcer.RaceSize = () => RaceStature.Of(_activeRound, _rounds, _raceEvents.ActiveRoundScope());

        WireCommentaryReaders(announcer.Director);

        // One click each way between the only two screens anybody is on
        // while a race is running.
        //
        // Wired here rather than where Race Control is built, because this
        // is the first point at which BOTH exist. Neither control knows
        // what pages there are or how to reach one - they raise an event
        // and this decides what that means, which is the only place that
        // knows.
        // Quick Race drops the pickers back to a plain freeform Race first,
        // so the session neither runs under some selected round's format
        // nor records itself against that round. Round first: selecting
        // freeform is what re-enables the session-type combo.
        _raceControlControl.QuickRaceSetupRequested += () => cmbActiveRound.SelectedIndex = 0;

        // The format follows the round: pick a different round and the
        // chip - and the session limits, if nothing is running - follow.
        cmbActiveRound.SelectedIndexChanged += (_, _) => _raceControlControl.RefreshFormat();

        _raceControlControl.AnnouncerRequested += OpenAnnouncerWindow;
        _raceControlControl.ChaseOwnBest = ChaseOwnBestFromRaceControl;
        _raceControlControl.OwnBestGhosts = () => OwnBestGhost.ChasedBy(_ghostStore.Current);
        _raceControlControl.MissionControlRequested += ShowMissionControl;

        // Brings Race Control forward without closing the announcer's
        // window. It used to be a tab, so "Race Control" meant leaving the
        // announcer's page; now the two can be side by side and this only
        // has to say which one has the keyboard.
        announcer.RaceControlRequested += () =>
        {
            GoToPage("Race Control");

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
        };

        var commentary = new CommentaryControl(_settings, _commentary)
        {
            Dock = DockStyle.Fill,
            OpenThisMonth = ShowThisMonth,

            // Whether a Cast voice is set, for the Simple view's section
            // switches (1 Nov 2026 - see CommentarySections).
            AnnouncerEngine = () => _voiceStore.Current.EffectiveEngine,
        };
        _commentaryControl = commentary;
        subPageCommentary.Controls.Add(commentary);

        // CAST (17 Oct 2026): the names and voices that were on the
        // Commentary page - the announcer's name, the recap host, the
        // co-host, the mechanic and the badges reader.
        var cast = new CastControl(_settings) { Dock = DockStyle.Fill };
        subPageCast.Controls.Add(cast);
        _castControl = cast;

        // The recap's broadcast: the Garage's record of the winning car for
        // the mechanic's interview, and the cast's voice lists - see
        // MainForm.Commentary.cs.
        WireRecapBroadcast(raceControl, commentary, cast);

        // The show's music and the recording of the night moved to Sounds
        // on 17 Oct 2026 (Imaging's Show treatment card, and the Recording
        // tab); they are wired where those are built, below - WireShowMusic
        // and WireTheDriversNight in MainForm.Commentary.cs.

        // Club notes, the room's vote and listening along - and the three
        // "closed on the relay" events - are on the Club Website page's
        // Room & Web card since 17 Oct 2026, and wired where that page is
        // built, below.

        // THE ANNOUNCER'S HALF of listening along (13 Oct 2026). Two hooks
        // rather than the audio service reaching for the publisher: it is
        // told whether anybody may listen, so a club with this off pays
        // nothing per line, and where to put a line once it is an mp3. The
        // encoding happens in between, on a pool thread - see
        // AnnouncementService.ListenAlong for why none of it may happen on
        // the way to the speaker.
        _announcer.ListenersWanted = () => _settings.ListenAlongOnTheRelay && _settings.RelayEnabled;
        _announcer.SendToListeners = (said, mp3) =>
            _relayPublisher.OfferToListeners(_raceEvents.Active?.Id, said, mp3, DateTime.UtcNow);

        // And the whole room rather than only the announcer (2 Nov 2026):
        // the music, the crowd and the imaging under the lines, as one
        // stream. While it is running a line is not sent on its own - it is
        // heard in the stream, or kept off it - and only its words go across
        // for the page to show. See ListenStream.
        _listenStream ??= new ListenStream(
            () => _settings.ListenAlongOnTheRelay && _settings.RelayEnabled,
            segment => _relayPublisher.OfferStreamToListeners(_raceEvents.Active?.Id, segment),
            OnClientRawMessage);
        _announcer.StreamingToListeners = () => _listenStream.Streaming;
        _announcer.CaptionToListeners = _listenStream.Caption;

        // And a club who has never been asked is put onto the show music
        // that ships with the app - once, here, where the app is known to
        // be starting rather than a page being previewed. See
        // StartShowMusicFromTheLibrary.
        StartShowMusicFromTheLibrary(_settings);

        // And the relay's count of the club's month, which the night's
        // budget paces against - see MainForm.Commentary.cs.
        WireCommentaryAllowance(announcer, commentary);

        // The model client's "[llm] ..." lines, which Race Control's "any
        // [llm] line above says why" points at. Stamped here, as the client
        // sends them bare; OnClientRawMessage is safe from any thread.
        commentary.ClientStatusMessage += msg => OnClientRawMessage($"{AppTime.LogStampNow()}  {msg}");

        // The rivalries read the driver list, which changes while the app
        // runs, so they refresh on tab entry rather than only at
        // construction - on Race Calls, where they are now. The announcer
        // used to be refreshed here too; it is a window now and
        // OpenAnnouncerWindow refreshes it on the way in. (Cast and AI
        // Commentary set themselves out again whenever they are shown.)
        _tabVoiceSubNav.SelectedIndexChanged += (_, _) =>
        {
            if (_tabVoiceSubNav.SelectedTab == subPageAnnouncements)
            {
                rivalries.RefreshDriverCombos();
            }
        };

        // The sound effects' store as well, for the two streak thresholds on
        // Race Calls' How often card (17 Oct 2026) - they are the effects
        // profile's, and decide the voice's streak call as well.
        var voiceSettings = new VoiceSettingsControl(_voiceStore, _settings, _undo, _soundStore) { Dock = DockStyle.Fill, Announcer = _announcer, HeatCallerVoice = _heatCallerVoice };
        voiceSettings.StatusMessage += OnClientRawMessage;
        subPageVoiceSettings.Controls.Add(voiceSettings);
        _voiceSettingsControl = voiceSettings;
        _voiceSettingsSubPage = subPageVoiceSettings;

        // The announcer's voice picker and Test Voice, built by the Voice tab
        // and shown at the top of Cast (22 Oct 2026) - see
        // CastControl.HostAnnouncerVoice, and RebuildVoiceAndSoundTabs for
        // handing them over again when a soundscape rebuilds this tab.
        cast.HostAnnouncerVoice(voiceSettings);

        // The announcement rows, built by the same control so they stay part
        // of the one voice profile it saves, but parented here so they get a
        // tab of their own - Race Calls. See VoiceSettingsControl.Announcements.cs.
        subPageAnnouncements.Controls.Add(voiceSettings.AnnouncementsPage);
        _voiceAnnouncementsSubPage = subPageAnnouncements;

        // And the rivalries as its last section - there is no Rivalries tab
        // any more (17 Oct 2026).
        voiceSettings.HostRivalries(rivalries);

        pageVoice.Controls.Add(_tabVoiceSubNav);

        _navRefreshHooks["Voice"] = () => rivalries.RefreshDriverCombos();

        return announcer;
    }

    /// <summary>
    /// Sounds and its tabs, and the ambience's wiring to the race.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildSoundsPage(AnnouncerControl announcer)
    {
        // Sound Effects hosts the audio sub-nav: discrete effects, the
        // continuous ambience beds, and per-driver lap cues. Grouped because
        // all three are "what the app sounds like", and Lap Beeps never
        // justified a top-level nav slot of its own.
        _tabSoundSubNav = new TabControl { Dock = DockStyle.Fill };

        var subPageEffects = new TabPage("Effects");
        var soundEffects = new SoundEffectsControl(_soundStore, _undo) { Dock = DockStyle.Fill };
        subPageEffects.Controls.Add(soundEffects);
        _soundEffectsControl = soundEffects;

        var subPageAmbience = new TabPage("Ambience");
        var ambienceControl = new AmbienceControl(_ambienceStore, _ambience, _undo) { Dock = DockStyle.Fill };
        _ambienceControl = ambienceControl;
        subPageAmbience.Controls.Add(ambienceControl);

        var subPageMusic = new TabPage("Music");
        var musicControl = new MusicControl(_musicStore, _music, _undo) { Dock = DockStyle.Fill };
        _musicControl = musicControl;
        subPageMusic.Controls.Add(musicControl);

        // Broadcast Imaging (16 Oct 2026): each soundscape's jingles,
        // sweepers, bumpers, stingers and beds, and the run of show that
        // fires them. The imaging itself is static - see BroadcastImaging -
        // so it is told here which soundscape is in use and whether the
        // sound is muted, and its console lines go where everything else's do.
        BroadcastImaging.ActiveSoundscape = () => _themeStore.ActiveThemeId is { } activeId ? _themeStore.Get(activeId) : null;
        BroadcastImaging.IsMuted = () => _announcer.Muted;
        BroadcastImaging.Status += OnClientRawMessage;

        // "Imaging" on the tab, and Broadcast Imaging on the page itself:
        // "Broadcast Imaging" made the row too wide at 1280 wide and at 200%
        // text. Since 17 Oct 2026 it also holds the club's own show music,
        // as the Show treatment card moved from the Commentary page.
        var subPageImaging = new TabPage("Imaging");
        var imagingControl = new BroadcastImagingControl(_themeStore, _settings) { Dock = DockStyle.Fill };
        subPageImaging.Controls.Add(imagingControl);

        // RECORDING (17 Oct 2026): the "Recording the night" card from the
        // Commentary page, as a tab of its own - the race recordings, the
        // drivers' clips, and the pieces written from the night.
        //
        // WHY (26 Oct 2026, asked for by Asher: "the recording tab is in a
        // bad location move it to the club website for now."): still BUILT
        // here, because its show music and the night's pieces are wired to
        // Imaging's Show treatment card and the announcer just below, but no
        // longer one of Sounds' tabs - BuildClubWebsitePage, which runs after
        // this, hosts it on the Club Website page. "For now": moving it again
        // is only a matter of which TabControl it is added to.
        var subPageRecording = new TabPage("Recording");
        var recordingControl = new RecordingControl(_settings) { Dock = DockStyle.Fill };
        subPageRecording.Controls.Add(recordingControl);
        _recordingSubPage = subPageRecording;

        // Both grey with AI commentary, as they did on its page - and both
        // are told by the host rather than reaching for it.
        imagingControl.ShowTreatment.CommentaryOn = () => _settings.CommentaryEnabled;
        recordingControl.CommentaryOn = () => _settings.CommentaryEnabled;

        // The show's music: the card chooses the files, the announcer
        // screen plays them - see MainForm.Commentary.cs.
        WireShowMusic(announcer, imagingControl.ShowTreatment, recordingControl);

        // A driver's own night, and the proof of a line. Its own method
        // because it needs the stores, which WireShowMusic cannot see - see
        // MainForm.Commentary.cs.
        WireTheDriversNight(announcer, recordingControl);

        // And the night-before preview, which otherwise stays on the
        // club's page after the club has unticked it. See
        // TakeTheNightBeforeOffThePage.
        recordingControl.NightBeforeClosedOnTheRelay += TakeTheNightBeforeOffThePage;

        // First tab: the blend matters more often than any individual
        // setting, and it's the page someone opens mid-event.
        var subPageMixer = new TabPage("Mixer");
        var mixerControl = new MixerControl(
            _voiceStore, _soundStore, _ambienceStore, _ambience, _musicStore, _music, _announcer, _settings, _undo)
        { Dock = DockStyle.Fill };
        _mixerControl = mixerControl;

        // The Mixer's mute goes through the sidebar's, so it is saved and
        // the sidebar's unmute knows what it stopped - see SetAllSoundMuted.
        mixerControl.SetAllSoundMutedRequested = SetAllSoundMuted;
        subPageMixer.Controls.Add(mixerControl);

        var subPageLapBeeps = new TabPage("Lap Beeps");
        var lapBeeps = new LapBeepControl(_lapBeeps, _profiles, _soundStore, _undo) { Dock = DockStyle.Fill };
        subPageLapBeeps.Controls.Add(lapBeeps);

        // Soundscapes sits right after the Mixer: it sets up everything the
        // tabs after it hold - effects, voice, ambience, music - in one go.
        // It lived on Themes as "Announcer Style" until 0.56.
        var subPageSoundscapes = new TabPage("Soundscapes");
        var themes = new ThemesControl(_themeStore, _voiceStore, _soundStore, _undo, _ambienceStore, _musicStore, _settings) { Dock = DockStyle.Fill, Announcer = _announcer };
        _themesControl = themes;
        themes.ThemeApplied += AfterSoundscapeApplied;
        themes.SoundscapeApplied += PutOnSoundscapeLook;

        // Editing or deleting the applied soundscape changes what Theme music
        // plays (or leaves it none): the player's copy is refreshed here, and
        // the next queue it builds - a track ending, or Play - uses it.
        themes.SoundscapesChanged += () =>
        {
            _music.Theme = _themeStore.ActiveThemeMusic();
            _musicControl?.RefreshAll();
            imagingControl.RefreshAll();
        };
        subPageSoundscapes.Controls.Add(themes);

        // The Voice + Sounds regroup's order (17 Oct 2026): the blend, the
        // soundscape that sets the rest up, its imaging, then each source.
        // Recording, which came last, is on the Club Website page since
        // 26 Oct 2026 - see subPageRecording above.
        _tabSoundSubNav.TabPages.Add(subPageMixer);
        _tabSoundSubNav.TabPages.Add(subPageSoundscapes);
        _tabSoundSubNav.TabPages.Add(subPageImaging);
        _tabSoundSubNav.TabPages.Add(subPageEffects);
        _tabSoundSubNav.TabPages.Add(subPageAmbience);
        _tabSoundSubNav.TabPages.Add(subPageMusic);
        _tabSoundSubNav.TabPages.Add(subPageLapBeeps);

        // Seven tabs: each as wide as its own caption rather than all as
        // wide as "Soundscapes", or the row is wider than the window at
        // 1280 wide and at 200% text - see AppTheme.SizeTabsToCaptions.
        AppTheme.SizeTabsToCaptions(_tabSoundSubNav);

        // Simple mode (1 Nov 2026) keeps Mixer, Soundscapes and Music. The
        // ambience and effects still play as the soundscape sets them - only
        // their tuning tabs go. See SimpleMode.
        SimpleMode.HideTab(_tabSoundSubNav, subPageImaging);
        SimpleMode.HideTab(_tabSoundSubNav, subPageEffects);
        SimpleMode.HideTab(_tabSoundSubNav, subPageAmbience);
        SimpleMode.HideTab(_tabSoundSubNav, subPageLapBeeps);

        // Lap Beeps lists driver profiles, so it needs refreshing when the
        // tab is opened rather than only at construction.
        _tabSoundSubNav.SelectedIndexChanged += (_, _) =>
        {
            if (_tabSoundSubNav.SelectedTab == subPageLapBeeps)
            {
                lapBeeps.RefreshAll();
            }
            else if (_tabSoundSubNav.SelectedTab == subPageImaging)
            {
                // The soundscape in use may have been changed on its own tab.
                imagingControl.RefreshAll();
            }
            else if (_tabSoundSubNav.SelectedTab == subPageMixer)
            {
                // Levels may have been changed on the other tabs since this
                // was last shown - re-read rather than showing stale faders.
                mixerControl.RefreshAll();
            }
        };

        pageSound.Controls.Add(_tabSoundSubNav);
        _soundEffectsSubPage = subPageEffects;

        // Ambience is wired to race state so the crowd follows the action.
        WireAmbienceToRace();
    }

    /// <summary>
    /// Themes - the Looks page.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildThemesPage()
    {
        // Themes is the Looks page itself - the colours the app and the crowd
        // screens wear, and the club logo - with no tabs. It held two tabs
        // until 0.56, when Announcer Style (now Soundscapes) moved to Sounds.
        _looksControl = new LooksControl(_settings, _undo, ApplyHeaderAnimationSetting, RefreshHeaderLogo, RestartForNewLook) { Dock = DockStyle.Fill };

        // Its "Use for the App" restarts the app, which would end a race -
        // the same question Backup & Restore asks before its restart.
        _looksControl.IsRaceUnderWay = () => _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;
        pageThemes.Controls.Add(_looksControl);

        // Looks shows what settings and the looks file hold, and those can
        // change while the page is out of sight - an undo, most often - so it
        // re-reads them each time it comes into view.
        _navRefreshHooks["Themes"] = () => _looksControl.RefreshAll();
    }

    /// <summary>
    /// Sponsors.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildSponsorsPage()
    {
        var sponsors = new SponsorsControl(_sponsors, _undo) { Dock = DockStyle.Fill };
        pageSponsors.Controls.Add(sponsors);

        // WHAT EACH SPONSOR GOT TONIGHT (11 Oct 2026), for the report
        // button on that page. Readers rather than figures handed over, so
        // a club pressing it after the last race gets the last race's
        // reads in it - see SponsorsControl.ReadsTonight.
        //
        // The announcer's own rotation is the only count there is: it is
        // what decides whose turn each read was and what increments when
        // one goes out. An empty dictionary before the announcer has ever
        // run, which the report words as no reads rather than as a fault.
        sponsors.ReadsTonight = () =>
            _announcerControl?.Director?.Sponsors.ReadsTonight
            ?? new Dictionary<string, int>(StringComparer.Ordinal);

        sponsors.EventName = () => _raceEvents.Active?.Name;

        // And the "presented by" mentions their sponsored moments had - the
        // announcer counts them as each one goes to the speaker. See
        // SponsoredMomentGate.
        sponsors.MentionsTonight = () => _announcer.SponsorMentions.MentionsTonight();

        // And what phones saw of them on the club's Live page (5 Nov 2026),
        // as the relay last counted it - null, and no such lines in the
        // report, for a club not on the relay or before the first read.
        sponsors.WebViewsTonight = () => _relayPublisher.SponsorViews is { } views
            ? RelaySponsors.ForTheNight(views, TonightAtTheTrack.NightOf(DateTime.Now))
            : null;
    }

    /// <summary>
    /// Local Screens.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildLocalScreensPage()
    {
        var webServer = new WebServerControl(
            _webServer,
            _settings,
            () => _raceControlControl.TriggerOpenBigScreen(),
            _relayPublisher,
            GoToClubWebsiteSettings,
            _undo)
        {
            Dock = DockStyle.Fill,
        };

        pageWebServer.Controls.Add(webServer);

        // Not from the control's constructor, which runs inside this one:
        // the server would be answering phones before there was a window
        // to act on what they asked. Shown handlers run in the order they
        // were added, so the one that marks the host ready has run first.
        Shown += (_, _) => webServer.AutoStartIfConfigured();
    }

    /// <summary>
    /// The Club Website page on its Website tab - the relay's address, token
    /// and switches - for a button elsewhere that sends somebody there to set
    /// the relay up.
    /// </summary>
    // WHY (26 Oct 2026): the page has two tabs since Recording moved onto it,
    // and a page keeps whichever tab was last open - so "set up the Club
    // Website" could otherwise land on the race recordings.
    private void GoToClubWebsiteSettings()
    {
        GoToPage("Club Website");

        if (_tabClubWebsiteSubNav is { TabCount: > 0 } tabs && tabs.SelectedIndex != 0)
        {
            tabs.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Club Website.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildClubWebsitePage()
    {
        // Next to Local Screens in the sidebar: the two together answer "how
        // does somebody who is not stood at this laptop see the race" -
        // one on the venue's wifi, one from anywhere.
        ConfigurePage(pageOnline);

        var online = new RelaySettingsControl(_relayPublisher, _settings, _profiles, _undo) { Dock = DockStyle.Fill };

        // TWO TABS (26 Oct 2026, asked for by Asher: "the recording tab is in
        // a bad location move it to the club website for now."): the page as
        // it was, as Website, and Sounds' Recording tab after it - built by
        // BuildSoundsPage, which runs before this, with all its wiring. The
        // TabControl is the page's direct child, as on every page with tabs,
        // so a search for "recording" or "clips" brings that tab to the front
        // (SelectSearchedTab, NavSearchTabAliases).
        var subPageWebsite = new TabPage("Website");
        subPageWebsite.Controls.Add(online);

        _tabClubWebsiteSubNav = new TabControl { Dock = DockStyle.Fill };
        _tabClubWebsiteSubNav.TabPages.Add(subPageWebsite);
        _tabClubWebsiteSubNav.TabPages.Add(_recordingSubPage);
        AppTheme.StyleTabControl(_tabClubWebsiteSubNav);

        // Recording is Advanced (1 Nov 2026, Simple mode).
        SimpleMode.HideTab(_tabClubWebsiteSubNav, _recordingSubPage);

        // Back on Website, the boxes show what settings hold now - the same
        // re-read the page gets on entry (the refresh hook at the end).
        _tabClubWebsiteSubNav.SelectedIndexChanged += (_, _) =>
        {
            if (_tabClubWebsiteSubNav.SelectedTab == subPageWebsite)
            {
                online.RefreshAll();
            }
        };

        pageOnline.Controls.Add(_tabClubWebsiteSubNav);

        // ROOM & WEB (17 Oct 2026), from the Commentary page. Shout-outs from
        // the club's web page switched OFF has to be SENT to the relay rather
        // than inferred from the polling stopping, or the relay goes on
        // taking strangers' messages for a club that declined them - and
        // holding them. See RelayPublisher.CloseClubNotesOnTheRelay.
        online.RoomAndWeb.NotesClosedOnTheRelay += () => _relayPublisher.CloseClubNotesOnTheRelay();

        // And the same for the room's vote, for the same reason.
        online.RoomAndWeb.VoteClosedOnTheRelay += () => _relayPublisher.CloseRoomVoteOnTheRelay();

        // And listening along, which is the one of the three where leaving
        // it to be inferred would leave the club's own voice - possibly a
        // named member's cloned one - playing to anybody with the link.
        // See RelayPublisher.CloseListeningOnTheRelay.
        online.RoomAndWeb.ListeningClosedOnTheRelay += () => _relayPublisher.CloseListeningOnTheRelay();

        // The club pages' colours and logo live on Themes → Looks now,
        // shared with the app and the other screens; the Club Website page links
        // there rather than keeping a second copy of the same settings.
        online.OpenLooksRequested += ShowLooks;

        // Same reasoning: the Manager PIN lives on Local Screens, shared by
        // the Wi-Fi and relay manager pages alike, so the Club Website page's
        // "Set Manager PIN..." goes there rather than keeping a second box.
        online.OpenPhoneWebRequested += () => GoToPage("Local Screens");

        // The HeatCaller voice speaks through this same relay unless Voice
        // Settings points it elsewhere: a new address, club or token here is
        // a fresh question for it, not a reason to keep resting on the old
        // relay's answer.
        online.VoiceRelayChanged += () => _announcer.ResetHeatCallerVoiceState("the relay on the Club Website page was changed");

        // A new set made, or personal PINs switched on or off: straight to
        // the relay, and emailed when the club has asked for that, rather
        // than waiting up to ten minutes for the next pass.
        //
        // REPUBLISHED FIRST (17 Oct 2026, Bug Hunt 8, A17-1). "New PINs Now"
        // and its Undo leave nothing for the pass to rotate or ensure, so
        // the pass went straight to emailing while the relay still held the
        // old hashes - the republish, which starts the hash warm the email
        // waits for, only came after. Now the warm is running before the
        // pass looks, so the email defers to it.
        online.RacerPinsChanged += () =>
        {
            RepublishCheckInNow();
            RunRacerPinsPass();
        };

        // Reissue All Badges made a new badge key, so every driver's online
        // id is different now: the ids the desk scanner and relay check-ins
        // are matched against are worked out again at once, and the check-in
        // list - which carries them - goes to the relay now rather than on
        // the next two-second pass.
        online.BadgesReissued += () =>
        {
            RebuildBadgeIds();
            RepublishCheckInNow();
        };

        // What the page did, in the console, like every other page's.
        online.StatusMessage += OnClientRawMessage;

        // Email PINs... waits while the new PINs' hashes are still being made
        // in the background: until that batch ends and the check-in list
        // goes, the relay holds the old PINs and would refuse a racer who
        // tapped the one just emailed. Read from the UI thread, like the
        // flag's other readers.
        online.CheckInListBeingPrepared = () => _racerPinHashesWarming;

        // An Email PINs... send counts as one of this window's own until its
        // result is recorded, so a sign-up racer held back meanwhile is not
        // emailed a second time in the moment between the sending ending and
        // the page marking them emailed; and once it is recorded, they are
        // tried again. See EmailSignedUpPinsHeldForWarm.
        online.PinEmailStarted += () => Interlocked.Increment(ref _pinMailsUnrecorded);
        online.PinEmailFinished += () =>
        {
            Interlocked.Decrement(ref _pinMailsUnrecorded);
            EmailSignedUpPinsHeldForWarm();
        };

        // Refreshed on entry: the boxes have to show what settings hold,
        // not what they held when the page was built.
        _navRefreshHooks["Club Website"] = online.RefreshAll;
    }

    /// <summary>
    /// Venues.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildVenuesPage(RaceControlControl raceControl)
    {
        var venues = new VenuesControl(_venueStore, _trackLayouts, _themeStore, _undo) { Dock = DockStyle.Fill };

        // Applying a venue switches the decoder and the track layout, which
        // would cut timing to a running heat and re-file it - the page
        // refuses while this says a race or practice is on, the same states
        // the venue's undo below refuses in.
        venues.IsRaceUnderWay = () => _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;
        venues.VenueApplied += venue =>
        {
            // Everything below is taken first, so Undo can put the decoder,
            // track, soundscape and race defaults back as they were. The
            // combo boxes are this form's own and live as long as it does,
            // which is why the undo may touch them (see "Reset round").
            var decoderBefore = cmbConnectionType.SelectedIndex;
            var trackBefore = cmbTrackLayout.SelectedItem as string;
            var lapLimitBefore = _settings.RaceLapLimit;
            var timeLimitBefore = _settings.RaceTimeLimitMinutes;
            var minLapBefore = _settings.RaceMinLapTimeMs;
            var streakBefore = _settings.RaceStreakWindowPercent;
            var venueTheme = string.IsNullOrWhiteSpace(venue.PreferredThemeName) ? null : _themeStore.FindByName(venue.PreferredThemeName);
            var putBackSoundscape = venueTheme is null ? null : ThemeStore.CaptureForUndo(venueTheme, _voiceStore, _soundStore, _ambienceStore);

            // A soundscape ends lap times only, as it does on the Soundscapes
            // page - left here, after the capture above (see LapTimesOnly.Leave).
            var putBackLapTimesOnly = venueTheme is null ? null : LapTimesOnly.Leave(_settings, _voiceStore);

            var venueUndoDescription = $"Apply venue '{venue.Name}'";
            Action? undoVenue = null;
            undoVenue = () =>
            {
                // Not while cars are on track: switching the decoder back
                // disconnects the one timing the race, and the track change
                // would re-file the race under another layout. The step is
                // put back on the stack before refusing, so it can still be
                // undone once the race is over.
                if (_race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
                {
                    _undo.Capture(venueUndoDescription, undoVenue!);
                    throw new InvalidOperationException("a race or practice is running - finish it first, then undo again.");
                }

                if (putBackSoundscape is not null)
                {
                    putBackSoundscape();
                    putBackLapTimesOnly?.Invoke();
                    ShowSoundscapeApplied();
                }

                if (decoderBefore >= 0 && decoderBefore < cmbConnectionType.Items.Count && cmbConnectionType.SelectedIndex != decoderBefore)
                {
                    cmbConnectionType.SelectedIndex = decoderBefore;
                }

                if (trackBefore is not null && cmbTrackLayout.Items.Contains(trackBefore))
                {
                    cmbTrackLayout.SelectedItem = trackBefore;
                }

                // Back through the same door the venue used, so the race
                // format is worked out again. The window percent goes in as
                // the multiplier ApplyRaceDefaults converts it from.
                _raceControlControl.ApplyRaceDefaults(lapLimitBefore, timeLimitBefore, minLapBefore, 1.0 + streakBefore / 100.0);
            };
            _undo.Capture(venueUndoDescription, undoVenue);

            // The decoder, on the Connection page. The venue's host and port
            // were Next Level Timing's address and are no longer applied - a
            // venue saved for it gets the EasyLap decoder, and says so.
            if (!DecoderConnection.IsOffered(venue.ConnectionType))
            {
                var retired = venue.ConnectionType == DecoderConnection.RetiredNeonApi;
                var was = retired
                    ? "Next Level Timing, which is no longer offered"
                    : "a decoder this version does not know";

                OnClientRawMessage($"{AppTime.LogStampNow()}  [venue] \"{venue.Name}\" was set up for {was} - using the EasyLap decoder. Pick the right decoder for it on the Venues page.");

                // Saved as what it now does, so this is said once rather than
                // on every apply. Only the retired number: one from a newer
                // version is left as it is, in case that version comes back.
                if (retired)
                {
                    venue.ConnectionType = DecoderConnection.EasyLap;
                    _venueStore.Upsert(venue);
                }
            }

            cmbConnectionType.SelectedIndex = DecoderConnection.IndexOf(venue.ConnectionType);

            // Preferred track layout, if it still exists.
            if (!string.IsNullOrWhiteSpace(venue.PreferredTrackLayoutName) && cmbTrackLayout.Items.Contains(venue.PreferredTrackLayoutName))
            {
                cmbTrackLayout.SelectedItem = venue.PreferredTrackLayoutName;
            }

            // Preferred theme, if it still exists - applies voice+sound and rebuilds those pages to reflect it.
            if (!string.IsNullOrWhiteSpace(venue.PreferredThemeName))
            {
                var theme = venueTheme;
                if (theme is not null)
                {
                    ThemeStore.Apply(theme, _voiceStore, _soundStore, _ambienceStore, _musicStore);
                    _themeStore.MarkApplied(theme);
                    ShowSoundscapeApplied();
                }
            }

            // Race-control defaults.
            raceControl.ApplyRaceDefaults(venue.RaceLapLimit, venue.RaceTimeLimitMinutes, venue.RaceMinLapTimeMs, venue.RaceStreakSlowMultiplier);

            OnClientRawMessage($"{AppTime.LogStampNow()}  [venue] Applied venue \"{venue.Name}\".");
        };
        pageVenues.Controls.Add(venues);
        _navRefreshHooks["Venues"] = venues.RefreshDropdownSources;
    }

    /// <summary>
    /// Backup & Restore.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildBackupPage()
    {
        var backupRestore = new BackupRestoreControl(_settings, _undo) { Dock = DockStyle.Fill };

        // The Settings mode card: Advanced behind its warning, or back to
        // Simple - see MainForm.SettingsMode.
        _backupRestore = backupRestore;
        backupRestore.SettingsModeToggleRequested += ToggleSettingsMode;
        backupRestore.RestoreDefaultsRequested += RestoreDefaultSettings;
        backupRestore.PutBackSettingsRequested += PutBackEarlierSettings;

        // A restore restarts the app, which would end a race or practice
        // mid-session - the page asks first when this says one is on.
        backupRestore.IsRaceUnderWay = () => _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        // Save Support Info - done here rather than on the page, which has
        // no Driver Hub to take the names out with. See SupportInfo.
        backupRestore.SupportInfoRequested += () => SupportInfo.Save(backupRestore, _settings, _voiceStore, _profiles, OnClientRawMessage);
        backupRestore.RestoreCompleted += () =>
        {
            // Every store only reads from disk once, at startup - the only
            // way restored data actually takes effect is a fresh process.
            //
            // Frozen first: closing this process saves settings and stores
            // on its way out, and every one of those writes would put the
            // pre-restore data straight back over what was just restored.
            SafeFile.Freeze();
            Application.Restart();
            Environment.Exit(0);
        };

        // Windows will not replace a file that is open, so everything this
        // app holds open in the data folder is let go just before the
        // restore starts putting files back: the music track, the ambience
        // loops and the decoders' raw capture logs. Each on its own, so one that
        // fails does not leave the others open. Nothing is started again -
        // the app restarts after a restore.
        backupRestore.ReleaseFilesForRestore += () =>
        {
            foreach (var release in new Action[] { _music.Stop, _ambience.Stop, _ilapClient.Disconnect, _streamClient.Disconnect })
            {
                try
                {
                    release();
                }
                catch
                {
                    // A file still open is retried, then reported, by the restore.
                }
            }
        };
        pageBackup.Controls.Add(backupRestore);
    }

    /// <summary>
    /// The sidebar's list of pages, and each page parented, held and given its header.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void BuildNavItems(Panel pageGhosts)
    {
        _navItems = new List<NavItem>
        {
            new("Race Control", pageRaceControl),
            new("Race Day", pageRaceDay),
            new("Championship", pageChampionship),
            new("Driver Hub", pageDriverProfiles),
            new("Classes", pageClasses),
            new("Rentals", pageRentals),
            new("Handicaps", pageHandicaps),
            new("Track Layouts", pageTrackLayouts),
            new("Race History", pageRaceHistory),
            new("Race Analytics", pageAnalytics),
            new("Achievements", pageAchievements),
            new("Garage", pageSetupLog),
            new("Practice", pagePractice),
            new("Ghost Cars", pageGhosts),
            new("Demo Mode", pageDemoMode),
            new("Venues", pageVenues),
            new("Themes", pageThemes),
            new("Sponsors", pageSponsors),
            new("Local Screens", pageWebServer),
            new("Club Website", pageOnline),
            new("Voice", pageVoice),
            new("Sounds", pageSound),
            new("Connection", pageLiveSession),
            new("Backup & Restore", pageBackup),
        };

        // Every page except the ones the designer already parented -
        // pageLiveSession (Connection) is added there. Testing the parent
        // rather than skipping index 0 means the list can be in any order.
        foreach (var item in _navItems)
        {
            if (item.Page.Parent is null)
            {
                pnlContent.Controls.Add(item.Page);
            }
        }

        // Every page holds its layout from here (LayoutFreeze): the text
        // size and the theme are pushed through the whole window below, and
        // a page that is not held works itself out again for every control
        // whose face changes. The page shown first is let go when RebuildNav
        // shows it, and each of the others when WarmPages walks it once the
        // window is up, or when it is opened - whichever comes first.
        foreach (var item in _navItems)
        {
            LayoutFreeze.Freeze(item.Page);
        }

        // Every page gets the same top edge, from here - one place, and the
        // title is the nav's own name for the page, so the two can never
        // disagree. PageHeader reserves its strip with the page's Padding,
        // which the Dock=Fill child each page holds honours on its own; no
        // page's own layout code knows this exists.
        foreach (var item in _navItems)
        {
            PageHeader.Attach(item.Page, item.Name);
        }
    }

    /// <summary>
    /// The footer's Undo button, and the clear-out of what undo set aside.
    ///
    /// Split out of PopulateTabs on 22 Oct 2026 (ROADMAP, "Next, in order" 9),
    /// unchanged and called in the same place, so the pages are still built in
    /// the order they always were.
    /// </summary>
    private void WireUndoButton()
    {
        // Files an undoable action set aside (a deleted song, a replaced
        // logo) are kept for a week in case they're wanted back, then
        // cleared here. Off the UI thread since it walks folders on disk;
        // Prune never throws.
        _ = Task.Run(UndoHoldingFolder.Prune);

        _undo.UndoFailed += (description, ex) =>
            OnClientRawMessage($"{AppTime.LogStampNow()}  [undo] FAILED to undo '{description}': {ex.Message}");

        _undo.StateChanged += () =>
        {
            btnUndo.Enabled = _undo.CanUndo;

            // Full history on hover - the button itself only has room for
            // the next step.
            _footerTooltip.SetToolTip(btnUndo, _undo.CanUndo
                ? "Will undo, in order:\r\n" + string.Join("\r\n", _undo.PendingDescriptions.Select((d, i) => $"  {i + 1}. {d}"))
                    + (_undo.Count > 1 ? "\r\n\r\nRight-click to undo several at once." : string.Empty)
                : "Nothing to undo.");

            btnUndo.Invalidate();
        };
        btnUndo.Click += (_, _) =>
        {
            // A left-click on the little caret opens the list instead - see
            // UndoCaretBounds. Anything else (the rest of the button, or
            // Space/Enter with it focused) is the plain one-step undo.
            if (_undoCaretPressed)
            {
                _undoCaretPressed = false;
                ShowUndoMenu();
                return;
            }

            UndoSteps(1, string.Empty);
        };

        // Several steps at once: right-click the button (or click its
        // caret) for the list of what's pending, newest first. A disabled
        // button gets no mouse events at all, which is right - there is
        // nothing to list.
        btnUndo.MouseDown += (_, e) =>
            _undoCaretPressed = e.Button == MouseButtons.Left && _undo.Count > 1 && UndoCaretBounds(btnUndo).Contains(e.Location);
        // Pressed on the caret and then dragged off: no Click comes, and
        // the flag must not turn a later Space press into the list.
        btnUndo.MouseLeave += (_, _) => _undoCaretPressed = false;
        btnUndo.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowUndoMenu();
            }
        };

        // The caret itself, drawn on top of the icon by a second Paint
        // handler wired here once (SetUpFooterButton re-runs on every theme
        // change, which is why it isn't done there). Only while there is
        // more than one step - a list of one is just the button.
        btnUndo.Paint += (_, e) =>
        {
            if (_undo.Count > 1)
            {
                DrawUndoCaret(e.Graphics, UndoCaretBounds(btnUndo), AppTheme.SidebarText);
            }
        };

        AppTheme.StyleToolTip(_footerTooltip);
    }

    /// <summary>
    /// What a saved race does to the timetable (Race Control's SessionSaved,
    /// in PopulateTabs): every line whose heat is now recorded is ticked off.
    /// On its own so a test can run the same thing the handler does.
    /// </summary>
    internal static int TickOffRecordedHeats(DayScheduleStore schedule, RoundDefinitionStore rounds) =>
        schedule.AutoCompleteRecorded(id => rounds.Get(id)?.IsRecorded == true);
}
