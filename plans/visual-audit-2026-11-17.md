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
(PapersForm), Tab visibility / first-run wizard (light touch with Venues).

**Never reworked**, biggest first (lines of code), so the windows people
meet most often come first:

BigScreenForm, MainForm chrome, Sheet, RaceDaySetupWizard, TimePenaltyForm,
AmbienceLayerForm, ProfileForm, HeatBalanceForm, FirstRunWizard,
LapDetailWindow, RaceClassEditForm, SetupEntryForm, EventEditForm,
TemplateDaysForm, SoundscapeEditorForm, TechInspectionEditForm,
PasswordPromptForm, PackPickerForm, ChassisTemplateForm, RaceFormatForm,
AchievementDetailForm, DialogForm, RaceReplayForm, GhostRivalPickerForm,
LookEditorForm, CarTransferForm, AboutForm, NoteWindow, CsvMappingForm,
SeasonRulesForm, GhostEditForm, HandScoringForm, ApproveSignupForm,
TargetLapForm, ResultEditForm, HandicapEditForm, NameCheckForm,
PhotoFinishForm, BroadcastWindow, LateArrivalsForm, PostHeatsForm,
QualifyingStandingsForm, PickCarForm, RoundRosterForm, AppTour,
ThisMonthForm, RentalCheckOutForm, BelongsToForm, UnsaidLinesForm,
ApproveDriverRequestForm, DriversMeetingForm, WarningReasonForm,
ChartExportForm, EventReportForm, NewRaceDayForm, DeskPinForm,
CloseNightForm, QualifyingProjectionForm, SetupFieldEditorForm,
TemplateEditForm, SanctioningPresetForm, ReadinessCheckForm,
RacingWordsForm, DeleteRaceDayForm, MissionControlForm, JoinPosterForm,
TermsForm, DriverPaidForm, SponsorVideoPreviewForm, ShortcutSheetForm,
SplashForm, MainRoundForm, EntryImportForm, PasteResultForm,
WarningLadderForm, CarriedPointsForm, RacerPinsForm, RcpStockForm,
SplitLineUpForm, AdvancedModeForm, RestoreDefaultsForm, ClubBatteriesForm,
MarshalAssignmentsForm, AnnounceNightForm, BreaksForm, ReportCardForm,
ScheduleEntryForm, ClassOrderForm, PickDriverForm, AnnouncerForm,
ResultsExportForm, BulkInspectionForm, RcpPrintForm, KpiPickerForm,
FillTimesForm, OtherClassesForm, CrewVoicesForm, PageWindow,
PaceAnalysisForm, TextPromptForm.

These are not in this pass. About a hundred windows are too many to redesign
one by one in a single change, so they are left for a follow-up in batches.
The first batch should be the windows a club meets on its first night:
FirstRunWizard, RaceDaySetupWizard, ProfileForm, RaceClassEditForm,
TimePenaltyForm, NewRaceDayForm, RentalCheckOutForm and HandicapEditForm.

Before-pictures of the pages in this pass (1366x768, text 100%) are in
`layout-check/before/` (git-ignored).
