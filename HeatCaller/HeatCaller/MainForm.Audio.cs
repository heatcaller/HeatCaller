using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's audio wiring: ambience reacting to the race, the session's
/// music and ambience starting and stopping, mute-all, soundscapes, the
/// announcer window, and rebuilding the Voice and Sounds tabs.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Drives ambience intensity from race state, so the crowd follows what
    /// is actually happening rather than droning at one level.
    ///
    /// Deliberately reads settings at call time rather than caching them -
    /// the Ambience tab can be edited mid-race, and the next event should
    /// respect the change without needing a restart.
    /// </summary>
    private void WireAmbienceToRace()
    {
        _ambience.Settings = _ambienceStore.Current;
        _ambience.StatusMessage += OnClientRawMessage;

        // Deliberately NOT started here. The app opening is not a session
        // starting, and audio beginning on its own at launch is startling -
        // especially on a shared club machine where someone is just
        // checking the schedule. Ambience comes up when a race or practice
        // starts (see the auto-start settings on the Mixer), when Preview
        // is pressed, or when it's switched on by hand.

        _race.StateChanged += state =>
        {
            HandleAudioAutoStart(state);
            HandleGhosts(state);

            // The run sheet's "actually started" stamp. Here rather than in
            // the start buttons because every path to a green flag - manual,
            // auto-start, Start Next, the programme - lands in this event,
            // and MarkStarted keeps only the first stamp per line.
            //
            // Not for practice or a demo: neither runs the round, and a
            // stamp from one would show the heat as started when it never
            // was.
            if (state == RaceState.Racing && !_race.IsPracticeMode && !_race.IsDemoMode && _activeRound is { } startedRound)
            {
                _daySchedule.MarkStarted(startedRound.Id, DateTime.UtcNow);
            }

            // An automatic backup that came due during a race was put off
            // (see CheckAutoBackup) - made now the track has gone quiet,
            // rather than waiting up to an hour for the next check.
            //
            // Always posted, even on the UI thread: Idle is also passed
            // through on the way INTO a session - practice resets the
            // session and then goes green in the same call - so the state is
            // read again once whatever raised this has finished, and a
            // session that has just started still puts the backup off.
            if (state is RaceState.Finished or RaceState.Idle)
            {
                TryPost(CheckAutoBackup);
            }

            // Reset BEFORE the running check - ambience might be switched on
            // mid-race, and these flags need to reflect the race that's
            // actually happening rather than whenever audio last started.
            if (state == RaceState.Racing)
            {
                _ambienceBattleFired = false;
                _ambienceLappedFired = false;
            }

            if (!_ambience.IsRunning)
            {
                return;
            }

            _ambience.SetIntensity(state switch
            {
                RaceState.Staging or RaceState.Countdown => AmbienceService.Intensity.Anticipation,
                RaceState.Racing => AmbienceService.Intensity.Racing,

                // A finish is a moment worth reacting to, then everything
                // settles back to idle between races.
                // Finished is handled by RaceCompleted below, which holds
                // at peak for the celebration window and THEN settles. This
                // used to set Peak here and never come down, because the
                // session stays Finished until the next race starts.
                RaceState.Finished => _ambience.EffectiveIntensity,

                _ => AmbienceService.Intensity.Idle,
            });
        };

        // A red flag kills the atmosphere; a restart brings it back.
        _race.FlagChanged += flag =>
        {
            if (!_ambience.IsRunning || _race.State != RaceState.Racing)
            {
                return;
            }

            _ambience.SetIntensity(flag switch
            {
                FlagState.Red => AmbienceService.Intensity.Idle,
                FlagState.Yellow => AmbienceService.Intensity.Anticipation,
                _ => AmbienceService.Intensity.Racing,
            });
        };

        _race.JumpStartDetected += (_, _) =>
        {
            if (_ambienceStore.Current.SurgeOnJumpStart)
            {
                _ambience.Surge(reason: "Jump start");
            }
        };

        // The session already flags a race-best lap on the lap event, so
        // this needs no new plumbing - and it fires only for the FASTEST of
        // the race, not every personal best, which would surge constantly.
        _race.LapProcessed += (_, _, isFastestLap) =>
        {
            if (!_ambience.IsRunning || !_ambienceStore.Current.ReactToEvents)
            {
                return;
            }

            if (isFastestLap && _ambienceStore.Current.SurgeOnFastLap)
            {
                _ambience.Surge(reason: "Fastest lap of the race");
            }

            CheckAmbienceBattle();
            CheckAmbienceLapped();
        };

        _race.PersonalBestLap += _ =>
        {
            if (_ambienceStore.Current.SurgeOnPersonalBest)
            {
                _ambience.Surge(reason: "Personal best");
            }
        };

        // Last lap - the crowd knows it's nearly over.
        _race.TimeLimitReached += () =>
        {
            if (_ambienceStore.Current.SurgeOnLastLap)
            {
                _ambience.Surge(reason: "Last lap");
            }
        };

        _race.RaceCompleted += () =>
        {
            if (!_ambience.IsRunning)
            {
                return;
            }

            // A plain Surge would restore to the base mood, which at a
            // finish is itself peak - so the crowd never came down. This
            // holds for the configured window then fades to idle.
            if (_ambienceStore.Current.SurgeOnFinish)
            {
                _ambience.CelebrateFinishThenSettle(_ambienceStore.Current.FinishCelebrationSeconds);
            }
            else
            {
                _ambience.SetIntensity(AmbienceService.Intensity.Idle);
            }
        };

        // Lead changes and track records surface as announcement text - the
        // announcer already decides what counts as either, so matching on
        // its output avoids a second set of rules that could disagree.
        _announcer.AnnouncementTextReady += text =>
        {
            // Onto the UI thread, the same guard every other announcer
            // subscriber in this file uses.
            //
            // This is raised from more than one thread: the race recap and
            // un-cached flag calls are spoken from Task.Run, and they raise
            // it too. The ambience engine is driven from a WinForms timer
            // everywhere else, so calling Surge from a pool thread was a
            // second hand on it that nothing coordinated with.
            if (IsHandleCreated && InvokeRequired)
            {
                TryPost(() => ReactToAnnouncement(text));
                return;
            }

            ReactToAnnouncement(text);
        };
    }

    /// <summary>Surges the crowd ambience for the announcements that deserve it. UI thread only.</summary>
    private void ReactToAnnouncement(string text)
    {
        if (!_ambience.IsRunning || !_ambienceStore.Current.ReactToEvents)
        {
            return;
        }

        if (_ambienceStore.Current.SurgeOnTrackRecord
            && text.Contains("track record", StringComparison.OrdinalIgnoreCase))
        {
            _ambience.Surge(AmbienceService.Intensity.Peak, "Track record");
        }
        else if (_ambienceStore.Current.SurgeOnLeadChange
                 && (text.Contains("takes the lead", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("new leader", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("lead change", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("into the lead", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("hits the front", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("grabs P1", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("leads the way", StringComparison.OrdinalIgnoreCase)))
        {
            _ambience.Surge(reason: "Lead change");
        }
        else if (_ambienceStore.Current.SurgeOnStreak
                 && text.Contains("streak", StringComparison.OrdinalIgnoreCase))
        {
            _ambience.Surge(reason: "Streak");
        }
    }

    /// <summary>
    /// Surges when two cars on the same lap are running within a second of
    /// each other.
    ///
    /// Fires at most once per race: a genuine scrap lasts many laps, and
    /// surging on every one of them would leave the ambience pinned high
    /// for the whole race rather than marking the moment it developed.
    /// </summary>
    private void CheckAmbienceBattle()
    {
        if (_ambienceBattleFired || !_ambienceStore.Current.SurgeOnCloseBattle)
        {
            return;
        }

        var ordered = _race.GetOrderedStandings();

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            // Same lap only - a one-second gap between cars a lap apart
            // isn't a battle, it's a coincidence of timing.
            // Measured at the line (see RaceGaps) - comparing lap counts
            // skipped every pair for the moment the front car had crossed
            // and the one behind hadn't yet.
            if (ordered[i].Laps < 2)
            {
                continue;
            }

            var gap = RaceGaps.Between(ordered[i + 1], ordered[i]);
            if (gap.LapsDown == 0 && gap.GapMs is > 0 and < 1000)
            {
                _ambienceBattleFired = true;
                _ambience.Surge(reason: "Close battle");
                return;
            }
        }
    }

    /// <summary>
    /// Surges when the leader first laps someone. Also once per race, for
    /// the same reason as the battle check.
    /// </summary>
    private void CheckAmbienceLapped()
    {
        if (_ambienceLappedFired || !_ambienceStore.Current.SurgeOnLapped)
        {
            return;
        }

        var ordered = _race.GetOrderedStandings();
        if (ordered.Count < 2)
        {
            return;
        }

        // A real lap down, not just the leader crossing the line first -
        // which is what the old lap-count comparison fired on, on lap one.
        if (RaceGaps.Between(ordered[^1], ordered[0]).LapsDown >= 1)
        {
            _ambienceLappedFired = true;
            _ambience.Surge(reason: "Car lapped");
        }
    }

    /// <summary>
    /// Brings music, ambience and the announcer up when a session starts,
    /// per the shared auto-start settings.
    ///
    /// Covers a quick race for free: that drives the same session through
    /// the same states as any other race, so keying off state rather than
    /// off which button was pressed means there's no separate path to keep
    /// in step.
    /// </summary>
    private void HandleAudioAutoStart(RaceState state)
    {
        var settings = _settings;

        // Practice and races are the same states - the session's own flag
        // is what distinguishes them.
        var wanted = _race.IsPracticeMode
            ? settings.AudioStartWithPractice
            : settings.AudioStartWithRaces;

        // "From staging" also fires on Racing, because PRACTICE never
        // enters Staging at all - it calls GreenFlag directly and jumps
        // straight to Racing. Matching only Staging meant practice could
        // never trigger, however the setting was configured.
        //
        // _sessionAudioStarted stops the Racing case from re-firing for a
        // race that already started at staging, or after a caution.
        var trigger = settings.AudioStartAtStaging
            ? state is RaceState.Staging or RaceState.Racing
            : state is RaceState.Racing;

        if (state is RaceState.Idle or RaceState.Finished)
        {
            // Only tear down what this actually brought up. Ambience the
            // user started by hand should survive a race ending - stopping
            // it would be the app overriding a deliberate choice.
            if (_sessionAudioStarted)
            {
                StopSessionAudio();
            }

            _sessionAudioStarted = false;
        }

        if (wanted && trigger && !_sessionAudioStarted)
        {
            _sessionAudioStarted = true;
            StartSessionAudio();
        }

        // "Stop while a race is running" and "start music when a session
        // starts" are directly contradictory. Auto-start wins, because it's
        // the more specific instruction and the one just acted on - without
        // this guard the two fought each other inside a single call: music
        // started, then stopped microseconds later, looking like auto-start
        // simply didn't work.
        var autoStartWantsMusic = wanted && _settings.AudioStartMusic;

        if (!autoStartWantsMusic
            && _musicStore.Current.PauseDuringRaces
            && state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            _music.Stop();
        }
    }


    /// <summary>
    /// Ends the session's music once the broadcast is genuinely over.
    ///
    /// Fired after the recap rather than at the flag, so a fade doesn't cut
    /// across the summary - and it still fires when the recap is switched
    /// off, or music set to fade afterwards would simply never stop.
    /// </summary>
    private void HandleSessionAudioFinished()
    {
        // Stopped at the flag instead (see StopSessionAudio). Music playing
        // now was started since, by hand or by the next session - not this
        // broadcast's to end.
        if (!_music.IsPlaying || _settings.AudioStopMusicAfterSession)
        {
            return;
        }

        // The end of the last broadcast, heard after the next session has
        // already brought its music up: the recap and the wait after it run
        // well past the flag, and the next heat can be staged meanwhile. That
        // music is the new session's - fading it here stopped it a few
        // seconds into the heat. Only another session can have set the flag
        // by now: a session's own Finished clears it (HandleAudioAutoStart)
        // before its recap even starts.
        if (_sessionAudioStarted && _settings.AudioStartMusic)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music kept playing - the next session has already started.");
            return;
        }

        switch (_settings.MusicAfterSummary)
        {
            case MusicEndBehaviour.FadeOut:
                var seconds = Math.Clamp(_settings.MusicFadeOutSeconds, 0.5, 30);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music fading out over {seconds:0.#}s - broadcast finished.");
                _ = _music.FadeOutAsync(seconds);
                break;

            case MusicEndBehaviour.StopImmediately:
                _music.Stop();
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music stopped - broadcast finished.");
                break;

            case MusicEndBehaviour.KeepPlaying:
            default:
                // Deliberately nothing - the gap between heats is exactly
                // when a soundtrack earns its keep.
                break;
        }
    }

    /// <summary>
    /// Takes down the audio that StartSessionAudio brought up.
    ///
    /// Deliberately NOT the mirror image of the start: music defaults to
    /// carrying on, because the gap between heats is when a soundtrack is
    /// most wanted. Ambience defaults to stopping, because crowd noise at
    /// an empty track just sounds wrong.
    /// </summary>
    private void StopSessionAudio()
    {
        if (_settings.AudioStopAmbienceAfterSession && _ambience.IsRunning)
        {
            // After the finish celebration, which runs from RaceCompleted
            // and holds the crowd at peak for its configured window, and then
            // faded out over the Ambience tab's fade-down time rather than
            // cut. It used to stop dead two seconds after the celebration,
            // part-way through the crowd settling - so the fade the club
            // set never played out, and the cheer just vanished.
            var celebration = _ambienceStore.Current.SurgeOnFinish
                ? Math.Clamp(_ambienceStore.Current.FinishCelebrationSeconds, 0, 300)
                : 0;
            var fade = Math.Clamp(_ambienceStore.Current.FadeDownSeconds, 0.5, 30);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (celebration > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(celebration));
                    }

                    // Only if another session hasn't started in the meantime
                    // - between heats that gap is short, and cutting the new
                    // race's atmosphere would be worse than leaving the old
                    // one running.
                    if (!_sessionAudioStarted && _ambience.IsRunning)
                    {
                        await _ambience.FadeOutAndStopAsync(fade);
                    }
                }
                catch
                {
                    // Ambience is decoration - never surface a failure.
                }
            });

            OnClientRawMessage(celebration > 0
                ? $"{AppTime.LogStampNow()}  [audio] Ambience will fade out over {fade:0.#}s after the {celebration}s finish celebration."
                : $"{AppTime.LogStampNow()}  [audio] Ambience fading out over {fade:0.#}s - session ended.");
        }

        // Music normally carries on through the recap - the checkered flag
        // is not the end of the broadcast, see HandleSessionAudioFinished.
        // Unless the club asked for it to stop at the flag ("At the flag,
        // stop: Music" on the Mixer), for a track that should go quiet the
        // moment racing does.
        if (_settings.AudioStopMusicAfterSession && _music.IsPlaying)
        {
            _music.Stop();
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music stopped - the race ended.");
        }
    }

    /// <summary>Starts whichever sources the auto-start settings name.</summary>
    private void StartSessionAudio()
    {
        // Ticking a source under "bring up" is the more specific
        // instruction, so it overrides that source's own master switch.
        // Requiring BOTH meant a disabled source silently refused to start
        // with nothing said about why - the setting looked broken.
        if (_settings.AudioStartMusic)
        {
            if (_music.IsPlaying)
            {
                // Already going - leave it rather than restarting a track
                // someone is part-way through. But not fading out: the last
                // session's end may still be taking it down, and that fade
                // used to stop it a few seconds into this one.
                _music.CancelFade();
            }
            else if (_musicStore.Current.Tracks.Count == 0)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music not started - the library is empty.");
            }
            else
            {
                _music.Settings = _musicStore.Current;
                _music.Play();

                OnClientRawMessage(_music.IsPlaying
                    ? $"{AppTime.LogStampNow()}  [audio] Music started for the new session."
                    : $"{AppTime.LogStampNow()}  [audio] Music couldn't start - no enabled track has a readable file.");
            }
        }

        if (_settings.AudioStartAmbience && !_ambience.IsRunning)
        {
            _ambience.Settings = _ambienceStore.Current;

            // ignoreEnabledSetting, for the same reason as music above.
            _ambience.Start(ignoreEnabledSetting: true);

            OnClientRawMessage(_ambience.IsRunning
                ? $"{AppTime.LogStampNow()}  [audio] Ambience started for the new session."
                : $"{AppTime.LogStampNow()}  [audio] Ambience couldn't start - no enabled layer has a readable file.");
        }

        // Unmuting matters more than it sounds: a muted announcer is easy
        // to leave set from a previous session and produces a silent race
        // with no obvious cause.
        if (_settings.AudioUnmuteAnnouncer && _announcer.Muted)
        {
            SetAllSoundMuted(false);
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Unmuted for the new session.");
        }
    }

    /// <summary>Which beds the MUTE button itself silenced, so unmuting brings back exactly those and nothing it didn't stop.</summary>
    private bool _ambienceStoppedByMute;
    private bool _musicPausedByMute;

    /// <summary>
    /// The sidebar mute button means SILENCE, not "no announcer over the
    /// crowd loop": voice, sound effects and lap beeps stop through the
    /// announcer's own Muted flag, and the ambience bed and any music -
    /// separate services the flag never touched - stop here with it.
    /// Unmuting restores only what the mute stopped: a bed that was not
    /// running does not start because the button was pressed twice.
    /// </summary>
    private void SetAllSoundMuted(bool muted)
    {
        // Read before the flag changes. Muting when already muted (the
        // Mixer page can ask too) must not replace the record of what the
        // FIRST mute stopped with "nothing" - that is what the second look
        // at the beds would find, and the unmute would then bring nothing back.
        var wasMuted = _announcer.Muted;

        _announcer.Muted = muted;
        _settings.AnnouncerMuted = muted;
        _settings.Save();

        if (muted)
        {
            // Fresh record for a new mute; added to for a repeat one, so a
            // bed started since from its own tab is silenced as well, and
            // comes back with the rest at the unmute.
            var ambienceRunning = _ambience.IsRunning;
            var musicPlaying = _music.IsPlaying && !_music.IsPaused;

            _ambienceStoppedByMute = (wasMuted && _ambienceStoppedByMute) || ambienceRunning;
            _musicPausedByMute = (wasMuted && _musicPausedByMute) || musicPlaying;

            if (ambienceRunning)
            {
                _ambience.Stop();
            }

            if (musicPlaying)
            {
                _music.Pause();
            }
        }
        else
        {
            if (_ambienceStoppedByMute)
            {
                _ambience.Start(ignoreEnabledSetting: true);
            }

            if (_musicPausedByMute)
            {
                _music.Resume();
            }

            _ambienceStoppedByMute = false;
            _musicPausedByMute = false;
        }

        UpdateMuteButtonText();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [voice] {(muted
            ? "Muted - voice, effects, beeps, ambience and music."
            : "Unmuted.")}");
    }

    /// <summary>
    /// After a soundscape is applied - on the Soundscapes page or by a venue:
    /// the pages that read their settings once are rebuilt or refreshed, and
    /// anything already playing picks up the new background straight away.
    /// Nothing is STARTED that wasn't playing - see SoundscapeBackground.
    /// (Ambience is switched ON by a soundscape with a crowd, but not
    /// started - it plays when a session or the Ambience page brings it up.)
    /// </summary>
    private void AfterSoundscapeApplied()
    {
        RebuildVoiceAndSoundTabs();

        _ambience.Settings = _ambienceStore.Current;
        if (_ambience.IsRunning)
        {
            // Layers are opened when ambience starts, so a new bed needs a restart.
            _ambience.Stop();
            _ambience.Start(ignoreEnabledSetting: true);
        }

        _music.Settings = _musicStore.Current;
        _music.Theme = _themeStore.ActiveThemeMusic();

        // Only with Theme music playing: the soundscape no longer touches
        // "My music", so music playing from the club's own library has
        // nothing new to pick up and carries on mid-track.
        if (_music.IsPlaying && _musicStore.Current.PlayThemeMusic == true)
        {
            // The queue is built when play starts; restarting plays the new soundscape's tracks.
            _music.Stop();
            _music.Play();
        }

        _ambienceControl?.RefreshAll();
        _musicControl?.RefreshAll();
    }

    /// <summary>
    /// Opens the announcer's screen, or brings it forward if it is already
    /// open. Reached from Race Control's button, the "A" shortcut and the
    /// remote - all three come through RaceControlControl.AnnouncerRequested.
    /// </summary>
    private void OpenAnnouncerWindow()
    {
        // Built once and reused for the life of the app. The X hides this
        // window rather than closing it - see AnnouncerForm - so a second
        // press brings the same one back with everything still on it, not a
        // fresh one with an empty talking-points list.
        if (_announcerWindow is not { IsDisposed: false })
        {
            _announcerWindow = new AnnouncerForm(_announcerControl);

            // Only reached on a real shutdown, given that hiding. Cleared
            // all the same, so a press afterwards finds nothing rather than
            // a disposed form - the same guard the projection window keeps.
            _announcerWindow.FormClosed += (_, _) => _announcerWindow = null;

            // Beside the main window where there is room, so the director
            // keeps Race Control and the announcer gets a screen of their
            // own rather than the two fighting over one. On the FIRST open
            // only: after that it stays wherever it was put.
            var screen = Screen.FromControl(this).WorkingArea;

            _announcerWindow.Location = Right + _announcerWindow.Width <= screen.Right
                ? new Point(Right + 8, Math.Max(screen.Top, Top))
                : new Point(
                    Math.Max(screen.Left, screen.Right - _announcerWindow.Width - 24),
                    Math.Max(screen.Top, screen.Top + 24));
        }

        var window = _announcerWindow;

        if (!window.Visible)
        {
            window.Show();
        }

        if (window.WindowState == FormWindowState.Minimized)
        {
            window.WindowState = FormWindowState.Normal;
        }

        // Always, whether the window was already up or not. The running
        // order changes during a race, and somebody pressing this twice is
        // usually pressing it BECAUSE something changed.
        _announcerControl.RefreshAll();

        window.Activate();
    }

    /// <summary>
    /// VoiceSettingsControl and SoundEffectsControl each read their store's
    /// Current value once, at construction, to populate their fields - they
    /// have no mechanism to notice the underlying data changed out from
    /// under them. Applying a theme changes exactly that, so the simplest
    /// reliable fix is rebuilding both controls from scratch afterward,
    /// rather than trying to retrofit a refresh path into forms that were
    /// never designed to be re-populated live.
    /// </summary>
    private void RebuildVoiceAndSoundTabs()
    {
        var voiceSubPage = _tabVoiceSubNav.TabPages[0];
        voiceSubPage.Controls.Remove(_voiceSettingsControl);

        // The announcement rows belong to the same control but are parented
        // in a tab of their own, so UserControl.Dispose never reaches them:
        // they have to be taken out and disposed by hand. Skipping this
        // would leave the Announcements tab showing the OLD page, still
        // wired to a control that had just been disposed - so the rows
        // would look fine, save nothing, and throw on the first Test.
        _voiceAnnouncementsSubPage.Controls.Remove(_voiceSettingsControl.AnnouncementsPage);
        _voiceSettingsControl.AnnouncementsPage.Dispose();

        _voiceSettingsControl.Dispose();
        _voiceSettingsControl = new VoiceSettingsControl(_voiceStore, _settings, _undo) { Dock = DockStyle.Fill, Announcer = _announcer, HeatCallerVoice = _heatCallerVoice };
        _voiceSettingsControl.StatusMessage += OnClientRawMessage;
        voiceSubPage.Controls.Add(_voiceSettingsControl);
        _voiceAnnouncementsSubPage.Controls.Add(_voiceSettingsControl.AnnouncementsPage);

        // Swapped inside its TAB page, not on pageSound - Sound Effects now
        // lives in a tab host, so removing/adding against the page would
        // have dropped it out of the tab and left it covering the whole
        // page instead.
        _soundEffectsSubPage.Controls.Remove(_soundEffectsControl);
        _soundEffectsControl.Dispose();
        _soundEffectsControl = new SoundEffectsControl(_soundStore, _undo) { Dock = DockStyle.Fill };
        _soundEffectsSubPage.Controls.Add(_soundEffectsControl);

        AppTheme.ApplyRecursively(voiceSubPage);
        AppTheme.ApplyRecursively(_voiceAnnouncementsSubPage);
        AppTheme.ApplyRecursively(pageSound);
    }
}
