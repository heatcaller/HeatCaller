using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// Measures a page and says what does not fit: controls lying on top of
/// each other, text cut short, things running past the edge of whatever
/// holds them, parts squashed to nothing. It changes nothing.
///
/// It began as the measuring half of the layout repair pass ("the doctor")
/// without the repairs. The doctor nudged a fixed-position page at runtime
/// until it stopped overlapping, on every launch, and a page it had tidied
/// could look fine while its own layout was still wrong underneath. This
/// only reports. It is what the layout test (HeatCaller.Tests\LayoutTests)
/// runs over every page and every tab, at several window sizes and text
/// sizes, and what every page - each rebuilt on the layout pieces
/// (LayoutPieces.cs) - has to come through with nothing to say. Once every
/// page did, the doctor went (30 Sep) - see the roadmap, "The fixed-pixel
/// layout".
///
/// The rules started as the doctor's: the same padding round a button's
/// caption, the same "wider than a line" test for a block of text, the same
/// indifference to one control sitting wholly inside another. Where the
/// doctor guessed and this can measure - a label's text wrapping inside its
/// own box - it measures.
/// </summary>
internal static class LayoutAudit
{
    /// <summary>Between a page's name and a tab's in a finding - the same mark the old repair pass's log lines used.</summary>
    internal const string Separator = " › ";

    /// <summary>
    /// The pages rebuilt so they lay themselves out.
    ///
    /// Every page in the sidebar is on it (30 Sep). The layout test fails if
    /// any page has anything to report at any size it tries - whether or not
    /// it is on this list, so a page added later without the pieces fails
    /// too. The list marks the rebuilt pages in the test's report; a new page
    /// goes on it in the same change that adds it.
    /// </summary>
    internal static readonly IReadOnlyList<string> ConvertedPages = new[]
    {
        "Race Control",

        // All three tabs: Next Steps (TodayControl over RaceProgramControl),
        // Setup (RaceDayEventControl) and Who's Racing (CheckInControl).
        "Race Day",

        // The timing decoder and the developer console (MainForm.ConnectionPage).
        "Connection",

        // The roster beside the driver's card (ProfileManagerControl).
        "Driver Hub",

        // The grid of classes (RaceClassesControl).
        "Classes",

        // Both tabs: Standings (SeasonsControl) and Calendar (EventsControl).
        "Championship",

        // The loan units and the count still out (RentalTransponderControl).
        "Rentals",

        // The drivers of a class and their handicaps (HandicapControl).
        "Handicaps",

        // The layouts beside the one selected (TrackLayoutManagerControl, in TrackLayoutForms).
        "Track Layouts",

        // The sessions beside the one selected (RaceHistoryControl).
        "Race History",

        // All six tabs: Overview, Personal Bests, Lap Times, Head to Head,
        // Trends and Pace (AnalyticsOverviewControl, PersonalBestsControl,
        // LapChartControl, HeadToHeadControl, TrendChartControl,
        // PaceAnalysisControl).
        "Race Analytics",

        // The picker, the progress strip and the wall of badges (AchievementsControl).
        "Achievements",

        // The filter, the wall of cars and its buttons (GarageControl).
        "Garage",

        // The buttons, the setup and ghost cards beside the clock, and this
        // session's table (PracticeModeControl).
        "Practice",

        // The switches and the list of ghosts (GhostControl).
        "Ghost Cars",

        // The settings and the log beside the racers (DemoModeControl).
        "Demo Mode",

        // The venues beside the selected one's settings (VenuesControl).
        "Venues",

        // The looks, the club logo and the appearance options (LooksControl).
        "Themes",

        // The slides beside their preview, and the rotation's timing (SponsorsControl).
        "Sponsors",

        // The server, the race-day manager and the addresses to open (WebServerControl).
        "Local Screens",

        // Both tabs: Website - the relay and the cards for everything
        // published through it (RelaySettingsControl), with the Room & Web
        // card (RoomAndWebCard) - and Recording (RecordingControl), moved
        // here from Sounds on 26 Oct 2026.
        "Club Website",

        // All four tabs: Voice and Race Calls (VoiceSettingsControl, both
        // files, with the rivalries at the foot of Race Calls -
        // RivalriesControl), Cast (CastControl) and AI Commentary
        // (CommentaryControl).
        "Voice",

        // All seven tabs: Mixer (MixerControl), Soundscapes (ThemesControl),
        // Imaging (BroadcastImagingControl, with ShowTreatmentCard), Effects
        // (SoundEffectsControl), Ambience (AmbienceControl), Music
        // (MusicControl) and Lap Beeps (LapBeepControl). Recording went to
        // Club Website on 26 Oct 2026.
        "Sounds",

        // Making, restoring and keeping backups, the password and getting help (BackupRestoreControl).
        "Backup & Restore",
    };

    /// <summary>Whether a page, or a tab inside one ("Race Day › Setup"), belongs to a converted page.</summary>
    internal static bool IsConverted(string where) =>
        ConvertedPages.Any(page =>
            string.Equals(where, page, StringComparison.Ordinal)
            || where.StartsWith(page + Separator, StringComparison.Ordinal));

