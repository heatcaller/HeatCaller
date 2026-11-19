# Visual audit: every page and window (17 Nov 2026)

What was checked: every sidebar page (and its tabs) and every window in
`HeatCaller/HeatCaller` (110 classes that derive from `Form`). Each one's git
history was searched for a real visual rework: a redesign, mock-ups, cards,
tiles or a rethought layout. Commits that only moved a page onto the layout
pieces (the "Layout overhaul b45-b67" series) or added a feature card do not
count.

## Sidebar pages

| Page | Reworked? | By |
|---|---|---|
| Race Control | Yes | fold the clock column, Expand the board, flag card (7c45e0e1, e97b5bf8) |
| Race Day (Who's Racing, Setup, Next Steps, Inbox) | Yes | pit board roster, Tonight cards, run sheet, triage console (#243, #246, #247) |
| Championship | Yes | a card per championship, four views (9c418999) |
| Driver Hub | Yes | Card and Table views (cf003a27) |
| Track Layouts | Yes | plan stage, racing tonight (a06868ee) |
| Race History | Yes | calendar rail, command bar (a569c713) |
| Race Analytics | Yes | RaceCoach, Lap Times as race engineer (#188, afae7873) |
| Venues | Yes | tiles, cards, venue card list (202327fa): **the template for this pass** |
| Classes | No: given the jazz-up in this pass | |
| Rentals | No: given the jazz-up in this pass | |
| Handicaps | No: given the jazz-up in this pass | |
| Achievements | No: given the jazz-up in this pass | |
| Garage | No: given the jazz-up in this pass | |
| Practice | No: given the jazz-up in this pass | |
| Ghost Cars | No: given the jazz-up in this pass | |
| Demo Mode | No: given the jazz-up in this pass | |
| Themes | No: given the jazz-up in this pass | |
| Sponsors | No: only feature cards added; given the jazz-up in this pass | |
| Local Screens | No: only feature cards added; given the jazz-up in this pass | |
| Club Website (Website, Recording) | No: given the jazz-up in this pass | |
| Voice (Voice, Cast, Race Calls, AI Commentary) | Cast lined up (33ab8ae3) and left as it was; the other three given the jazz-up in this pass | |
| Sounds (7 tabs) | Level meters only (70c4ddbf); six tabs given the jazz-up in this pass, Mixer left as it was (a card per fader already) | |
| Connection | No: given the jazz-up in this pass | |
| Backup & Restore | No: only a Check for Updates card; given the jazz-up in this pass | |

## Windows

All 110 windows are themed already. `AppTheme.ApplyRecursively` gives each one
the look's colours, the dark title bar (WindowChrome) and the dimming
behind a dialog (ModalScrim). The question here is whether a window's *layout*
was ever redesigned.

**Reworked:** Track Designer (RcpTrackEditorForm, RcpBuildForm), Car window
(CarForm), Desk Inbox (DeskInboxForm, CustomVoteForm), Track Layout edit
(TrackLayoutEditForm), Overlay editor Studio (OverlayEditorForm), Papers
(PapersForm), Tab visibility (light touch with Venues).

**Reworked in the first dialog batch (18 Nov 2026):** FirstRunWizard,
NewRaceDayForm, RaceDaySetupWizard, RentalCheckOutForm, ProfileForm,
HandicapEditForm, RaceClassEditForm and TimePenaltyForm. The two wizards share
one step header: a progress bar, "STEP 2 OF 4" in small caps, a dark title, and
numbered marks on the rail.

**Reworked in the second dialog batch (18 Nov 2026), the race-night windows:**
HeatBalanceForm, PostHeatsForm, LapDetailWindow, LateArrivalsForm,
SetupEntryForm, ResultEditForm, EventEditForm and TechInspectionEditForm.

**Reworked in batches 3 to 7 (18-19 Nov 2026, PRs 259, 261, 263, 264 and
this one):**

- Qualifying and results: PhotoFinish, QualifyingStandings,
  QualifyingProjection, MainRound, SplitLineUp, HandScoring, PasteResult,
  FillTimes, ResultsExport, EventReport.
- Rosters and pickers: NameCheck, RoundRoster, PickDriver, PickCar,
  OtherClasses.
- Race Control and desk popups: CloseNight, WarningReason, WarningLadder,
  DriverPaid, DriversMeeting, TargetLap, DeskPin.
- Setup: TemplateDays, Breaks, ClassOrder, ScheduleEntry.
- Rules: RaceFormat, SeasonRules, SanctioningPreset, CarriedPoints,
  RacerPins.
- Garage: ChassisTemplate, SetupFieldEditor, CarTransfer, PackPicker,
  ClubBatteries.
- Sign-ups and imports: ApproveSignup, ApproveDriverRequest, BelongsTo,
  EntryImport, DeleteRaceDay.
- Sound: AmbienceLayer, CrewVoices, UnsaidLines.
- Ghosts and charts: GhostEdit, GhostRivalPicker, RaceReplay, ChartExport,
  KpiPicker.
- Looks and reports: LookEditor, AchievementDetail, ReportCard, ThisMonth,
  JoinPoster (primary button only), SponsorVideoPreview.
- Track: RcpStock, RcpPrint, MarshalAssignments, BulkInspection,
  ReadinessCheck, AnnounceNight.
- App windows: About, Terms, AdvancedMode, RestoreDefaults, RacingWords,
  PasswordPrompt, TextPrompt, ShortcutSheet, AppTour (callout only),
  MissionControl, BroadcastWindow, DialogForm (the app's message box),
  BigScreenForm.

**Missed by the first audit, found by the look-back and done in batch 8
(PR 267):** the 17 Nov list counted 110 windows, but main has 132 Form
classes. These 23 share a file with a bigger window:

- Garage: CarDetail, CarPhotos, ChassisTypes, LendCar, ChassisSheets,
  UnlinkedSetups.
- Batteries, rentals and classes: BatteryPackEdit, PackTransfer, RentalPrompt,
  RentalUnitEdit, HandicapSuggest, TechChecklist.
- Signups and the desk: InviteDrivers, SignupReason, DriverRequestReason,
  QuickReplies, CallOff, RoundRoster's DriverPicker.
- Settings: QuickClass, QuickDriver, ManagerPin, PutBackSettings.

RaceClassManagerForm is only the Classes page in a window, so it was left
alone.

**Looked at and left as they were, on purpose:**

- TemplateEditForm, SoundscapeEditorForm: they already had cards, and more
  would shrink their fields.
- CsvMappingForm: only its primary button changed. A card cost rows of its
  grid.
- AnnouncerForm, PaceAnalysisForm: frames around a page's own control.
  Restyling them would change that page.
- NoteWindow: the hover note. It already matches the app's tooltips.
- SplashForm: already drawn with the app's branding, and anything more would
  cost startup time.
- PageWindow: anything added would change how a popped-out page looks.
- Sheet: the dark see-through layer behind an open dialog (in
  ControlSkins.cs). It has nothing of its own to restyle.
- MainForm chrome: the sidebar holds the Race Control, Race Day and
  Championship nav tabs, which are not to be changed.

The Race Control, Race Day and Championship pages themselves were not changed
in any batch. Only the popups they open were restyled, each in its own class.
