namespace HeatCaller;

/// <summary>Where a setting stands in Simple mode - see SettingsTable.</summary>
internal enum SettingGroup
{
    /// <summary>On screen in Simple mode: what a race night needs.</summary>
    ShownInSimple,

    /// <summary>Tuning: on screen only in Advanced mode. Keeps its value, and keeps working, while hidden.</summary>
    HiddenInSimple,

    /// <summary>The club's own: keys, accounts, PINs, the decoder's port, backups, first-run flags and bookkeeping. Restore default settings never touches these.</summary>
    KeptOnRestore,

    /// <summary>The app remembering where things were left - the last track viewed, the window's size. Not a choice anyone made, so never counted as one.</summary>
    Memory,
}

/// <summary>
/// THE SETTINGS TABLE (1 Nov 2026, Simple and Advanced mode, round 1). Every
/// setting the app saves, in settings.json and in the voice, music, ambience
/// and imaging files, sorted into one of four groups (SettingGroup).
///
/// What reads it: the update check (SettingsModes.Decide) - an install that
/// has changed anything hidden in Simple stays in Advanced, so nothing
/// someone tuned vanishes from view - and, in round 4, Restore default
/// settings, which puts every HiddenInSimple setting back.
///
/// A new setting is in no group until someone puts it in one, and
/// SettingsTableTests fails until then: every setting has to be decided,
/// not defaulted.
///
/// By property name (nameof), so a rename is a compile error here rather
/// than a setting quietly falling out of the table.
/// </summary>
internal static class SettingsTable
{
    /// <summary>settings.json - AppSettings.</summary>
    internal static readonly IReadOnlyDictionary<string, SettingGroup> App = Build(
        shown: new[]
        {
            // Connection: connect when HeatCaller starts.
            nameof(AppSettings.DirectDecoderAutoConnect),
            nameof(AppSettings.ConnectDecodersAtStartup),

            // Race Control's Race Format box: lap limit, time limit, start type.
            nameof(AppSettings.RaceLapLimit),
            nameof(AppSettings.RaceTimeLimitMinutes),
            nameof(AppSettings.RaceStartType),

            // Practice: the Setup card, start at the gate, save on stop.
            nameof(AppSettings.PracticeTrackLayout),
            nameof(AppSettings.PracticeAutoStartOnGatePass),
            nameof(AppSettings.PracticeLapLimit),
            nameof(AppSettings.PracticeTimeLimitMinutes),
            nameof(AppSettings.AutoSavePractice),

            // Race Day: the tick to start the next race by itself.
            nameof(AppSettings.AutoAdvanceRounds),

            // AI Commentary: the master switch and the talking points tick.
            nameof(AppSettings.CommentaryEnabled),
            nameof(AppSettings.CommentaryTalkingPoints),

            // Cast: the Announcer card and the Co-host card's tick, name and voice.
            nameof(AppSettings.RecapAnnouncerName),
            nameof(AppSettings.CommentaryCoHost),
            nameof(AppSettings.CommentaryCoHostName),
            nameof(AppSettings.CommentaryTwoVoicesInHeats),
            nameof(AppSettings.CoHostElevenLabsVoiceId),
            nameof(AppSettings.CoHostElevenLabsVoiceName),
            nameof(AppSettings.CoHostHeatCallerVoice),
            nameof(AppSettings.CoHostHeatCallerVoiceName),
            nameof(AppSettings.CoHostKokoroVoiceName),

            // Soundscapes: Lap Times Only.
            nameof(AppSettings.LapTimesOnly),

            // Themes: the Looks card, the Club logo card, 24-hour time.
            nameof(AppSettings.ThemeMode),
            nameof(AppSettings.AppLook),
            nameof(AppSettings.DisplayLook),
            nameof(AppSettings.RelayLogoFile),
            nameof(AppSettings.ShowLogoInApp),
            nameof(AppSettings.ShowLogoOnScreens),
            nameof(AppSettings.Use24HourTime),

            // Local Screens: the Server card's start.
            nameof(AppSettings.WebServerAutoStart),
            nameof(AppSettings.WebServerAutoStartOffered),

            // Club Website: the Relay card's switch, Online check-in on or off.
            nameof(AppSettings.RelayEnabled),
            nameof(AppSettings.RelayCheckInEnabled),

            // Demo Mode shows in full.
            nameof(AppSettings.SaveDemoSessions),
            nameof(AppSettings.DemoFillsTheProgramme),

            // Where the app is used - asked in the wizard, and on Achievements.
            nameof(AppSettings.RacingAt),

            // Backup & Restore shows in full.
            nameof(AppSettings.UpdateChecksOn),

            // The sidebar: Manage Navigation's order, hidden pages and choices.
            // Its footer button shows in Simple too (6 Nov 2026) - a club
            // re-orders or hides pages whichever mode it is in.
            nameof(AppSettings.HiddenTabs),
            nameof(AppSettings.NavOrder),
            nameof(AppSettings.NavConnectionAboveBackup),
            nameof(AppSettings.RaceNightFocus),
            nameof(AppSettings.HideUntestedPages),
        },
        hidden: new[]
        {
            // Race Control: the gap Auto start counts (11 Nov 2026).
            nameof(AppSettings.AutoStartGapSeconds),

            // Connection.
            nameof(AppSettings.SplitLoopEnabled),
            nameof(AppSettings.SplitLoopOffsetMs),
            nameof(AppSettings.RecordTimingPasses),
            nameof(AppSettings.TimingOnItsOwnThread),

            // Mixer: When a session starts, When it ends.
            nameof(AppSettings.AudioStartWithRaces),
            nameof(AppSettings.AudioStartWithPractice),
            nameof(AppSettings.AudioStartAtStaging),
            nameof(AppSettings.AudioStartMusic),
            nameof(AppSettings.AudioStartAmbience),
            nameof(AppSettings.AudioUnmuteAnnouncer),
            nameof(AppSettings.AudioStopAmbienceAfterSession),
            nameof(AppSettings.AudioStopMusicAfterSession),
            nameof(AppSettings.MusicAfterSummary),
            nameof(AppSettings.MusicFadeOutSeconds),

            // Soundscapes: "Ask which sound to use in Set Up Tonight".
            nameof(AppSettings.AskAboutSoundInWizards),

            // AI Commentary: who writes it, how it's written, and every piece
            // behind the six section switches (round 3).
            nameof(AppSettings.CommentaryUseLocalModel),
            nameof(AppSettings.CommentaryLocalModelPath),
            nameof(AppSettings.CommentaryUseRelay),
            nameof(AppSettings.CommentaryExchangeTurns),
            nameof(AppSettings.CommentaryVoiceTags),
            nameof(AppSettings.CommentaryKeepEverything),
            nameof(AppSettings.CommentaryLowestTier),
            nameof(AppSettings.CommentaryNightsPerMonth),
            nameof(AppSettings.CommentaryBudgetNight),
            nameof(AppSettings.CommentarySegmentsOff),
            nameof(AppSettings.CommentarySectionsRemembered),
            nameof(AppSettings.CommentaryChatter),
            nameof(AppSettings.CommentaryTimeoutSeconds),
            nameof(AppSettings.CommentaryRaceIntros),
            nameof(AppSettings.CommentaryRaceSummaries),
            nameof(AppSettings.CommentaryIntrosMainsOrRivalriesOnly),
            nameof(AppSettings.CommentaryDriverIntros),
            nameof(AppSettings.CommentaryDriverIntrosMainsOnly),
            nameof(AppSettings.CommentaryQualifyingRecap),
            nameof(AppSettings.FlagResultRead),
            nameof(AppSettings.CommentaryToneStyle),
            nameof(AppSettings.SailorMouthSwears),
            nameof(AppSettings.CommentaryMaxWords),
            nameof(AppSettings.CommentaryVariety),
            nameof(AppSettings.CommentarySummaryFinishers),
            nameof(AppSettings.CommentarySummaryExtraWords),
            nameof(AppSettings.CommentarySummaryMentionFastestLap),
            nameof(AppSettings.CommentaryTalkingPointIntervalSeconds),
            nameof(AppSettings.CommentaryTalkingPointObservationCount),
            nameof(AppSettings.SummaryUseBroadcastVoice),
            nameof(AppSettings.SummaryVoiceStability),
            nameof(AppSettings.SummaryVoiceStyle),
            nameof(AppSettings.SummaryDelaySeconds),
            nameof(AppSettings.SummaryDeepDuck),
            nameof(AppSettings.SummaryDuckPercent),
            nameof(AppSettings.SummaryTeaserEnabled),
            nameof(AppSettings.SummaryTeaserText),
            nameof(AppSettings.SummaryTeaserLines),
            nameof(AppSettings.SummaryTeaserGapSeconds),
            nameof(AppSettings.RecapBroadcastOpening),
            nameof(AppSettings.RecapBroadcastClosing),
            nameof(AppSettings.RecapAdBreak),
            nameof(AppSettings.ClubNotesFromTheRoom),
            nameof(AppSettings.RoomVoteFromTheRoom),

            // The On Air show and its imaging (Sounds: Imaging).
            nameof(AppSettings.ShowThemeEnabled),
            nameof(AppSettings.ShowOpenerBuiltIn),
            nameof(AppSettings.ShowBedBuiltIn),
            nameof(AppSettings.ShowStingBuiltIn),
            nameof(AppSettings.ShowOpenerPath),
            nameof(AppSettings.ShowBedPath),
            nameof(AppSettings.ShowStingPath),
            nameof(AppSettings.ShowOpenerLevel),
            nameof(AppSettings.ShowBedLevel),
            nameof(AppSettings.ShowBedDuckPercent),
            nameof(AppSettings.ShowStingLevel),
            nameof(AppSettings.ShowVoiceEntersMs),
            nameof(AppSettings.ShowSegments),
            nameof(AppSettings.ShowRejoinEnabled),
            nameof(AppSettings.RaceStartWithOnAir),
            nameof(AppSettings.RaceStartWithWarmUp),
            nameof(AppSettings.RaceWarmUpFirstSeconds),
            nameof(AppSettings.AutoStartBetweenRaces),
            nameof(AppSettings.UseCloseNightChecklist),
            nameof(AppSettings.AutoStartWaitForMarshals),
            nameof(AppSettings.MarshalSwapsNeedDesk),
            nameof(AppSettings.MarshalNoShowRule),
            nameof(AppSettings.MarshalNoShowPenaltySeconds),
            nameof(AppSettings.AutoStartNeverEarly),
            nameof(AppSettings.PostHeatsBeforeFirstRace),
            nameof(AppSettings.BuildHeatsWhenEntriesClose),
            nameof(AppSettings.AddLateCheckInsWithoutAsking),
            nameof(AppSettings.AskForWaivers),
            nameof(AppSettings.WaiverMonths),
            nameof(AppSettings.CheckMemberships),
            nameof(AppSettings.AllowSailorMouth),

            // Cast: the Co-host's "What they're like", every "If ElevenLabs
            // runs out" Kokoro voice (5 Nov 2026), and the Recap host,
            // Technical Lead, Team Principal and Achievements reader cards.
            nameof(AppSettings.CoHostStance),
            nameof(AppSettings.CoHostKokoroAlways),
            nameof(AppSettings.CoHostKokoroStandInVoiceName),
            nameof(AppSettings.RecapShowName),
            nameof(AppSettings.RecapHostName),
            nameof(AppSettings.RecapHostElevenLabsVoiceId),
            nameof(AppSettings.RecapHostElevenLabsVoiceName),
            nameof(AppSettings.RecapHostHeatCallerVoice),
            nameof(AppSettings.RecapHostHeatCallerVoiceName),
            nameof(AppSettings.RecapHostKokoroVoiceName),
            nameof(AppSettings.RecapHostKokoroAlways),
            nameof(AppSettings.RecapHostKokoroStandInVoiceName),
            nameof(AppSettings.RecapMechanicInterview),
            nameof(AppSettings.RecapMechanicInterviewHeats),
            nameof(AppSettings.RecapMechanicName),
            nameof(AppSettings.MechanicElevenLabsVoiceId),
            nameof(AppSettings.MechanicElevenLabsVoiceName),
            nameof(AppSettings.MechanicHeatCallerVoice),
            nameof(AppSettings.MechanicHeatCallerVoiceName),
            nameof(AppSettings.MechanicKokoroVoiceName),
            nameof(AppSettings.MechanicKokoroAlways),
            nameof(AppSettings.MechanicKokoroStandInVoiceName),
            nameof(AppSettings.PrincipalBeforeRace),
            nameof(AppSettings.PrincipalAfterRace),
            nameof(AppSettings.PrincipalInHeats),
            nameof(AppSettings.PrincipalTeamsOnly),
            nameof(AppSettings.PrincipalName),
            nameof(AppSettings.PrincipalElevenLabsVoiceId),
            nameof(AppSettings.PrincipalElevenLabsVoiceName),
            nameof(AppSettings.PrincipalHeatCallerVoice),
            nameof(AppSettings.PrincipalHeatCallerVoiceName),
            nameof(AppSettings.PrincipalKokoroVoiceName),
            nameof(AppSettings.PrincipalKokoroAlways),
            nameof(AppSettings.PrincipalKokoroStandInVoiceName),
            nameof(AppSettings.DriversPickCrew),
            nameof(AppSettings.CrewVoicesHidden),
            nameof(AppSettings.JuniorNotesOnStream),
            nameof(AppSettings.JuniorNotesFromPhone),
            nameof(AppSettings.RecapBadgeHonours),
            nameof(AppSettings.RecapHonoursName),
            nameof(AppSettings.HonoursElevenLabsVoiceId),
            nameof(AppSettings.HonoursElevenLabsVoiceName),
            nameof(AppSettings.HonoursHeatCallerVoice),
            nameof(AppSettings.HonoursHeatCallerVoiceName),
            nameof(AppSettings.HonoursKokoroVoiceName),
            nameof(AppSettings.HonoursKokoroAlways),
            nameof(AppSettings.HonoursKokoroStandInVoiceName),

            // Voice: Race Calls (the between-races calls) and the voice's tuning.
            nameof(AppSettings.BetweenRacesLevel),
            nameof(AppSettings.BetweenRacesLineLength),
            nameof(AppSettings.BetweenRacesMostLines),
            nameof(AppSettings.BetweenRacesSpaceSeconds),
            nameof(AppSettings.BetweenRacesCallUpRepeatSeconds),
            nameof(AppSettings.BetweenRacesCallUpsPerRace),
            nameof(AppSettings.BetweenRacesSponsorMinutes),
            nameof(AppSettings.BetweenRacesQuietAfterRecapSeconds),
            nameof(AppSettings.BetweenRacesQuietBeforeCallSeconds),
            nameof(AppSettings.BetweenRacesCallUpsWithOnAir),
            nameof(AppSettings.BetweenRacesLongBreakNotice),
            nameof(AppSettings.BetweenRacesLongBreakMinutes),
            nameof(AppSettings.BetweenRacesAutoCountdown),
            nameof(AppSettings.BetweenRacesCountdownMarks),
            nameof(AppSettings.BetweenRacesCountdownTimesUp),
            nameof(AppSettings.BetweenRacesCallUps),
            nameof(AppSettings.BetweenRacesTwoOut),
            nameof(AppSettings.BetweenRacesMarshals),
            nameof(AppSettings.BetweenRacesNotCheckedIn),
            nameof(AppSettings.BetweenRacesHandOver),
            nameof(AppSettings.BetweenRacesComingUpNext),
            nameof(AppSettings.BetweenRacesSegments),
            nameof(AppSettings.BetweenRacesClubNotes),
            nameof(AppSettings.BetweenRacesCountdown),
            nameof(AppSettings.BetweenRacesCustomLines),
            nameof(AppSettings.BetweenRacesFlagButtons),
            nameof(AppSettings.AnnouncerDensity),
            nameof(AppSettings.SayFullNamesWithOnlineVoices),
            nameof(AppSettings.VoiceEnergyFollowsRace),
            nameof(AppSettings.StreamLiveCalls),
            nameof(AppSettings.VoiceRelayBaseUrl),

            // Race Control's Race Format box, and Race Day's auto start.
            nameof(AppSettings.RaceFinishOnNextCrossing),
            nameof(AppSettings.RaceMinLapTimeMs),
            nameof(AppSettings.RaceStreakSlowMultiplier),
            nameof(AppSettings.RaceStreakWindowPercent),
            nameof(AppSettings.RaceStreakFasterBreaks),
            nameof(AppSettings.RaceStaggerGapSeconds),
            nameof(AppSettings.RaceStaggerClock),
            nameof(AppSettings.RaceWarmUpSeconds),
            nameof(AppSettings.AutoStartStagingSeconds),
            nameof(AppSettings.AutoStartCountdownSeconds),
            nameof(AppSettings.ChainStartSequence),
            nameof(AppSettings.AutoAdvanceScope),
            nameof(AppSettings.AutoAdvanceClassName),

            // The Columns buttons and the KPI columns.
            nameof(AppSettings.RaceControlKpis),
            nameof(AppSettings.PracticeKpis),
            nameof(AppSettings.BigScreenKpis),
            nameof(AppSettings.WebKpis),
            nameof(AppSettings.RelayKpis),

            // Themes: animated header, the synthwave crowd screen.
            nameof(AppSettings.AnimateHeader),
            nameof(AppSettings.BigScreenSynthwave),
            nameof(AppSettings.BigScreenCaptions),
            nameof(AppSettings.BigScreenIntroCards),

            // Local Screens: the Port row, the Race-day manager, result editing.
            nameof(AppSettings.WebServerPort),
            nameof(AppSettings.AllowResultEditing),
            nameof(AppSettings.ManagerViaRelay),

            // Club Website: the relay address, What gets published, the Look
            // card, Personal PINs, the Email card, Room & Web.
            nameof(AppSettings.RelayBaseUrl),
            nameof(AppSettings.RelayPublishRoster),
            nameof(AppSettings.RelayAbbreviateNames),
            nameof(AppSettings.PublishDriverNightsToTheRelay),
            nameof(AppSettings.PublishNightBeforeToTheRelay),
            nameof(AppSettings.ListenAlongOnTheRelay),
            nameof(AppSettings.RelayCaptions),
            nameof(AppSettings.RelayDriverRequests),
            nameof(AppSettings.RelayResultQueries),
            nameof(AppSettings.ProtestMinutes),
            nameof(AppSettings.ClubNotesFromTheRelay),
            nameof(AppSettings.RoomVoteFromTheRelay),
            nameof(AppSettings.RelaySiteTheme),
            nameof(AppSettings.RelayTagline),
            nameof(AppSettings.RelayClubAlias),
            nameof(AppSettings.RelayShortName),
            nameof(AppSettings.RelayDriverThemes),
            nameof(AppSettings.InboxQuickReplies),
            nameof(AppSettings.InboxPinnedToSide),
            nameof(AppSettings.VideoEnabled),
            nameof(AppSettings.VideoObsHost),
            nameof(AppSettings.JuniorNamesOnStream),
            nameof(AppSettings.StreamBasics),
            nameof(AppSettings.ObsSwitchScenes),
            nameof(AppSettings.ObsSceneStaging),
            nameof(AppSettings.ObsSceneRacing),
            nameof(AppSettings.ObsSceneFinished),
            nameof(AppSettings.ObsSceneBetween),
            nameof(AppSettings.VideoObsPort),
            nameof(AppSettings.VideoLeadInSeconds),
            nameof(AppSettings.VideoUltraLowLatency),
            nameof(AppSettings.VideoOnPublicResults),
            nameof(AppSettings.RelayNewcomerWhere),
            nameof(AppSettings.RelayNewcomerWhen),
            nameof(AppSettings.RelayNewcomerJoin),
            nameof(AppSettings.RelayFirstNightGuide),
            nameof(AppSettings.RelayClubUpdates),
            nameof(AppSettings.RelaySocialLinks),
            nameof(AppSettings.RelayCheckInPhotos),
            nameof(AppSettings.RelayEmailSignInLink),
            nameof(AppSettings.RelayRacerPinsEnabled),
            nameof(AppSettings.RacerPinRotation),
            nameof(AppSettings.RacerPinsEmailAutomatically),
            nameof(AppSettings.NightEmailOffered),
            nameof(AppSettings.NightEmailAutomatic),
        },
        kept: new[]
        {
            // The retired Next Level Timing connection, kept so old files load.
            nameof(AppSettings.Host),
            nameof(AppSettings.Port),
            nameof(AppSettings.ApiKey),

            // The decoder setup: connect via, COM port, baud - and each Beta
            // decoder's source, address and port (1 Nov 2026).
            nameof(AppSettings.LastConnectionType),
            nameof(AppSettings.ILapPort),
            nameof(AppSettings.ILapBaud),
            nameof(AppSettings.StreamDecoders),

            // Backup settings.
            nameof(AppSettings.AutoBackupEnabled),
            nameof(AppSettings.AutoBackupIntervalDays),
            nameof(AppSettings.AutoBackupMaxToKeep),
            nameof(AppSettings.AutoBackupFolder),
            nameof(AppSettings.LastAutoBackupUtc),
            nameof(AppSettings.CloudBackupGoogleDrive),
            nameof(AppSettings.CloudBackupICloudDrive),
            nameof(AppSettings.CloudBackupOneDrive),
            nameof(AppSettings.LastCloudBackupUtc),
            nameof(AppSettings.LastCloudBackupProblem),
            nameof(AppSettings.BackupPassword),

            // The warm standby PC (11 Nov 2026, Phase 6.2): which PC is which is the club's own setup.
            nameof(AppSettings.StandbyServeEnabled),
            nameof(AppSettings.StandbyModeEnabled),
            nameof(AppSettings.StandbyPrimaryAddress),
            nameof(AppSettings.StandbyPin),

            // Keys, accounts and PINs.
            nameof(AppSettings.WebServerPin),
            nameof(AppSettings.WebServerCheckInPin),
            nameof(AppSettings.ManagerPin),
            nameof(AppSettings.RelayClub),
            nameof(AppSettings.RelayToken),
            nameof(AppSettings.RelayPinSalt),
            nameof(AppSettings.VoiceRelayClub),
            nameof(AppSettings.VoiceRelayToken),
            nameof(AppSettings.RelayCheckInPin),
            nameof(AppSettings.RelayCheckInSecret),
            nameof(AppSettings.RacerPins),
            nameof(AppSettings.RacerPinsIssuedUtc),
            nameof(AppSettings.RacerPinsEmailed),
            nameof(AppSettings.SmtpHost),
            nameof(AppSettings.SmtpPort),
            nameof(AppSettings.SmtpUser),
            nameof(AppSettings.SmtpFromName),
            nameof(AppSettings.SmtpPassword),
            nameof(AppSettings.VideoObsPassword),
            nameof(AppSettings.UsageEmailsOn),
            nameof(AppSettings.UsageEmailTo),
            nameof(AppSettings.SecretsLocked),
            nameof(AppSettings.Secrets),
            nameof(AppSettings.CheckInPinNeedsEntering),

            // The club's own voices and the consent recorded for them.
            nameof(AppSettings.HeatCallerClonedVoices),
            nameof(AppSettings.HeatCallerConsentVoiceId),
            nameof(AppSettings.HeatCallerConsentWho),
            nameof(AppSettings.HeatCallerConsentAtUtc),
            nameof(AppSettings.HeatCallerConsentRevokedUtc),

            // The relay's bookkeeping: what was published, what is owed.
            nameof(AppSettings.RelayResultsFromUtc),
            nameof(AppSettings.RelayLastGoodBaseUrl),
            nameof(AppSettings.RelayLastGoodClub),
            nameof(AppSettings.RelayLastGoodToken),
            nameof(AppSettings.RelayLastGoodUtc),
            nameof(AppSettings.NightBeforeOnThePage),
            nameof(AppSettings.NightEmailSentFor),
            nameof(AppSettings.NightEmailSentTo),
            nameof(AppSettings.PrivacyRepublishOwed),
            nameof(AppSettings.PrivacyRetained),
            nameof(AppSettings.PrivacyAppliedFingerprint),
            nameof(AppSettings.PrivacyAppliedFlagged),

            // Tonight's running state: what has been said, read and rotated.
            nameof(AppSettings.CommentaryFactsNight),
            nameof(AppSettings.SponsorRotationNight),
            nameof(AppSettings.SponsorMentionsNight),
            nameof(AppSettings.CommentaryContinuityNight),

            // The club's own track parts, for the Track Designer.
            nameof(AppSettings.TrackStock),

            // The club's own night templates, its own checklist lines and
            // the next night it has told the drivers about. 13 Nov 2026, Bug
            // hunt 17: these were filed as tuning, so Restore Default
            // Settings emptied them (and withdrew the announced night), and
            // Put Back Earlier Settings swapped them for an older copy's.
            nameof(AppSettings.NightTemplates),
            nameof(AppSettings.CloseNightOwnItems),
            nameof(AppSettings.ReadyOwnItems),
            nameof(AppSettings.NextNight),

            // First-run flags, one-time steps, and the mode itself.
            nameof(AppSettings.HasCompletedFirstRun),
            nameof(AppSettings.HasBuiltGarageFromProfiles),
            nameof(AppSettings.HasCaughtUpGarageFromProfiles),
            nameof(AppSettings.HasLinkedSetupsToCars),
            nameof(AppSettings.HasSeenRaceDaySetup),
            nameof(AppSettings.ShowTourOnNextStart),
            nameof(AppSettings.ShowMusicStarted),
            nameof(AppSettings.UpdateWaitingForNextStart),
            nameof(AppSettings.SettingsMode),
            nameof(AppSettings.SettingsModeDecided),

            // A lock, not tuning: a restore must not take it off.
            nameof(AppSettings.AdvancedNeedsManagerPin),

            // Retired, kept only so older files still load.
            nameof(AppSettings.DarkMode),
            nameof(AppSettings.ShowIdentLine),

            // Not a setting: what this session found when it loaded the file.
            nameof(AppSettings.LoadedUnreadable),
        },
        memory: new[]
        {
            nameof(AppSettings.StartMaximized),

            // Auto start turned off for one night (11 Nov 2026).
            nameof(AppSettings.AutoStartOffForNight),
            nameof(AppSettings.CloseNightTicks),
            nameof(AppSettings.ReadyTicks),

            // A one-time step done (11 Nov 2026) - see AppSettings.ArrivePitLaneLiveOff.
            nameof(AppSettings.PitLaneLiveArrivedOff),
            nameof(AppSettings.FontScale),
            nameof(AppSettings.NavIconsOnly),
            nameof(AppSettings.RaceControlSideFolded),
            nameof(AppSettings.AnnouncerMuted),
            nameof(AppSettings.LastTrackLayoutName),
            nameof(AppSettings.LastActiveRoundId),
            nameof(AppSettings.LastDriverProfileTransponder),
            nameof(AppSettings.DriversPageView),
            nameof(AppSettings.LastRaceProgramClass),
            nameof(AppSettings.TodayProgrammeSplit),
            nameof(AppSettings.LastHeatSize),
            nameof(AppSettings.LastHeatTrackName),
            nameof(AppSettings.LastCheckedInOnlyForHeats),
            nameof(AppSettings.LastDemoTrackName),
            nameof(AppSettings.LastDemoStagingSeconds),
            nameof(AppSettings.LastDemoCountdownSeconds),
            nameof(AppSettings.LastDemoPlaceholderCount),
            nameof(AppSettings.LastDemoCheckedDrivers),
            nameof(AppSettings.LastCheckInClassFilter),
            nameof(AppSettings.LastViewedTrackLayoutName),
            nameof(AppSettings.LastRaceControlTrackName),
            nameof(AppSettings.LapWindowSpots),
            nameof(AppSettings.RaceCallsListHeight),
            nameof(AppSettings.ChartExportStyle),
            nameof(AppSettings.ChartExportWidth),
            nameof(AppSettings.ResultsCardStyle),
            nameof(AppSettings.OnAirReadyHistoryMs),

            // Which of the EasyLap and I-Lap was last the line - kept by the
            // app for the Line Up offset, not chosen by anyone (5 Nov 2026).
            nameof(AppSettings.SplitLoopOffsetLine),

            // The voices' cache revisions, moved on by the app, not by anyone.
            nameof(AppSettings.CoHostHeatCallerRev),
            nameof(AppSettings.RecapHostHeatCallerRev),
            nameof(AppSettings.MechanicHeatCallerRev),
            nameof(AppSettings.PrincipalHeatCallerRev),
            nameof(AppSettings.HonoursHeatCallerRev),
        });

