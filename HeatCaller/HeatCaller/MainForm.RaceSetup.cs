using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's session setup for Race Control: the Session row, the Round
/// picker, starting a round, the race-day service's wiring, track layouts,
/// and undo for the setup.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// The Session row on Race Control, measured against the group's real
    /// width on every resize.
    ///
    /// It was laid out once, left to right, at fixed field widths: a name
    /// box, a track picker, a session type, a 280px round picker and then
    /// "Reset Round" at whatever x the arithmetic reached. Nothing checked
    /// that the total fitted - and at the designed width it already did
    /// not, so the button sat off the right-hand edge of the group and, on
    /// a window narrower than the sum of the parts, off the page.
    ///
    /// Now the button is pinned to the right edge and the round picker
    /// takes whatever is left between its caption and the button. When
    /// that is too little to read a round name in, the row splits in two
    /// rather than squeezing. A picker showing half a round name with an
    /// ellipsis is not a picker - it is a tooltip you have to hover to read.
    /// </summary>
    private void LayoutSessionRow()
    {
        if (grpSession.IsDisposed || grpSession.Width < 80)
        {
            return;
        }

        const int inset = 12;

        // The narrowest the round picker is allowed to get before the row
        // gives up and wraps. Measured against the longest thing it ever
        // has to show rather than guessed.
        var wanted = TextRenderer.MeasureText(
            "(freeform - no race program round)",
            AppTheme.BodyFont,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 40;

        var right = grpSession.Width - inset;
        var top = UiLayout.InsideGroup(grpSession);
        var rowHeight = Math.Max(cmbActiveRound.Height, UiLayout.ButtonHeight);

        // --- Name, only while there is no round to derive one from ---
        //
        // With a round chosen the box is filled from that round and made
        // read-only, so it spends a caption and 170 pixels restating what
        // the Round picker already says. Hidden then, and the row closes up
        // behind it.
        var x = inset;

        // _sessionNameShown, not txtSessionLabel.Visible - see that field.
        if (_sessionNameShown)
        {
            lblSessionLabel.Location = new Point(x, UiLayout.CentreOn(lblSessionLabel, top, rowHeight));
            txtSessionLabel.Location = new Point(UiLayout.After(lblSessionLabel), UiLayout.CentreOn(txtSessionLabel, top, rowHeight));
            x = UiLayout.After(txtSessionLabel) + UiLayout.SectionGap;
        }

        lblTrack.Location = new Point(x, UiLayout.CentreOn(lblTrack, top, rowHeight));
        cmbTrackLayout.Location = new Point(UiLayout.After(lblTrack), UiLayout.CentreOn(cmbTrackLayout, top, rowHeight));

        // --- The round picker, on the same row when it fits ---
        //
        // It takes everything left over to the right-hand edge. There used
        // to be a "Reset Round" button pinned out there taking a bite out of
        // it; that is Race Control's own Reset now, which was doing the same
        // job two feet away.
        var oneRowLeft = UiLayout.After(cmbTrackLayout) + UiLayout.SectionGap;

        // Measured, not PreferredWidth: an autosize width leaves out the
        // glyph padding the caption is actually drawn with, which is the ten
        // pixels the layout pass spent every startup pushing this back out.
        var captionWidth = TextRenderer.MeasureText(lblActiveRound.Text, AppTheme.BodyFont).Width;
        var oneRowCaption = oneRowLeft + captionWidth + UiLayout.LabelGap;
        var oneRowSpace = right - oneRowCaption;

        var roundY = oneRowSpace >= wanted
            ? top
            : top + rowHeight + UiLayout.RowGap;

        var roundCaptionLeft = oneRowSpace >= wanted ? oneRowLeft : inset;

        lblActiveRound.Location = new Point(roundCaptionLeft, UiLayout.CentreOn(lblActiveRound, roundY, rowHeight));

        var comboLeft = UiLayout.After(lblActiveRound);

        cmbActiveRound.Width = Math.Max(140, right - comboLeft);
        cmbActiveRound.Location = new Point(comboLeft, UiLayout.CentreOn(cmbActiveRound, roundY, rowHeight));

        // The group grows for a second row rather than clipping it.
        var height = UiLayout.Below(cmbActiveRound, inset);

        if (grpSession.Height != height)
        {
            grpSession.Height = height;
        }
    }

    /// <summary>Repopulates the "Race Round" dropdown with not-yet-recorded rounds from the race program, preserving the current selection or falling back to the last one used.</summary>
    private void RefreshActiveRoundCombo()
    {
        var previousId = (cmbActiveRound.SelectedItem as RoundComboItem)?.Round.Id
            ?? _activeRound?.Id
            ?? _settings.LastActiveRoundId;

        // Declared outside the try so it's still in scope for the
        // no-longer-available check further down.
        var idx = -1;

        _rebuildingRoundCombo = true;
        try
        {
            cmbActiveRound.Items.Clear();
            cmbActiveRound.Items.Add(RoundComboItem.None);
            // Tonight's rounds only, the same scope auto-advance uses. The
            // box listed every unrun round the club had ever made, so last
            // month's heats sat above tonight's with nothing to tell them
            // apart, and choosing one filed the race against that meeting.
            foreach (var round in _rounds.Unrecorded(_raceEvents.ActiveRoundScope()))
            {
                cmbActiveRound.Items.Add(new RoundComboItem(round));
            }

            if (previousId is not null)
            {
                for (var i = 0; i < cmbActiveRound.Items.Count; i++)
                {
                    if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == previousId)
                    {
                        idx = i;
                        break;
                    }
                }
            }

            cmbActiveRound.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally
        {
            _rebuildingRoundCombo = false;
        }

        // The handler was suppressed during the rebuild, so bring the rest
        // of the UI back in step with whatever ended up selected - without
        // the handler's side effect of erasing the saved round id.
        var restored = cmbActiveRound.SelectedItem as RoundComboItem;
        if (restored is not null && restored.Round.Id != RoundComboItem.None.Round.Id)
        {
            _activeRound = restored.Round;
            ApplyRoundToSessionSetup(restored.Round);
        }
        else
        {
            _activeRound = null;
            cmbTrackLayout.Enabled = true;
            txtSessionLabel.ReadOnly = false;
            SetSessionNameVisible(true);
        }

        // The combo's own change handler re-reads Race Control's format,
        // but it fired while _activeRound still held the old round, so it
        // read the wrong round's format - at startup the remembered round's
        // format was never applied at all. Read again now _activeRound is
        // right. Null during construction, before Race Control exists.
        _raceControlControl?.RefreshFormat();

        // If the previously-selected round is no longer selectable (it was
        // recorded, or deleted), say so rather than silently reverting to
        // freeform - that silence is exactly what made this hard to spot.
        if (previousId is not null && idx < 0)
        {
            OnClientRawMessage(
                $"{AppTime.LogStampNow()}  [program] The selected race round is no longer available "
                + "(already recorded or deleted) - session setup is back to freeform.");
        }
    }

    private void CmbActiveRound_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Ignore the events raised while the list is being rebuilt; only a
        // real user choice should change the active round.
        if (_rebuildingRoundCombo)
        {
            return;
        }

        var item = cmbActiveRound.SelectedItem as RoundComboItem;
        if (item is null || item.Round.Id == RoundComboItem.None.Round.Id)
        {
            _activeRound = null;
            cmbTrackLayout.Enabled = true;
            txtSessionLabel.ReadOnly = false;
            SetSessionNameVisible(true);

            // Back to the day's own layout, since nothing more specific is
            // choosing one any more.
            SyncTrackLayoutToRaceDay();

            _settings.LastActiveRoundId = null;
            _settings.Save();
            return;
        }

        ApplyRoundToSessionSetup(item.Round);
        _settings.LastActiveRoundId = item.Round.Id;
        _settings.Save();
    }

    /// <summary>
    /// Puts a programme round on track and starts it - the one way a round
    /// is started, whether from the Program tab, the Next Up bar or a
    /// phone (RaceDayService).
    ///
    /// It REFUSES rather than asks: practice running, or a race running
    /// that nobody has agreed to abandon, is answered with the reason and
    /// nothing is touched. The desk asks its own questions first (practice,
    /// abandon the running race?) and passes the answer in as
    /// <paramref name="confirmedOverRunningRace"/>; a phone asks on the
    /// phone. Returns null when the round was started.
    /// </summary>
    /// <param name="showRaceControl">Switch the laptop to Race Control - true from the Program tab, false where the person is already there or not at the laptop.</param>
    /// <param name="logLine">What to write in the console, after the time stamp.</param>
    private string? StartRoundNow(RoundDefinition round, bool confirmedOverRunningRace, bool showRaceControl, string logLine)
    {
        // Practice is on the same session, and starting over it would wipe
        // the practice laps. The Practice page has its own save and
        // teardown, so it is stopped there, never taken over here.
        if (_race.IsPracticeMode)
        {
            return "Practice is running, so a race can't be started yet - stop practice first on the Practice page.";
        }

        if (_race.State == RaceState.Racing && !confirmedOverRunningRace)
        {
            return _race.IsDemoMode
                ? $"A demo is running. Starting \"{round.RoundLabel}\" ends it - confirm to go ahead."
                : $"A race is running. Starting \"{round.RoundLabel}\" abandons it and clears its laps - confirm to go ahead.";
        }

        // A demo is ended BEFORE the grid call is spoken: ending it
        // clears its backlog of queued lap calls, which would take the
        // grid call with it if Stage ended the demo afterwards.
        _raceControlControl.StopDemoForTakeover($"start \"{round.RoundLabel}\"");

        // The setup as it is now, before this round replaces it - so
        // undoing a start over a running race puts that race's round,
        // track and label back with it.
        var restoreSessionSetup = CaptureSessionSetupForUndo();

        // Same path as picking the round by hand, then hitting
        // auto-start - so there's one code path for starting a race.
        ApplyRoundToSessionSetup(round);
        _activeRound = round;
        RefreshActiveRoundCombo();

        if (showRaceControl)
        {
            GoToPage("Race Control");
        }

        _announcer.NotifyGridCall(round, _rounds, _trackLayouts, _raceEvents);
        _raceControlControl.TriggerAutoStart(confirmedOverRunningRace: true, restoreHostOnUndo: restoreSessionSetup);

        OnClientRawMessage($"{AppTime.LogStampNow()}  {logLine}");
        return null;
    }

    /// <summary>
    /// Checks a driver in or out from a phone - the LAN check-in page and
    /// the race-day manager both come here. True when it changed anything.
    /// UI thread only.
    /// </summary>
    private bool ApplyPhoneCheckIn(string transponder, bool wanted)
    {
        // Already so: no write, no republish. The server checks the
        // same thing against its copy of the roster, which can be a
        // moment behind; the store is the one that is right.
        if (_checkInStore.IsCheckedIn(transponder) == wanted)
        {
            return false;
        }

        _checkInStore.SetCheckedIn(transponder, wanted);
        _checkInChangedUtc[transponder] = DateTime.UtcNow;

        // Republished promptly rather than waiting for the next
        // tick, so the phone that made the change sees it on its
        // very next poll - but coalesced, so ten phones tapping at
        // once is one rebuild, not ten.
        PublishCheckInRosterSoon();

        // Only the ticks, not the whole page: somebody may be
        // working down the list at the desk.
        _checkInControl?.RefreshCheckInMarks();
        return true;
    }

    /// <summary>
    /// Builds the race-day manager and hands it the stores, Race Control
    /// and the Program tab - the same objects the desk uses - plus the
    /// three MainForm methods that stand in for a dialog: starting a round,
    /// checking a driver in, and sending a result again. Called once
    /// PopulateTabs has built Race Control and the Program tab. See
    /// RaceDayService and raceday-design.md.
    /// </summary>
    private void WireRaceDayService()
    {
        _raceDayService = new RaceDayService(new RaceDayServiceHost
        {
            UiThread = this,
            Settings = _settings,
            Undo = _undo,
            Race = _race,
            Events = _raceEvents,
            Classes = _raceClasses,
            Profiles = _profiles,
            Rounds = _rounds,
            History = _sessionHistory,
            CheckIns = _checkInStore,
            Tech = _techInspections,
            Schedule = _daySchedule,
            Program = _raceProgram,
            RaceControl = _raceControlControl,
            ActiveRound = () => _activeRound,

            // A phone never switches what is on the laptop's own screen -
            // showRaceControl is a Program-tab/Next-Up-bar convenience for
            // somebody sitting at the laptop, not something a request from
            // someone else's pocket should do.
            StartRound = (round, confirmedOverRunningRace, logLine) =>
                StartRoundNow(round, confirmedOverRunningRace, showRaceControl: false, logLine),

            SetCheckedIn = ApplyPhoneCheckIn,
            RepublishSession = PublishSessionToRelay,

            RefreshDesk = () =>
            {
                RefreshCurrentPage();
                RefreshActiveRoundCombo();
                _raceControlControl?.RefreshFormat();
                _checkInControl?.RefreshCheckInMarks();
            },

            Log = OnClientRawMessage,
            IsClosing = () => _closingDown,
        });

        // Lets the Wi-Fi /manage page reach the same service the desk does -
        // see WebServerService.Manager.
        _webServer.Manager = _raceDayService;
    }

    /// <summary>
    /// Takes a copy of the session setup - the active round, session type,
    /// track and label - and hands back an action that puts it back.
    ///
    /// Captured by a program or Next Up start BEFORE it switches the setup
    /// to the new round, and passed to Race Control as the host's part of
    /// the undo for the race that start abandons. Without it, undoing the
    /// start put the old race back but left the new round selected, so the
    /// old race's result was filed under the wrong heat.
    /// </summary>
    private Action CaptureSessionSetupForUndo()
    {
        var round = _activeRound;
        var trackLayout = cmbTrackLayout.SelectedItem as string;
        var label = txtSessionLabel.Text;

        return () =>
        {
            if (round is not null)
            {
                ApplyRoundToSessionSetup(round);
            }
            else
            {
                // Freeform, the same way picking "(freeform)" in the Round
                // box leaves it. Set here as well as by the box's own
                // handler, which does not run when the box was already on
                // freeform.
                cmbActiveRound.SelectedIndex = 0;
                _activeRound = null;
                cmbTrackLayout.Enabled = true;
                txtSessionLabel.ReadOnly = false;
                SetSessionNameVisible(true);
            }

            // The values themselves, as they were. The round above sets its
            // own, but a round with no track of its own leaves the track on
            // whatever the new round chose - and freeform sets none at all.
            if (trackLayout is not null)
            {
                var trackIdx = cmbTrackLayout.Items.IndexOf(trackLayout);
                if (trackIdx >= 0)
                {
                    cmbTrackLayout.SelectedIndex = trackIdx;
                }
            }

            txtSessionLabel.Text = label;
        };
    }

    /// <summary>
    /// Whether the session Name box is meant to be on screen.
    /// </summary>
    /// <remarks>
    /// A field rather than reading Control.Visible, which answers a
    /// different question: it is false for any child of a window that has
    /// not been shown yet, however the property was set. So at startup -
    /// before the form is up, which is exactly when a remembered round is
    /// restored - asking the label whether it is visible answers "no" for
    /// every control on the page, the guard below would skip the hide, and
    /// the box would appear beside a round that had already locked it.
    /// </remarks>
    private bool _sessionNameShown = true;

    /// <summary>
    /// Shows or hides the session Name box, and re-flows the row behind it.
    /// </summary>
    private void SetSessionNameVisible(bool visible)
    {
        if (_sessionNameShown == visible)
        {
            return;
        }

        _sessionNameShown = visible;

        lblSessionLabel.Visible = visible;
        txtSessionLabel.Visible = visible;

        LayoutSessionRow();
    }

    /// <summary>
    /// Points the Track box at the open race day's layout.
    /// </summary>
    /// <remarks>
    /// THIS BOX USED TO BE A SECOND, UNSYNCHRONISED COPY of something the
    /// race day already knows.
    ///
    /// Nothing ever pushed RaceEvent.TrackLayoutName into it. It was seeded
    /// from the last layout used and only picked up the day's by accident,
    /// when a programme round that had inherited the day's layout happened to
    /// be selected. So a FREEFORM session on a day set to one track recorded
    /// itself against whichever track was used last - and that name is what
    /// decides which track's all-time record a lap challenges, and what every
    /// personal best, driver stat, trend and relay record is filed under. It
    /// was wrong quietly, which is the worst way for it to be wrong.
    ///
    /// Called when a day is opened or switched, when the round goes back to
    /// freeform, and once at startup. NOT on every visit to the page: picking
    /// a track by hand for one session is allowed, and it stays picked until
    /// the day or the round changes under it.
    /// </remarks>
    private void SyncTrackLayoutToRaceDay()
    {
        // A round's own layout is more specific than the day's default, and
        // the box is locked to it while one is chosen.
        if (_activeRound is not null)
        {
            return;
        }

        var wanted = _raceEvents.Current.TrackLayoutName;

        // A day that names no layout has no opinion, so whatever is in the
        // box stays - which is also what happens with no day open at all.
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return;
        }

        var idx = cmbTrackLayout.Items.IndexOf(wanted);

        if (idx >= 0 && cmbTrackLayout.SelectedIndex != idx)
        {
            cmbTrackLayout.SelectedIndex = idx;
        }
    }

    /// <summary>Pre-fills and locks Track/Label from a race-program round.</summary>
    private void ApplyRoundToSessionSetup(RoundDefinition round)
    {
        _activeRound = round;

        if (!string.IsNullOrWhiteSpace(round.TrackLayoutName))
        {
            var trackIdx = cmbTrackLayout.Items.IndexOf(round.TrackLayoutName);
            if (trackIdx >= 0)
            {
                cmbTrackLayout.SelectedIndex = trackIdx;
            }
        }
        cmbTrackLayout.Enabled = false;

        // Still SET, so the saved session keeps its "Class - Round" name,
        // but hidden: with a round chosen, this box shows a name derived
        // from the round and refuses to be edited - a field spending a row
        // to repeat what the Round picker beside it already says.
        txtSessionLabel.Text = $"{round.ClassName} - {round.RoundLabel}";
        txtSessionLabel.ReadOnly = true;
        SetSessionNameVisible(false);

        // Reflect the selection in the dropdown itself, in case this was
        // triggered from the Race Program dialog rather than the combo.
        for (var i = 0; i < cmbActiveRound.Items.Count; i++)
        {
            if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == round.Id)
            {
                cmbActiveRound.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Wraps a RoundDefinition with a friendly display string for the combo box.</summary>
    private sealed record RoundComboItem(RoundDefinition Round)
    {
        public static readonly RoundComboItem None = new(new RoundDefinition { Id = string.Empty, RoundLabel = "(freeform - no race program round)" });

        public override string ToString() =>
            Round.Id == string.Empty ? Round.RoundLabel : $"[{Round.ClassName}] {Round.RoundLabel} ({Round.Kind})";
    }

    /// <summary>
    /// Adds a Garage car for each car name a driver has just gained.
    /// </summary>
    /// <remarks>
    /// Marshalled to the UI thread: the phone's race-day page and relay
    /// signups add drivers from wherever the request arrived, and the car
    /// store is read by the Garage page on this one.
    /// </remarks>
    private void OnProfileCarsAdded(DriverProfile profile, IReadOnlyList<string> names)
    {
        if (IsDisposed || _closingDown)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnProfileCarsAdded(profile, names));
            return;
        }

        var added = GarageSync.AddCars(profile, names, _cars);

        if (added == 0)
        {
            return;
        }

        var who = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

        OnClientRawMessage(
            $"{AppTime.LogStampNow()}  [garage] Added {Plural.Of(added, "car")} for {who}: {string.Join(", ", names)}.");

        // Showing on screen right now - the phone and the new-transponder
        // prompt can add a car while somebody is looking at the wall.
        if (_shownPage == "Garage")
        {
            RefreshCurrentPage();
        }
    }

    /// <summary>
    /// Keeps the Race Control track picker in step with the layouts on
    /// disk, whichever page changed them.
    /// </summary>
    /// <remarks>
    /// The picker used to be refreshed only at startup, after the first-run
    /// wizard, and when the CONNECTION page was opened - which was right
    /// when the session row lived on Connection, and has been wrong ever
    /// since it moved to Race Control. A track made on Track Layouts
    /// therefore never reached the picker you would go and look for it in.
    ///
    /// The store says when it saved; this decides whether that save changed
    /// anything the picker shows. Most saves do not - a new track record
    /// set mid-race is a save too - and rebuilding the list clears and
    /// re-selects the box, which churns the hidden copy Race Control records
    /// races against and writes settings twice. So the NAMES are compared
    /// first, and nothing happens unless one was added, removed or renamed.
    /// </remarks>
    private void OnTrackLayoutsChanged()
    {
        if (IsDisposed || _closingDown)
        {
            return;
        }

        // The record check that saves mid-race runs off the lap pipeline,
        // which is not promised to be on this thread.
        if (InvokeRequired)
        {
            BeginInvoke(() => OnTrackLayoutsChanged());
            return;
        }

        var shown = cmbTrackLayout.Items.Cast<object>().Skip(1).Select(o => o as string);
        var stored = _trackLayouts.All.Select(t => t.Name);

        if (shown.SequenceEqual(stored, StringComparer.Ordinal))
        {
            return;
        }

        RefreshTrackLayoutCombo();

        // The day may name a track that did not exist until a moment ago -
        // the setup wizard creates both together. Fills that gap only: a
        // track somebody picked by hand is left where they put it.
        if (cmbTrackLayout.SelectedIndex <= 0)
        {
            SyncTrackLayoutToRaceDay();
        }
    }

    /// <summary>Repopulates the track layout dropdown, preserving the current selection if it still exists, or falling back to the last one used (persisted across restarts) if there's no in-memory selection yet.</summary>
    private void RefreshTrackLayoutCombo()
    {
        var previous = cmbTrackLayout.SelectedItem as string ?? (_settings.LastTrackLayoutName.Length > 0 ? _settings.LastTrackLayoutName : null);
        cmbTrackLayout.Items.Clear();
        cmbTrackLayout.Items.Add("(none)");
        foreach (var t in _trackLayouts.All)
        {
            cmbTrackLayout.Items.Add(t.Name);
        }

        var idx = previous is not null ? cmbTrackLayout.Items.IndexOf(previous) : -1;
        cmbTrackLayout.SelectedIndex = idx >= 0 ? idx : 0;

        // Race Control keeps its own list of layouts to record races
        // against; a layout added, renamed or removed has to reach it too,
        // or a race is filed against a name that no longer exists. Null
        // on the first call, which comes before Race Control is built.
        _raceControlControl?.RefreshTrackLayouts();
    }
}