    /// <summary>
    /// The windows - dialogs - rebuilt on the layout pieces and sized to
    /// what is in them (DialogFit), by the name the layout test gives each
    /// (LayoutTests.EveryConvertedWindowFitsAtEverySize). The test fails if
    /// any of these has anything to report at any size, or is bigger than
    /// the screen. A window goes on this list in the same change that
    /// converts it.
    /// </summary>
    internal static readonly IReadOnlyList<string> ConvertedWindows = new[]
    {
        // Who marshals which corner for each round (MarshalAssignmentsForm).
        "Corner Marshals",

        // A main round added by hand, and who is in it (MainRoundForm).
        "Add Main",

        // A class's qualifying order so far (QualifyingStandingsForm).
        "Qualifying Standings",

        // Whether every car in the race is being heard (ReadinessCheckForm).
        "Pre-race Check",

        // The app's own message box (AppDialog), for every message it shows.
        "Message",

        // How to start tonight, and what kind of night it is and which classes are racing (NewRaceDayForm - the Start Tonight window).
        "Start Tonight",

        // Who is in a round and in what order (RoundRosterForm).
        "Drivers in Round",

        // The saved drivers to add to a round from outside its class (RoundRosterForm.DriverPicker).
        "Add a Driver to This Round",

        // A race format - the app default, a class's or a round's - or the default it follows (RaceFormatForm).
        "Race Format",

        // A saved race's result corrected, and the new order it makes (ResultEditForm).
        "Edit Result",

        // A class's heats side by side, drivers dragged between them (HeatBalanceForm).
        "Move drivers between heats",

        // Online sign-up: the switch, the sign-ups waiting and the invites (SignupsForm).
        "Online Sign-ups",

        // One sign-up checked before it becomes a driver (ApproveSignupForm, titled "Approve <name>").
        "Approve",

        // Who to email a sign-up code to (InviteDriversForm).
        "Invite Drivers",

        // Why a sign-up is turned down (SignupReasonForm, titled "Turn Down <name>").
        "Turn Down",

        // Who a code made at the desk is for (SignupReasonForm).
        "Code for Someone Here",

        // Set Up Tonight at each of its steps: the rail beside the step and its footer (RaceDaySetupWizard).
        "Set Up Tonight",

        // A new class's name, from Set Up Tonight's Add Class... (QuickClassForm).
        "Add a class",

        // A new driver's name, transponder and class, from Set Up Tonight's and Who's Racing's Add Driver... (QuickDriverForm).
        "Add a driver",

        // The windows a new club meets first (30 Sep).

        // The first-run setup at each of its steps: the rail beside the step and its footer (FirstRunWizard).
        "Welcome to HeatCaller",

        // A driver's profile, added or edited: the fields beside the photo strip (ProfileForm, titled "Add Driver Profile" / "Edit Driver Profile").
        "Driver Profile",

        // A class's settings, added or edited (RaceClassEditForm - titled "Add Class" for a new one).
        "Edit Class",

        // What a scrutineer checks for a class (RaceClassEditForm.TechChecklistForm, titled "Tech Checklist - <class>").
        "Tech Checklist",

        // One check typed in, from the Tech Checklist's Add... (TextPromptForm).
        "Add check",

        // The same, from its Rename... (TextPromptForm).
        "Rename check",

        // The Classes page as a window, from a driver profile's Manage... (RaceClassManagerForm).
        "Race Classes",

        // What each warning in a race costs a class's drivers (WarningLadderForm, titled "Warnings - <class>").
        "Warnings",

        // A sanctioning body's rules for a class, and what they leave out (SanctioningPresetForm, titled "Sanctioning preset - <class>").
        "Sanctioning preset",

        // A club transponder lent at the counter, to a saved driver or a walk-in (RentalCheckOutForm, titled "Check Out - <unit>").
        "Check Out",

        // A new rental unit's transponder, asset tag and notes (RentalUnitEditForm).
        "Add Rental Unit",

        // A rental unit's details changed, or retired (RentalUnitEditForm, titled "Edit <unit>").
        "Edit Rental Unit",

        // A rental unit coming back, and any notes on its condition (RentalPromptForm, from Rentals' Check In...).
        "Check in",

        // A car's inspection against its class's checklist, new or edited (TechInspectionEditForm, titled "New Tech Inspection" / "Edit Inspection - <driver>").
        "Tech Inspection",

        // One inspection for several cars picked on Who's Racing (BulkInspectionForm, titled "Log Inspection for <n> Drivers").
        "Log Inspection for Several Drivers",

        // The rest of race night, and its paperwork (30 Sep).

        // The live qualifying order while a heat runs, or the note that none is (QualifyingProjectionForm, from Race Control's Qualifying Projection).
        "Qualifying projection",

        // The first race's time and the gap between races, for the timetable (FillTimesForm, from the Timetable's Fill In the Times...).
        "Fill in the times",

        // What a race day's delete takes with it - saved races, records, championship picks, badges (DeleteRaceDayForm).
        "Delete race day",

        // A new line of the run sheet: its time, class and description (ScheduleEntryForm).
        "Add Schedule Entry",

        // A run sheet line changed (ScheduleEntryForm).
        "Edit Schedule Entry",

        // A race day renamed, and its notes (EventEditForm, titled "Edit <race day>").
        "Edit Race Day",

        // Points each driver brings into a championship from before it ran here (CarriedPointsForm, titled "Points carried in - <championship>").
        "Points carried in",

        // Every racer's personal check-in PIN, masked until Show PINs is ticked (RacerPinsForm).
        "Racer Check-in PINs",

        // The backup password asked for after a backup is restored onto another PC (SecretRecovery.PasswordPromptForm).
        "Backup password",

        // What to export from Race History, and in which format (ResultsExportForm).
        "Export Results",

        // What goes in a race day's event report (EventReportForm).
        "Event Report",

        // The seconds Race Control's Penalty... adds to the selected driver (RaceControlControl.TimePenaltyForm).
        "Time penalty",

        // A warning's reason typed by hand, from the warnings list's "Other reason..." (RaceControlControl.WarningReasonForm, titled "Warning for <driver>").
        "Warning for",

        // The lap time a driver is coached to, from the board's Coach > Target lap... (RaceControlControl.TargetLapForm, titled "Target lap for <driver>", 22 Oct 2026).
        "Target lap for",

        // The garage, setup sheets and batteries (30 Sep).

        // A car added or edited: its fields lined up, the hint under the name, the notes (CarForm, titled "Add a Car" / "Edit Car").
        "Add a Car",
        "Edit Car",

        // A car's photos beside the one selected, from a car's Photos... (CarPhotosForm, titled "Photos - <car>").
        "Photos",

        // Who a car is lent to for the race day that is open, from a car's Lend Out... (LendCarForm).
        "Lend a Car",

        // The club's chassis list beside the fields for the one selected, from the Garage's Chassis Types... (ChassisTypesForm).
        "Chassis Types",

        // A car opened from the Garage's wall: a strip for the car - its thumbnail, facts and buttons - over a timeline of the setups logged for it (CarDetailForm, titled with the car's name).
        "Car Details",

        // A chassis sheet's editor: the diagram beside the selected point and the other tuning fields (ChassisTemplateForm).
        "Chassis Diagram",

        // The club's chassis sheets and their buttons, from the Garage's and the setup log's Chassis Sheets... (ChassisSheetsForm).
        "Chassis Setup Sheets",

        // A car's setup filled in against a chassis sheet: the diagram beside the sheet's fields, a line each (SetupEntryForm).
        "Setup Sheet",

        // One field of a chassis sheet: its label, kind and unit, and its options beside their buttons (SetupFieldEditorForm).
        "Edit Field",

        // A new battery pack's label, type and notes (BatteryPackEditForm).
        "Add Pack",

        // A battery pack's details changed, or retired (BatteryPackEditForm, titled "Edit <pack>").
        "Edit Pack",

        // A driver's packs and the club's, to say which a session was run on, from the Batteries tab's Set Pack... (PackPickerForm).
        "Which pack?",

        // Who a pack goes to for good, from Transfer... on a driver's Batteries tab or Club Batteries (PackTransferForm, titled "Transfer <pack>").
        "Transfer Pack",

        // The club's own packs and their buttons, from the Garage's Club Batteries... (ClubBatteriesForm).
        "Club Batteries",

        // The club tools (30 Sep).

        // One driver's handicap, from the Handicaps page's Edit... (HandicapEditForm, titled "Handicap - <driver>").
        "Handicap",

        // Time credits from recent pace, ticked before they are applied, from Suggest from History... (HandicapSuggestForm, titled "Suggest Handicaps - <class>").
        "Suggest Handicaps",

        // What a championship counts and how it adds it up, from Championship's Scoring Rules... (SeasonRulesForm, titled "Scoring rules - <championship>").
        "Scoring rules",

        // A new championship's name, from Championship's New... (TextPromptForm).
        "New Season",

        // A championship renamed, from its Rename... (TextPromptForm).
        "Rename Season",

        // A ghost car added: its name and car, and its pace from the source picked (GhostEditForm).
        "Add Ghost Car",

        // A ghost car changed: only the rows its pace source uses, the rest hidden (GhostEditForm).
        "Edit Ghost Car",

        // Whose best lap a Quick Rival is built from, from the Ghost Cars page's Quick Rival (GhostRivalPickerForm).
        "Quick Rival",

        // A badge opened from Achievements: the medallion, how rare it is and who has it (AchievementDetailForm, titled with the badge's name).
        "Badge Details",

        // A leaderboard's columns and their order - Race Control, Practice, the web page, the Big Screen (KpiPickerForm, titled "Choose columns - <leaderboard>").
        "Choose columns",

        // Which pages the sidebar shows and in what order, from the sidebar's Manage Navigation (TabVisibilityForm).
        "Manage Navigation",

        // The ? cheat sheet: every shortcut in two columns, the note to close it at the foot (ShortcutSheetForm).
        "Keyboard shortcuts",

        // The glossary from Next Steps' Glossary: the sport's words and what they mean (RacingWordsForm).
        "What these words mean",

        // What the app is, which build it is, where to write for help, and every
        // third-party component it ships - from Backup & Restore's Getting help
        // card (AboutForm).
        "About HeatCaller",

        // The beta license agreement and privacy notice: asked for before
        // HeatCaller first opens, with a tick for each part, and opened again
        // to read from the Getting help card and the About box (TermsForm).
        "HeatCaller license and privacy notice",

        // The scan-to-join poster over Print, Full screen and Close (JoinPosterForm, titled "<heading> - scan to join").
        "Scan to join",

        // A CSV file's columns matched to a driver's fields, over its first rows, before an import (CsvMappingForm, titled "Import drivers - <file>").
        "Import drivers",

        // What an entry import will do, row by row, with the check-in and replace ticks under it - from Who's Racing's Import Entries (EntryImportForm, 11 Nov 2026).
        "Import entries",

        // What is left before lights out, a tick, words and a button a line, with the club's own lines under - from Next Steps' Close the night... (CloseNightForm, 11 Nov 2026).
        "Close the Night",

        // The big editors (30 Sep).

        // A club's own look made from a copy of another: its fields and eleven colours beside the preview and the readability warnings (LookEditorForm).
        "New Look",

        // One of the club's own looks changed (LookEditorForm, titled "Edit Look - <look>").
        "Edit Look",

        // The new soundscape's name, then its description, from Soundscapes' Save Current Settings as New... (TextPromptForm).
        "Save Current Settings as New Soundscape",

        // A soundscape changed: its sounds beside the announcer's delivery and lines, from Soundscapes' Edit... (SoundscapeEditorForm, titled "Edit soundscape - <name>").
        "Edit soundscape",

        // A built-in soundscape customized into a new one, from Soundscapes' Customize... (SoundscapeEditorForm).
        "New soundscape",

        // A new ambience layer: its file, switches and a volume for each race state, from Ambience's Add Layer... (AmbienceLayerForm).
        "Add Ambience Layer",

        // An ambience layer changed, from Ambience's Edit... (AmbienceLayerForm, titled "Edit <layer>").
        "Edit Ambience Layer",

        // How a chart or results card is saved as an image: its style, its size and a preview (ChartExportForm).
        "Save chart as image",

        // One race's pace table as a window, from Race History's Pace... (PaceAnalysisForm, titled "Pace - <race>").
        "Pace",

        // The RCP track designer: the toolbar, the board with the panel of tabs down its right, and the status line under the board (RcpTrackEditorForm, titled "Track Designer - <track>"). Each tab is a catalog entry of its own, as only the one showing is checked.
        "Track Designer",

        // A track layout added or edited: the figures lined up, the notes, the photo's place beside its name and buttons, the main picture (TrackLayoutEditForm).
        "Add Track Layout",
        "Edit Track Layout",

        // A finished race played back: the lanes taking the room over one line of playback controls (RaceReplayForm, titled "Replay - <race>").
        "Replay",

        // The night's inspections as a window of their own, from Who's Racing's Scrutineering Log (TechInspectionControl in a PageWindow).
        "Scrutineering log",

        // The race day's run sheet as a window of its own, from the program's Timetable (DayScheduleControl in a PageWindow).
        "Timetable",

        // The announcer's window: the running order beside the talking points, the spotlight and the feed (AnnouncerControl, measured in a stand-in window of the same title and size - see the test's AnnouncerHost).
        "Announcer",

        // Every check before a green flag as a wall of tiles, the headline over them and Close under them (MissionControlForm).
        "Mission Control",

        // The broadcast's controls and what the announcer is doing, from Race Control's Broadcast button: the row of Hold, Play again, Skip, Notes and Votes in a card, the readings under it and Close (BroadcastWindow).
        "Broadcast",

        // The finish camera's shots: the night across the top, the list of shots beside the picture, the slider and its time under it (PhotoFinishForm).
        "Photo Finish",

        // This month's allowances: a line each with its bar and Ask for More..., the requests waiting, the email tick box, Refresh and Close (ThisMonthForm).
        "This Month",

        // Lining the split loop up against the line: the crossings paired so
        // far, the answer under them, and Save / Set to Zero / Cancel
        // (SplitLineUpForm). It used DialogFit from the day it was written
        // but was never added here, so nothing held it to anything - and its
        // Save button sat off the bottom of the window unnoticed.
        "Line Up the Split Loop",

        // Choose... beside "Also races" on a driver's profile (4 Oct):
        // DialogFit from the start, and on the list from the start.
        "Other Classes",

        // Hearing how a driver's name comes out before it is read to a room
        // (NameCheckForm, from Driver Hub's Check Names...): the drivers
        // taking the height, the line saying what is being read under them,
        // and the tick box and the count beside Say It, Say the Next One and
        // Close (10 Oct).
        "Check How Names Are Read",

        // The warning before Advanced mode, with its tick box (AdvancedModeForm, 1 Nov 2026).
        "Turn on Advanced mode?",

        // Restore default settings and the copies it saves (RestoreDefaultsForm, PutBackSettingsForm, 1 Nov 2026).
        "Restore default settings?",
        "Put back earlier settings",

        // The manager PIN before Advanced mode, when a club asks for it (ManagerPinForm, round 5).
        "Manager PIN",
    };