    /// <summary>voice-settings.json - VoiceProfile.</summary>
    internal static readonly IReadOnlyDictionary<string, SettingGroup> Voice = Build(
        shown: new[]
        {
            // The Speak with card, and the Wireless speaker tick.
            nameof(VoiceProfile.Engine),
            nameof(VoiceProfile.VoiceId),
            nameof(VoiceProfile.VoiceName),
            nameof(VoiceProfile.HeatCallerVoice),
            nameof(VoiceProfile.HeatCallerVoiceName),
            nameof(VoiceProfile.KokoroVoiceName),
            nameof(VoiceProfile.Volume),
            nameof(VoiceProfile.WirelessSpeaker),

            // The Race calls switches, for races and for practice.
            nameof(VoiceProfile.RaceCallsInRaces),
            nameof(VoiceProfile.RaceCallsInPractice),
        },
        hidden: new[]
        {
            // The ElevenLabs model, the Voice parameters card, Race Calls.
            nameof(VoiceProfile.ModelId),
            nameof(VoiceProfile.RaceCallModelId),
            nameof(VoiceProfile.KokoroStandsIn),
            nameof(VoiceProfile.Stability),
            nameof(VoiceProfile.SimilarityBoost),
            nameof(VoiceProfile.Style),
            nameof(VoiceProfile.SpeakerBoost),
            nameof(VoiceProfile.Speed),
            nameof(VoiceProfile.TextNormalization),
            nameof(VoiceProfile.LanguageCode),
            nameof(VoiceProfile.OutputFormat),
            nameof(VoiceProfile.MaxAnnouncementsPerLap),
            nameof(VoiceProfile.Events),
        },
        kept: new[]
        {
            nameof(VoiceProfile.ApiKey),
            nameof(VoiceProfile.ApiKeySealed),
            nameof(VoiceProfile.TemplateVersion),
        },
        memory: new[]
        {
            nameof(VoiceProfile.HeatCallerRev),
        });

    /// <summary>music.json - MusicSettings.</summary>
    internal static readonly IReadOnlyDictionary<string, SettingGroup> Music = Build(
        shown: new[]
        {
            // The Player card and Enable music.
            nameof(MusicSettings.Enabled),
            nameof(MusicSettings.Volume),
            nameof(MusicSettings.Shuffle),
        },
        hidden: new[]
        {
            // The Behavior card.
            nameof(MusicSettings.PauseDuringRaces),
            nameof(MusicSettings.ContinueAfterRace),
            nameof(MusicSettings.DuckUnderAnnouncements),
            nameof(MusicSettings.DuckToFraction),
            nameof(MusicSettings.PlayThemeMusic),
        },
        kept: new[]
        {
            // The library: the club's own tracks.
            nameof(MusicSettings.Tracks),
        },
        memory: Array.Empty<string>());

    /// <summary>ambience.json - AmbienceSettings. Its tab is hidden; the soundscape still sets it.</summary>
    internal static readonly IReadOnlyDictionary<string, SettingGroup> Ambience = Build(
        shown: new[]
        {
            // The crowd fader on the Mixer.
            nameof(AmbienceSettings.MasterVolume),
        },
        hidden: new[]
        {
            nameof(AmbienceSettings.Enabled),
            nameof(AmbienceSettings.Source),
            nameof(AmbienceSettings.ReactToEvents),
            nameof(AmbienceSettings.FadeUpSeconds),
            nameof(AmbienceSettings.FadeDownSeconds),
            nameof(AmbienceSettings.SurgeSeconds),
            nameof(AmbienceSettings.FinishCelebrationSeconds),
            nameof(AmbienceSettings.DuckUnderAnnouncements),
            nameof(AmbienceSettings.DuckToFraction),
            nameof(AmbienceSettings.BoostVoiceOverAmbience),
            nameof(AmbienceSettings.VoiceBoostMultiplier),
            nameof(AmbienceSettings.SurgeOnFastLap),
            nameof(AmbienceSettings.SurgeOnLeadChange),
            nameof(AmbienceSettings.SurgeOnTrackRecord),
            nameof(AmbienceSettings.SurgeOnJumpStart),
            nameof(AmbienceSettings.SurgeOnPersonalBest),
            nameof(AmbienceSettings.SurgeOnStreak),
            nameof(AmbienceSettings.SurgeOnLastLap),
            nameof(AmbienceSettings.SurgeOnFinish),
            nameof(AmbienceSettings.SurgeOnCloseBattle),
            nameof(AmbienceSettings.SurgeOnLapped),
            nameof(AmbienceSettings.Layers),
        },
        kept: Array.Empty<string>(),
        memory: new[]
        {
            nameof(AmbienceSettings.SoundscapeCrowdVersion),
        });