    /// <summary>
    /// Whether a window the layout test checked is a converted one: by its
    /// name, or one of the ways the test fills it in ("Qualifying Standings
    /// (best 2 of 3)"), or a tab inside it.
    /// </summary>
    internal static bool IsConvertedWindow(string where) =>
        ConvertedWindows.Any(window =>
            string.Equals(where, window, StringComparison.Ordinal)
            || where.StartsWith(window + " (", StringComparison.Ordinal)
            || where.StartsWith(window + Separator, StringComparison.Ordinal));

    // What each finding is, for the report's headings.
    internal const string Overlap = "Overlap";
    internal const string CutOff = "Cut off";
    internal const string PastEdge = "Past the edge";

    /// <summary>A window bigger than the screen it opens on - found by the layout test for windows, not by Check.</summary>
    internal const string TooBig = "Bigger than the screen";
    internal const string Squashed = "Squashed";
    internal const string TooNarrow = "Too narrow";
    internal const string Tabs = "Tabs";

    /// <summary>One thing that does not fit. Where is the page, and the tab inside it if there is one.</summary>
    internal sealed record Finding(string Where, string Kind, string Detail);

    private static readonly ConditionalWeakTable<Control, object> Overflowing = new();

    private static readonly object Marker = new();

    /// <summary>
    /// Says that a control is MEANT to reach past its container.
    ///
    /// The sticky headings over a wide list are the case: their strip is as
    /// wide as the list's rows, far wider than the box it sits in, and slides
    /// sideways as the list scrolls. The list itself scrolls sideways on
    /// purpose. Both would otherwise be reported on every run, and a report
    /// that always says the same wrong thing is one nobody reads.
    /// </summary>
    internal static void AllowOverflow(Control control)
    {
        if (!Overflowing.TryGetValue(control, out _))
        {
            Overflowing.Add(control, Marker);
        }
    }

    private static bool MayOverflow(Control control) => Overflowing.TryGetValue(control, out _);

    /// <summary>
    /// Checks a page that is on screen, and every tab inside it.
    ///
    /// Each tab is selected in turn to be measured, because a tab that has
    /// never been selected has never been given its real size - measuring
    /// it is measuring a control nobody has laid out. The tab that was
    /// showing is put back afterwards.
    /// </summary>
    /// <param name="settle">
    /// Run after each tab is selected, so work the page posts to itself
    /// (a BeginInvoke from a layout handler) has happened before anything
    /// is measured. The layout test pumps messages here.
    /// </param>
    /// <param name="eachView">
    /// Told each time something new is on screen - the page, then each tab
    /// as it is selected - with where it is. The layout test takes a
    /// picture of the page there.
    /// </param>
    internal static List<Finding> Check(Control page, string pageName, Action? settle = null, Action<string>? eachView = null)
    {
        var findings = new List<Finding>();

        settle?.Invoke();
        eachView?.Invoke(pageName);

        Walk(page, pageName, isPage: true, findings, settle, eachView);

        return findings;
    }

    private static void Walk(Control container, string where, bool isPage, List<Finding> findings, Action? settle, Action<string>? eachView)
    {
        // The themed scrollbars are overlays parked on a control's edge, not
        // content, and they sit over what they scroll by design.
        var kids = container.Controls
            .OfType<Control>()
            .Where(c => c.Visible && c is not ThinScrollBar)
            .ToList();

        foreach (var kid in kids)
        {
            CheckText(kid, where, findings);
            CheckSize(kid, where, findings);
        }

        CheckOverlaps(container, kids, where, isPage, findings);
        CheckEdges(container, kids, where, isPage, findings);

        foreach (var kid in kids)
        {
            if (kid is TabControl tabs)
            {
                WalkTabs(tabs, where, findings, settle, eachView);
            }
            else if (LooksInside(kid))
            {
                Walk(kid, where, isPage: false, findings, settle, eachView);
            }
        }
    }