    /// <summary>imaging.json - ImagingSettings. Its tab is hidden in Simple.</summary>
    internal static readonly IReadOnlyDictionary<string, SettingGroup> Imaging = Build(
        shown: Array.Empty<string>(),
        hidden: new[]
        {
            nameof(ImagingSettings.Enabled),
            nameof(ImagingSettings.Level),
            nameof(ImagingSettings.BedLevel),
            nameof(ImagingSettings.BedDuckPercent),
            nameof(ImagingSettings.Posts),
            nameof(ImagingSettings.TalkUps),
            nameof(ImagingSettings.PostNudge),
            nameof(ImagingSettings.RunOfShow),
        },
        kept: new[]
        {
            // The club's own saved soundscapes.
            nameof(ImagingSettings.Soundscapes),
        },
        memory: Array.Empty<string>());

    /// <summary>One file's table from its four lists. Throws on a name in two of them - a setting is in exactly one group.</summary>
    private static IReadOnlyDictionary<string, SettingGroup> Build(string[] shown, string[] hidden, string[] kept, string[] memory)
    {
        var table = new Dictionary<string, SettingGroup>(StringComparer.Ordinal);

        void Put(string[] names, SettingGroup group)
        {
            foreach (var name in names)
            {
                if (!table.TryAdd(name, group))
                {
                    throw new InvalidOperationException($"SettingsTable: {name} is in two groups ({table[name]} and {group}).");
                }
            }
        }

        Put(shown, SettingGroup.ShownInSimple);
        Put(hidden, SettingGroup.HiddenInSimple);
        Put(kept, SettingGroup.KeptOnRestore);
        Put(memory, SettingGroup.Memory);

        return table;
    }
}