    private static void WalkTabs(TabControl tabs, string where, List<Finding> findings, Action? settle, Action<string>? eachView)
    {
        var showing = tabs.SelectedTab;

        try
        {
            foreach (var tab in tabs.TabPages.Cast<TabPage>().ToList())
            {
                if (!ReferenceEquals(tabs.SelectedTab, tab))
                {
                    tabs.SelectedTab = tab;
                    settle?.Invoke();
                }

                // A tab the page would not let go to (a Selecting handler
                // that cancelled) is not on screen, so there is nothing of
                // it to measure.
                if (!ReferenceEquals(tabs.SelectedTab, tab) || !tab.Visible)
                {
                    continue;
                }

                var here = where + Separator + tab.Text;

                eachView?.Invoke(here);
                Walk(tab, here, isPage: false, findings, settle, eachView);
            }
        }
        finally
        {
            if (showing is not null
                && !tabs.IsDisposed
                && tabs.TabPages.Contains(showing)
                && !ReferenceEquals(tabs.SelectedTab, showing))
            {
                tabs.SelectedTab = showing;
                settle?.Invoke();
            }
        }
    }

    /// <summary>
    /// Whether to look inside something. The old repair pass's list: a
    /// grid's editing controls, a spinner's own buttons and a drop-down's
    /// edit box are part of how those are built, not page layout.
    /// </summary>
    private static bool LooksInside(Control control) =>
        control.HasChildren
        && control is not (DataGridView or ListView or ComboBox or UpDownBase or TextBoxBase or ToolStrip or DateTimePicker);

    // --- Text that does not fit its box ------------------------------------

    private static void CheckText(Control control, string where, List<Finding> findings)
    {
        switch (control)
        {
            case Label label:
                CheckLabel(label, where, findings);
                break;
            case ButtonBase button:
                CheckButton(button, where, findings);
                break;
            case ComboBox combo:
                CheckCombo(combo, where, findings);
                break;
            case NumericUpDown number:
                CheckNumber(number, where, findings);
                break;
            case TextBox { Multiline: false, BorderStyle: BorderStyle.None } box:
                CheckTextBox(box, where, findings);
                break;
            case TabControl tabs:
                CheckTabs(tabs, where, findings);
                break;
        }
    }

    /// <summary>
    /// A label given a size by hand wraps its text inside that box and
    /// drops whatever lines do not fit - which is exactly how Label draws
    /// (word break, and partly visible lines left out), so it is measured
    /// exactly that way.
    ///
    /// A label that sizes itself fits, unless whatever holds it has the
    /// last word on its height - a table row of fixed height, or a
    /// MaximumSize - and then it is asked how tall it wanted to be at the
    /// width it was given.
    ///
    /// One that ends in "..." on purpose has chosen to be cut short, and is
    /// left alone: AutoEllipsis, or QuietEllipsis - which the theme pass
    /// swaps every AutoEllipsis label over to, turning AutoEllipsis off, so
    /// asking AutoEllipsis alone reported every one of them as cut off.
    /// </summary>
    private static void CheckLabel(Label label, string where, List<Finding> findings)
    {
        if (label.AutoEllipsis || QuietEllipsis.IsAttached(label) || string.IsNullOrWhiteSpace(label.Text))
        {
            return;
        }

        if (label.AutoSize)
        {
            // A width with no height asks for the height at that width - the
            // text wrapped the way it is drawn.
            var wanted = label.GetPreferredSize(new Size(Math.Max(1, label.Width), 0));

            if (wanted.Height > label.Height + 1)
            {
                findings.Add(new Finding(where, CutOff,
                    $"{Describe(label)} needs {wanted.Height}px tall at its width of {label.Width}px and has {label.Height}px{In(label.Parent)}"));
            }

            return;
        }

        var room = new Size(
            label.ClientSize.Width - label.Padding.Horizontal,
            label.ClientSize.Height - label.Padding.Vertical);

        // Too small to hold anything at all is reported as squashed.
        if (room.Width <= 0 || room.Height <= 0)
        {
            return;
        }

        var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        if (!label.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        var needed = TextRenderer.MeasureText(label.Text, label.Font, new Size(room.Width, int.MaxValue), flags);

        if (needed.Height > room.Height + 1 || needed.Width > room.Width + 1)
        {
            findings.Add(new Finding(where, CutOff,
                $"{Describe(label)} needs {needed.Width}x{needed.Height} for its text and has {room.Width}x{room.Height}{In(label.Parent)}"));
        }
    }

    /// <summary>A flat button's border and insets round its caption, beyond TextRenderer's own room - see CheckButton.</summary>
    private const int FlatButtonInset = 16;

    /// <summary>
    /// Buttons, check boxes and radio buttons. The widths were the doctor's:
    /// a caption plus 22px for a button and 26px for a box and its gap, and
    /// a button's height is its caption's plus 12 - the rule the theme sizes
    /// every button by (AppTheme.StyleButton).
    ///
    /// And one the doctor never had, for a button: its caption measured with
    /// the room TextRenderer keeps either side of a line, which grows with
    /// the font, plus the flat button's own border and insets. The doctor's
    /// 22 was enough at the normal text size and not at 200% - Race Control's
    /// Reset, 105px wide at 200%, passed it and showed "Rese" in the layout
    /// pictures of 28 Sep. The 16 is what that picture showed was needed.
    ///
    /// One that sizes itself fits, unless whatever holds it has the last
    /// word - a table column narrower than the caption - and then it is
    /// asked what it wanted at the width it was given, as a label is.
    /// </summary>
    private static void CheckButton(ButtonBase button, string where, List<Finding> findings)
    {
        if (string.IsNullOrWhiteSpace(button.Text))
        {
            return;
        }

        if (button.AutoSize)
        {
            var wanted = button.GetPreferredSize(new Size(Math.Max(1, button.Width), 0));

            if (wanted.Width > button.Width + 1 || wanted.Height > button.Height + 1)
            {
                findings.Add(new Finding(where, CutOff,
                    $"{Describe(button)} needs {wanted.Width}x{wanted.Height} for its caption and was given {button.Width}x{button.Height}{In(button.Parent)}"));
            }

            return;
        }

        var isButton = button is Button || button is CheckBox { Appearance: Appearance.Button };
        // A check box's box, or a radio button's dot, and the gap after it: 26
        // at the normal text size, as Windows' own glyph needed, and the app's
        // tick box and dot grow with the text (TickBox.Side is 13 at the
        // normal size; RadioDot is drawn the same size).
        var chrome = isButton ? 22 : 26 - 13 + TickBox.Side;
        var room = button.Width - chrome;
        var font = button.Font;

        var oneLine = TextRenderer.MeasureText(button.Text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        // Tall enough for two lines is a caption meant to wrap - measured
        // wrapped, against the whole box.
        var tallEnoughToWrap = isButton
            ? button.Height >= 2 * font.Height + 12
            : button.Height > font.Height * 1.8;

        if (button.Text.Contains('\n') || (tallEnoughToWrap && oneLine.Width > room))
        {
            var wrapped = TextRenderer.MeasureText(button.Text, font, new Size(Math.Max(1, room), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            var tall = wrapped.Height + (isButton ? 12 : 0);

            if (wrapped.Width > room + 1 || tall > button.Height + 1)
            {
                findings.Add(new Finding(where, CutOff,
                    $"{Describe(button)} needs {wrapped.Width + chrome}x{tall} for its caption and is {button.Width}x{button.Height}{In(button.Parent)}"));
            }

            return;
        }

        var across = isButton
            ? Math.Max(oneLine.Width + chrome, TextRenderer.MeasureText(button.Text, font).Width + FlatButtonInset)
            : oneLine.Width + chrome;

        if (across > button.Width)
        {
            findings.Add(new Finding(where, CutOff,
                $"{Describe(button)} needs {across}px across for its caption and is {button.Width}px{In(button.Parent)}"));
        }

        var lineHeight = TextRenderer.MeasureText(button.Text, font).Height;
        var neededHeight = isButton ? lineHeight + 12 : lineHeight;

        if (neededHeight > button.Height + 1)
        {
            findings.Add(new Finding(where, CutOff,
                $"{Describe(button)} needs {neededHeight}px tall for its caption and is {button.Height}px{In(button.Parent)}"));
        }
    }

    /// <summary>
    /// A drop-down too narrow for its longest choice shows that choice cut
    /// short once it is picked. The arrow's width and a little room either
    /// side of the text come off the box first.
    /// </summary>
    private static void CheckCombo(ComboBox combo, string where, List<Finding> findings)
    {
        if (combo.Items.Count == 0)
        {
            return;
        }

        var widestText = string.Empty;
        var widest = 0;

        // Capped: a driver list can run to hundreds, and the longest of the
        // first few hundred says as much as the longest of all of them.
        foreach (var item in combo.Items.Cast<object>().Take(400))
        {
            var text = combo.GetItemText(item) ?? string.Empty;
            var width = TextRenderer.MeasureText(text, combo.Font).Width;

            if (width > widest)
            {
                widest = width;
                widestText = text;
            }
        }

        // The arrow's zone grows with the text (DropArrow): 25px off at the
        // normal text size, as it always was, and more as the arrow grows.
        var room = combo.Width - (DropArrow.Room - 16);

        if (widest > room)
        {
            findings.Add(new Finding(where, TooNarrow,
                $"{Describe(combo)} is {combo.Width}px wide and \"{Short(widestText)}\" needs {widest + combo.Width - room}px{In(combo.Parent)}"));
        }
    }

    /// <summary>
    /// A box of one line with no border - every one the theme outlines -
    /// shorter than the line Windows draws its text in cuts the bottom off
    /// its letters: a "g", "p" or "y" loses its tail. It sizes itself to
    /// Font.Height, which for this app's typeface is short of that line, so
    /// the theme gives it its height (InputOutline.KeepLettersWhole). Empty
    /// or not: what is typed into it later is cut just the same. What it
    /// holds is left out of the report when the box hides it.
    /// </summary>
    private static void CheckTextBox(TextBox box, string where, List<Finding> findings)
    {
        var needed = Pieces.LineHeight(box.Font);

        if (box.ClientSize.Height < needed)
        {
            var what = box.UseSystemPasswordChar || box.PasswordChar != '\0'
                ? $"{box.GetType().Name} (hidden text)"
                : Describe(box);

            findings.Add(new Finding(where, CutOff,
                $"{what} is {box.ClientSize.Height}px tall and its letters need {needed}px{In(box.Parent)}"));
        }
    }

    /// <summary>A number box too narrow for the number it is showing.</summary>
    private static void CheckNumber(NumericUpDown number, string where, List<Finding> findings)
    {
        var text = number.Text;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Its arrows are wider than Windows' at the larger text sizes
        // (UpDownButtonsSkin.Width), and the number has that much less room.
        var needed = TextRenderer.MeasureText(text, number.Font).Width + SystemInformation.VerticalScrollBarWidth + 8 + UpDownButtonsSkin.Extra;

        if (needed > number.Width)
        {
            findings.Add(new Finding(where, TooNarrow,
                $"{Describe(number)} is {number.Width}px wide and its value \"{text}\" needs {needed}px{In(number.Parent)}"));
        }
    }

    /// <summary>
    /// A row of tabs. Each caption is drawn centred in its own slot and
    /// ended with "..." when it does not fit (TabStripSkin), and a row wider
    /// than its box scrolls, with the tabs past the end out of sight.
    /// </summary>
    private static void CheckTabs(TabControl tabs, string where, List<Finding> findings)
    {
        if (tabs.TabCount == 0 || !tabs.IsHandleCreated)
        {
            return;
        }

        var rowWidth = 0;

        for (var i = 0; i < tabs.TabCount; i++)
        {
            var slot = tabs.GetTabRect(i);
            var caption = tabs.TabPages[i].Text;
            var needed = TextRenderer.MeasureText(caption, tabs.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;

            rowWidth += slot.Width;

            if (needed > slot.Width)
            {
                findings.Add(new Finding(where, Tabs,
                    $"the \"{Short(caption)}\" tab needs {needed}px for its caption and is {slot.Width}px wide"));
            }
        }

        if (!tabs.Multiline && rowWidth > tabs.ClientSize.Width)
        {
            findings.Add(new Finding(where, Tabs,
                $"{tabs.TabCount} tabs need {rowWidth}px and the row is {tabs.ClientSize.Width}px wide - the last of them are off the end"));
        }
    }

    // --- Size ----------------------------------------------------------------

    /// <summary>
    /// Something with content squeezed to nothing - usually a docked panel
    /// or grid that everything around it has taken the room from - and a
    /// grid too short to show a single row under its headings.
    /// </summary>
    private static void CheckSize(Control control, string where, List<Finding> findings)
    {
        var hasContent = control.HasChildren
            || control is DataGridView or ListBox or ListView or TextBoxBase or ComboBox or TrackBar or ButtonBase
            || (control is Label && !string.IsNullOrWhiteSpace(control.Text));

        if (hasContent && (control.Width < 4 || control.Height < 4))
        {
            findings.Add(new Finding(where, Squashed,
                $"{Describe(control)} has been squeezed to {control.Width}x{control.Height}{In(control.Parent)}"));
            return;
        }

        if (control is DataGridView { ColumnCount: > 0 } grid)
        {
            var needed = (grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0) + grid.RowTemplate.Height;

            if (grid.ClientSize.Height < needed)
            {
                findings.Add(new Finding(where, Squashed,
                    $"{Describe(grid)} is {grid.ClientSize.Height}px tall - too short for one row under its headings ({needed}px){In(grid.Parent)}"));
            }
        }
    }

    // --- Overlaps ------------------------------------------------------------

    /// <summary>
    /// Two controls in the same container covering part of each other.
    ///
    /// The old repair pass's rules for what does not count: an overlap of a
    /// pixel or less, and one control lying wholly inside another - a caption
    /// on a panel, a badge on a card, a message over an empty grid are all
    /// put there on purpose. Two docked controls cannot overlap (WinForms
    /// places them), so a pair is only looked at when one of them was placed
    /// by hand.
    /// </summary>
    private static void CheckOverlaps(Control container, List<Control> kids, string where, bool isPage, List<Finding> findings)
    {
        for (var i = 0; i < kids.Count; i++)
        {
            for (var j = i + 1; j < kids.Count; j++)
            {
                var first = kids[i];
                var second = kids[j];

                if (first.Dock != DockStyle.None && second.Dock != DockStyle.None)
                {
                    continue;
                }

                var overlap = Rectangle.Intersect(first.Bounds, second.Bounds);

                if (overlap.Width <= 1 || overlap.Height <= 1)
                {
                    continue;
                }

                if (first.Bounds.Contains(second.Bounds) || second.Bounds.Contains(first.Bounds))
                {
                    continue;
                }

                findings.Add(new Finding(where, Overlap,
                    $"{Describe(first)} at {Box(first.Bounds)} and {Describe(second)} at {Box(second.Bounds)} "
                    + $"overlap by {overlap.Width}x{overlap.Height}{(isPage ? " on the page" : In(container))}"));
            }
        }
    }

    // --- Past the edge -------------------------------------------------------

    /// <summary>
    /// Anything placed by hand that runs past the edge of what holds it.
    ///
    /// In a container that does not scroll, whatever is past the edge is
    /// simply not on screen. In one that does, running off the bottom is
    /// what scrolling is for and is fine - but running off the side means
    /// a sideways scrollbar, which on these pages is a fault being hidden
    /// rather than a way of reaching things (the old repair pass refused to
    /// turn on sideways scrolling for the same reason), so that is still
    /// reported.
    ///
    /// On the page itself, the top strip is the page's heading (PageHeader
    /// reserves it with the page's Padding), and a control placed in it
    /// sits under the heading.
    /// </summary>
    private static void CheckEdges(Control container, List<Control> kids, string where, bool isPage, List<Finding> findings)
    {
        var client = container.ClientSize;

        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        // A group box draws its frame inside its own bounds.
        var frame = container is GroupBox ? 2 : 0;
        var right = client.Width - frame;
        var bottom = client.Height - frame;
        var scrolls = container is ScrollableControl { AutoScroll: true };
        var heading = isPage ? container.Padding.Top : 0;
        var inside = isPage ? " of the page" : $" of {Describe(container)}";

        foreach (var kid in kids)
        {
            if (kid.Dock != DockStyle.None || MayOverflow(kid))
            {
                continue;
            }

            var bounds = kid.Bounds;

            if (scrolls)
            {
                if (bounds.Right > right + 1 && !MayOverflow(container))
                {
                    findings.Add(new Finding(where, PastEdge,
                        $"{Describe(kid)} at {Box(bounds)} runs {bounds.Right - right}px past the right edge{inside}, which scrolls sideways to reach it"));
                }

                continue;
            }

            if (bounds.Right > right + 1)
            {
                findings.Add(new Finding(where, PastEdge,
                    $"{Describe(kid)} at {Box(bounds)} runs {bounds.Right - right}px past the right edge{inside} and is cut off"));
            }

            if (bounds.Bottom > bottom + 1)
            {
                findings.Add(new Finding(where, PastEdge,
                    $"{Describe(kid)} at {Box(bounds)} runs {bounds.Bottom - bottom}px past the bottom{inside}, which does not scroll"));
            }

            if (bounds.Left < -1)
            {
                findings.Add(new Finding(where, PastEdge,
                    $"{Describe(kid)} at {Box(bounds)} starts {-bounds.Left}px past the left edge{inside}"));
            }

            if (bounds.Top < heading - 1)
            {
                findings.Add(new Finding(where, PastEdge, heading > 0
                    ? $"{Describe(kid)} at {Box(bounds)} starts under the page's heading, which takes the top {heading}px"
                    : $"{Describe(kid)} at {Box(bounds)} starts {-bounds.Top}px above the top{inside}"));
            }
        }
    }

    // --- Naming things ---------------------------------------------------------

    /// <summary>A control as somebody would find it: its kind, its text if it has any, and its name if it was given one.</summary>
    internal static string Describe(Control control)
    {
        var kind = control.GetType().Name;
        var text = Short(control.Text);
        var name = string.IsNullOrWhiteSpace(control.Name) ? string.Empty : $" ({control.Name})";

        return text.Length > 0 ? $"{kind} \"{text}\"{name}" : kind + name;
    }

    private static string In(Control? container) =>
        container is null ? string.Empty : $" in {Describe(container)}";

    private static string Box(Rectangle bounds) => $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}";

    private static string Short(string? text)
    {
        var flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        return flat.Length <= 32 ? flat : flat[..31] + "…";
    }
}
