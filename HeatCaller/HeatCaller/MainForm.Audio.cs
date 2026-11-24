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

        // RACE CONTROL'S NOW PLAYING (25 Oct 2026). The crowd and the
        // playlist are simply on or off, so they are asked on Race Control's
        // clock tick rather than told - nothing is added to either player.
        // Under names, so a window built again replaces these rather than
        // adding a second pair. See NowPlaying.
        NowPlaying.Watch("crowd", () =>
        {
            if (!_ambience.IsRunning || _ambience.IsHeld)
            {
                return null;
            }

            var text = _ambience.IsSurging && !string.IsNullOrWhiteSpace(_ambience.SurgeReason)
                ? NowPlaying.Joined("Crowd ambience", "surge: " + _ambience.SurgeReason)
                : "Crowd ambience";

            return new NowPlayingEntry(NowPlayingKind.Crowd, NowPlaying.Tidy(text, NowPlayingKind.Crowd), 0);
        });

        NowPlaying.Watch("music", () =>
        {
            if (!_music.IsPlaying || _music.IsPaused || _music.NowPlaying is not { } track)
            {
                return null;
            }

            var title = string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileNameWithoutExtension(track.FilePath) : track.Title;

            return new NowPlayingEntry(NowPlayingKind.Music, NowPlaying.Tidy(NowPlaying.Joined("Music", title), NowPlayingKind.Music), 0);
        });

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

            // WHY (23 Nov 2026, bug hunt 24): which round this green is
            // for, so a Stage or Countdown that abandons the race can take
            // that round's start stamp off again (see RaceAbandoned in
            // WireRaceAbandonedSchedule). By then a program start has already
            // moved the active round on to the next heat.
            if (state == RaceState.Racing)
            {
                _roundRacingId = !_race.IsPracticeMode && !_race.IsDemoMode ? _activeRound?.Id : null;
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
                // Off because of the mute: the unmute sets the mood this
                // change would have (see _ambienceMoodMissed).
                _ambienceMoodMissed |= _ambienceStoppedByMute;
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
                _ambienceMoodMissed |= _ambienceStoppedByMute && !_ambience.IsRunning;
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

        if (LeaderHasLappedSomeone(_race.GetOrderedStandings(), _race.IsPracticeMode))
        {
            _ambienceLappedFired = true;
            _ambience.Surge(reason: "Car lapped");
        }
    }

    /// <summary>
    /// Whether the leader has really put a car a lap down - the rule behind
    /// the "Car lapped" crowd surge.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the old check compared the leader
    /// with the last row whatever it was. On a hand-released grid every car
    /// is on the board at 0 laps from the green, so a car still held at the
    /// line (or a no-show) read as lapped the moment the first car round
    /// crossed twice; in practice a driver who joined late read as lapped
    /// at once. The once-per-race surge was spent on nobody being lapped,
    /// and the real first lapping later got no crowd reaction. Now practice
    /// never surges, and the leader is compared with the last car that has
    /// actually completed a lap.
    /// </summary>
    internal static bool LeaderHasLappedSomeone(IReadOnlyList<RacerStanding> ordered, bool isPractice)
    {
        if (isPractice || ordered.Count < 2)
        {
            return false;
        }

        var leader = ordered[0];
        RacerStanding? lastStarted = null;
        for (var i = ordered.Count - 1; i > 0; i--)
        {
            if (ordered[i].Laps >= 1)
            {
                lastStarted = ordered[i];
                break;
            }
        }

        // A real lap down, not just the leader crossing the line first -
        // which is what the old lap-count comparison fired on, on lap one.
        return lastStarted is not null && RaceGaps.Between(lastStarted, leader).LapsDown >= 1;
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
        if (!EndsMusicAfterRecap(_settings, _music.IsPlaying))
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
    /// Whether Music after the recap applies once a race's broadcast is over.
    ///
    /// Only with auto-start on for races - the end of the broadcast is only
    /// ever raised for a race (or a demo), never practice. With it off the
    /// Mixer greys the whole When it ends card, yet this still faded or
    /// stopped music the club had started by hand after every recap.
    /// </summary>
    internal static bool EndsMusicAfterRecap(AppSettings settings, bool musicPlaying) =>
        settings.AudioStartWithRaces
        && musicPlaying

        // Stopped at the flag instead (see StopSessionAudio). Music playing
        // now was started since, by hand or by the next session - not this
        // broadcast's to end.
        && !settings.AudioStopMusicAfterSession;

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
        // The session's crowd was already silenced by the mute button when
        // the race ended. Nothing below schedules an end for a bed that is
        // not running, so without this the unmute would bring the crowd back
        // at an empty track and leave it playing between heats.
        if (_settings.AudioStopAmbienceAfterSession
            && !_ambience.IsRunning
            && _announcer.Muted
            && _ambienceStoppedByMute)
        {
            _ambienceStoppedByMute = false;
        }

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

            // Recorded so the mute button knows the crowd is on its way out
            // (see SetAllSoundMuted). All three are touched on the UI thread
            // only: the task below hands its result back through TryPost.
            var endGeneration = ++_ambienceEndGeneration;
            _ambienceEnding = true;
            _ambienceFadeStartsAtUtc = DateTime.UtcNow.AddSeconds(celebration);

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
                finally
                {
                    TryPost(() => FinishAmbienceEnding(endGeneration));
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

    /// <summary>
    /// Closes the record of a session's crowd ending, once the delayed fade
    /// in StopSessionAudio has run its course. UI thread only.
    /// </summary>
    private void FinishAmbienceEnding(int generation)
    {
        // A newer session (or a newer ending) owns the record now.
        if (generation != _ambienceEndGeneration)
        {
            return;
        }

        _ambienceEnding = false;

        // Muted while the crowd was ending: the mute's Stop also called off
        // the fade (or left the delayed task nothing running to fade), so
        // nothing would ever stop the bed the unmute brings back. The crowd
        // this mute silenced was the one the session was ending anyway, so
        // it is no longer the mute's to restore.
        if (_announcer.Muted && _ambienceStoppedByMute && !_ambience.IsRunning)
        {
            _ambienceStoppedByMute = false;
        }
    }

    /// <summary>Starts whichever sources the auto-start settings name.</summary>
    private void StartSessionAudio()
    {
        // A new session calls off the last one's crowd ending - the delayed
        // task already leaves the bed alone once _sessionAudioStarted is set,
        // so the mute button must stop treating it as ending too.
        _ambienceEndGeneration++;
        _ambienceEnding = false;

        // The mute button means silence (see SetAllSoundMuted), and the
        // session only lifts it when "Unmute the announcer" is ticked under
        // "bring up". With that unticked, a session that starts while muted
        // stays muted - and the beds it brings up used to play through the
        // mute anyway, the crowd the mute itself had stopped included, while
        // the button still read Muted. So they are readied instead, and put
        // on the mute's own record, so the unmute brings them up with the
        // rest of the sound.
        var staysMuted = _announcer.Muted && !_settings.AudioUnmuteAnnouncer;

        // Ticking a source under "bring up" is the more specific
        // instruction, so it overrides that source's own master switch.
        // Requiring BOTH meant a disabled source silently refused to start
        // with nothing said about why - the setting looked broken.
        if (_settings.AudioStartMusic)
        {
            if (_music.IsPlaying && _musicReadiedForOnAir && !staysMuted)
            {
                // Opened and paused by On Air (see ReadySessionAudioForOnAir)
                // - just carried on, so no output opens at the staging call.
                _musicReadiedForOnAir = false;
                _music.CancelFade();
                _music.Resume();
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music started for the new session - opened when On Air was pressed, so nothing skipped.");
            }
            else if (_music.IsPlaying && _musicReadiedForOnAir)
            {
                // Readied by On Air, and the session stays muted (17 Oct
                // 2026, Bug Hunt 8, A15-1). The mute counted only music that
                // was audible, so this paused music was never on its record,
                // the branch below left it paused, and the unmute never
                // resumed it - silent all night. Put on the record here, so
                // the unmute plays it with the rest of the sound.
                _musicReadiedForOnAir = false;
                _music.CancelFade();
                _musicPausedByMute = true;
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music ready for the new session - held paused until the sound is unmuted.");
            }
            else if (_music.IsPlaying)
            {
                // Already going - leave it rather than restarting a track
                // someone is part-way through. But not fading out: the last
                // session's end may still be taking it down, and that fade
                // used to stop it a few seconds into this one.
                _musicReadiedForOnAir = false;
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

                if (staysMuted && _music.IsPlaying)
                {
                    // Started and paused at once, so the new session's queue
                    // is ready and the unmute's Resume plays it. Play loads
                    // the first track synchronously, so the pause lands before
                    // the output has anything to sound.
                    _music.Pause();
                    _musicPausedByMute = true;
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music ready for the new session - held paused until the sound is unmuted.");
                }
                else
                {
                    OnClientRawMessage(_music.IsPlaying
                        ? $"{AppTime.LogStampNow()}  [audio] Music started for the new session."
                        : $"{AppTime.LogStampNow()}  [audio] Music couldn't start - no enabled track has a readable file.");
                }
            }
        }

        // Opened silent by On Air (see ReadySessionAudioForOnAir): brought
        // up rather than opened, so nothing skips at the staging call - or,
        // when the session stays muted, closed again and held as below.
        if (_ambience.IsHeld)
        {
            if (staysMuted || !_settings.AudioStartAmbience)
            {
                _ambience.Stop();
            }
            else
            {
                _ambience.Release();
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Ambience started for the new session - opened when On Air was pressed, so nothing skipped.");
            }
        }

        // WHY (20 Oct 2026): a crowd already playing (a Preview left on) becomes the session's own - the Ambience page must stop owning it,
        // or its "Stop Preview" mid-race drops the mood to Idle and stops the crowd dead.
        if (_settings.AudioStartAmbience && _ambience.IsRunning && !_ambience.IsHeld)
        {
            _ambienceControl?.SessionAdoptedAmbience();
        }

        // WHY (7 Nov 2026, Bug Hunt 13): the same for the Mixer's Play
        // Everything, which can be holding the crowd and the music both - or
        // its Stop Everything mid-race cuts the race's sound dead.
        _mixerControl?.SessionAdoptedAudio(
            ambience: _settings.AudioStartAmbience && _ambience.IsRunning && !_ambience.IsHeld,
            music: _settings.AudioStartMusic && _music.IsPlaying);

        if (_settings.AudioStartAmbience && !_ambience.IsRunning && staysMuted)
        {
            // Not started: the unmute starts whatever the mute's record
            // holds, with the settings set here. A crowd the session later
            // ends (StopSessionAudio) is taken off the record again there.
            _ambience.Settings = _ambienceStore.Current;
            _ambienceStoppedByMute = true;

            // It never had this session's mood at all: whatever it comes
            // back with is left over from the last race.
            _ambienceMoodMissed = true;
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Ambience held for the new session - it starts when the sound is unmuted.");
        }
        else if (_settings.AudioStartAmbience && !_ambience.IsRunning)
        {
            _ambience.Settings = _ambienceStore.Current;

            // WHY (23 Nov 2026, bug hunt 24): the crowd opened at the mood it
            // was stopped at, in one step, before the race's mood was applied -
            // a crowd muted during the last finish cheer came back at full
            // roar over this heat's staging call. Set first, it opens right.
            _ambience.SetOpeningMood(AmbienceMoodFor(_race.State, _race.Flag));

            // ignoreEnabledSetting, for the same reason as music above.
            _ambience.Start(ignoreEnabledSetting: true);

            // WHY (23 Nov 2026, bug hunt 24): belt and braces for the Ambience
            // page - a Preview crowd stopped and this fresh one started inside
            // one message, before the page's own timer notices, is still
            // handed to the session, so the page's Stop Preview leaves it be.
            if (_ambience.IsRunning)
            {
                _ambienceControl?.SessionAdoptedAmbience();
            }

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

    /// <summary>
    /// The music and the crowd stop at once - for Race Control's Reset (26
    /// Oct 2026, asked for by Asher: "music and crowd and sponsor spot should
    /// be stopped with a reset as well"). Cut, not faded: a reset is "back to
    /// nothing". Whichever started them - the session, On Air readying them,
    /// or the club by hand.
    ///
    /// The mute's record of what it silenced is cleared with them, as Lap
    /// times only clears it (MainForm.SoundOffer), so an unmute after a
    /// reset does not bring them back. The next session starts them again
    /// as it always does (HandleAudioAutoStart). A crowd fade the last race
    /// scheduled finds nothing running and does nothing. UI thread.
    /// </summary>
    private void StopMusicAndCrowd(string why)
    {
        var stopped = new List<string>();

        // Stopped whether or not they read as on: Stop also calls off a
        // track about to be retried, and lets go of a crowd held for On Air.
        if (_music.IsPlaying)
        {
            stopped.Add("music");
        }

        _music.Stop();

        if (_ambience.IsRunning)
        {
            stopped.Add("crowd");
        }

        _ambience.Stop();

        _musicPausedByMute = false;
        _ambienceStoppedByMute = false;
        _musicReadiedForOnAir = false;

        if (stopped.Count > 0)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] {why} - {string.Join(" and ", stopped)} stopped.");
        }
    }

    /// <summary>The music was opened and paused by On Air, for the staging call to carry on - see ReadySessionAudioForOnAir.</summary>
    private bool _musicReadiedForOnAir;

    /// <summary>The programme round the race now on track went green for, or null for a freeform race, practice or a demo. Set at each green.</summary>
    private string? _roundRacingId;

    /// <summary>
    /// Hands Race Control what to do to the run sheet when Stage or
    /// Countdown abandons a running race: the abandoned heat's start stamp is
    /// taken off, and Undo of the abandon puts it back. UI thread.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): nothing on the abandon path touched
    /// the run sheet. MarkStarted keeps the first stamp only, so the abandoned
    /// heat read "on track - started 7:42 PM" until it was run again, and
    /// then was timed from the start that was thrown away.
    /// </summary>
    private void WireRaceAbandonedSchedule()
    {
        _raceControlControl.RaceAbandoned = () =>
            ClearStartOfAbandonedRound(_roundRacingId, _daySchedule.ClearStarted, _daySchedule.MarkStarted);
    }

    /// <summary>
    /// Takes the start stamp off <paramref name="roundId"/> with
    /// <paramref name="clear"/> and gives back what puts it back with
    /// <paramref name="mark"/> - or null when there is no round, or it had no
    /// stamp to take (a heat already ticked done keeps its own). Pure.
    /// </summary>
    internal static Action? ClearStartOfAbandonedRound(
        string? roundId, Func<string, DateTime?> clear, Action<string, DateTime> mark)
    {
        if (string.IsNullOrEmpty(roundId) || clear(roundId) is not { } was)
        {
            return null;
        }

        return () => mark(roundId, was);
    }

    /// <summary>
    /// Opens the session's music and crowd when On Air is pressed, silent,
    /// so the race being staged a minute later only has to bring them up
    /// (17 Oct 2026).
    ///
    /// WHY. Music and the crowd each open a sound output of their own when
    /// they start, and opening one makes Windows reconfigure the sound for a
    /// moment - everything else playing stalls. They started at staging,
    /// which with On Air is the very moment the bumper and the staging call
    /// are playing, so the broadcast skipped like an old Discman right at
    /// the hand-over. When On Air is pressed nothing is on air yet - the
    /// opening is still being written - so a stall there is heard by nobody.
    ///
    /// Only what the staging call would start anyway: races, set to come up
    /// at staging, not already started for this session, and not while the
    /// sound is muted (the session decides that at staging, as it always
    /// has). The music is opened under the show's own silence and paused at
    /// once; the crowd is opened held at silence (AmbienceService.StartHeld).
    /// If On Air is called off and the race is never staged, both stay
    /// quiet, and the next staging brings them up.
    /// </summary>
    private void ReadySessionAudioForOnAir()
    {
        var settings = _settings;

        if (_race.IsPracticeMode
            || !settings.AudioStartWithRaces
            || !settings.AudioStartAtStaging
            || _sessionAudioStarted
            || _announcer.Muted)
        {
            return;
        }

        if (settings.AudioStartMusic && !_music.IsPlaying && _musicStore.Current.Tracks.Count > 0)
        {
            _music.Settings = _musicStore.Current;

            // Under the show's silence, so the first moment of the track -
            // between it opening and the pause - is never heard.
            ShowOnAir.Begin();

            try
            {
                _music.Play();

                if (_music.IsPlaying)
                {
                    _music.Pause();
                    _musicReadiedForOnAir = true;
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music opened ahead of the race and paused - it starts at staging without a skip.");
                }
            }
            finally
            {
                ShowOnAir.End();
            }
        }

        if (settings.AudioStartAmbience && !_ambience.IsRunning)
        {
            _ambience.Settings = _ambienceStore.Current;
            _ambience.StartHeld(ignoreEnabledSetting: true);
        }
    }

    /// <summary>Which beds the MUTE button itself silenced, so unmuting brings back exactly those and nothing it didn't stop.</summary>
    private bool _ambienceStoppedByMute;
    private bool _musicPausedByMute;

    /// <summary>
    /// The race changed state or flag while the mute had the crowd off, so
    /// the mood the crowd stopped at is no longer the race's. The crowd only
    /// follows the race while it is running, and it used to come back from a
    /// mute at whatever it had when it stopped: idle under a green flag, or
    /// a racing crowd at an empty track. Only means anything while
    /// _ambienceStoppedByMute is set.
    /// </summary>
    private volatile bool _ambienceMoodMissed;

    /// <summary>The crowd's mood for the race as it stands - what the state and flag changes would have left it at.</summary>
    internal static AmbienceService.Intensity AmbienceMoodFor(RaceState state, FlagState flag) => state switch
    {
        RaceState.Staging or RaceState.Countdown => AmbienceService.Intensity.Anticipation,
        RaceState.Racing => flag switch
        {
            FlagState.Red => AmbienceService.Intensity.Idle,
            FlagState.Yellow => AmbienceService.Intensity.Anticipation,
            _ => AmbienceService.Intensity.Racing,
        },
        _ => AmbienceService.Intensity.Idle,
    };

    /// <summary>
    /// The session's crowd is on its way out: StopSessionAudio has scheduled
    /// its finish celebration and fade, and that has not yet finished. From
    /// _ambienceFadeStartsAtUtc the fade is under way (or was due to start),
    /// and a mute's Stop calls it off - so an unmute after that point must
    /// not restart the bed, or nothing would ever stop it again.
    /// _ambienceEndGeneration ties a delayed task's report back to the
    /// ending it belongs to. All UI thread only.
    /// </summary>
    private bool _ambienceEnding;
    private DateTime _ambienceFadeStartsAtUtc;
    private int _ambienceEndGeneration;

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
            //
            // Not a crowd held silent by On Air (17 Oct 2026, Bug Hunt 8,
            // A12-2): it is already silent, and stopping it here cleared the
            // hold, so the unmute opened it audible over the show's opener.
            // Left open and off the record, staging still decides - it stops
            // a held crowd and records it when the session stays muted.
            var ambienceRunning = _ambience.IsRunning && !_ambience.IsHeld;
            var musicPlaying = _music.IsPlaying && !_music.IsPaused;

            // A new record starts with nothing missed.
            if (!(wasMuted && _ambienceStoppedByMute))
            {
                _ambienceMoodMissed = false;
            }

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

            // And the imaging already playing - a jingle, a bumper, a bed
            // (22 Oct 2026, bug hunt). Not brought back at the unmute: the
            // moment it was for has gone.
            BroadcastImaging.StopAll();
        }
        else
        {
            // Unless the crowd the mute stopped was already being ended by
            // the session (see _ambienceEnding). During the finish
            // celebration it comes back - the delayed task still finds it
            // running and fades it as planned. Once the fade is due, the
            // mute has called it off, and a restarted bed would play on
            // through the gap between heats with nothing left to stop it.
            var crowdWasEnding = _ambienceEnding && DateTime.UtcNow >= _ambienceFadeStartsAtUtc;

            if (_ambienceStoppedByMute && !crowdWasEnding)
            {
                // WHY (23 Nov 2026, bug hunt 24): the crowd opened at the mood
                // it was stopped at, in one step, before the race's mood was
                // applied. Only when the race moved on while it was off, so an
                // unmute inside the finish celebration still comes back to
                // the cheer. The SetIntensity below then finds nothing to do.
                if (_ambienceMoodMissed)
                {
                    _ambience.SetOpeningMood(AmbienceMoodFor(_race.State, _race.Flag));
                }

                _ambience.Start(ignoreEnabledSetting: true);

                // The race moved on while it was off (see _ambienceMoodMissed).
                if (_ambienceMoodMissed && _ambience.IsRunning)
                {
                    _ambience.SetIntensity(AmbienceMoodFor(_race.State, _race.Flag));
                }
            }
            else if (_ambienceStoppedByMute)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Ambience left off - the session had ended and the crowd was fading out.");
            }

            _ambienceMoodMissed = false;

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
    /// After a soundscape is applied - on the Soundscapes page, by a venue or
    /// from a setup wizard - or the sound is set to lap times only:
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
            // Layers are opened when ambience starts, so a new bed needs a
            // restart. Restart rather than Stop and Start: a soundscape
            // applied while the session's crowd is fading out keeps that
            // fade, so the crowd still stops when it is due instead of
            // coming back at idle with nothing left to stop it.
            _ambience.Restart(ignoreEnabledSetting: true);
        }

        _music.Settings = _musicStore.Current;
        _music.Theme = _themeStore.ActiveThemeMusic();

        // Only with Theme music playing: the soundscape no longer touches
        // "My music", so music playing from the club's own library has
        // nothing new to pick up and carries on mid-track.
        if (_music.IsPlaying && _musicStore.Current.PlayThemeMusic == true)
        {
            // The queue is built when play starts; restarting plays the new soundscape's tracks.
            //
            // IsPlaying counts paused music as playing, so music paused by
            // the mute button or on the Music page came through here too -
            // and the restart started the new tracks unpaused, playing
            // through a mute the sidebar still showed. Paused music is
            // paused again straight after, on the new queue: the unmute's
            // Resume (the mute's record is left as it was) or resuming on
            // the Music page then brings up the new soundscape's music.
            var wasPaused = _music.IsPaused;

            _music.Stop();
            _music.Play();

            if (wasPaused && _music.IsPlaying)
            {
                _music.Pause();
            }
        }

        _ambienceControl?.RefreshAll();
        _musicControl?.RefreshAll();

        // Lap times only, and leaving it, change what the Mixer brings up
        // with a race and what is spoken between races - pages that read
        // those once (LapTimesOnly).
        _mixerControl?.ShowStartTicks();
        _commentaryControl?.ShowSpokenTicks();

        // And every level and duck, which the Mixer alone shows now - a
        // soundscape can bring its own crowd and music settings.
        _mixerControl?.RefreshAll();
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
    /// VoiceSettingsControl (the Voice and Race Calls tabs) and
    /// SoundEffectsControl each read their store's Current value once, at
    /// construction, to populate their fields - they
    /// have no mechanism to notice the underlying data changed out from
    /// under them. Applying a theme changes exactly that, so the simplest
    /// reliable fix is rebuilding both controls from scratch afterward,
    /// rather than trying to retrofit a refresh path into forms that were
    /// never designed to be re-populated live.
    /// </summary>
    private void RebuildVoiceAndSoundTabs()
    {
        // By reference, not by position (17 Oct 2026): the Voice and Sounds
        // tabs were regrouped, and TabPages[0] is only the Voice tab for as
        // long as nobody reorders them. Sound Effects below was already
        // found this way.
        var voiceSubPage = _voiceSettingsSubPage;
        voiceSubPage.Controls.Remove(_voiceSettingsControl);

        // THE RIVALRIES FIRST (17 Oct 2026). They are the last section of
        // the Race Calls page, which is disposed just below - and disposing
        // a panel disposes everything still in it. They are the app's, not
        // the page's, so they are taken out and put on the new page as they
        // are. The How often card is the page's own, and is built again with
        // it from the stores the soundscape has just saved.
        var rivalries = _voiceSettingsControl.ReleaseRivalries();

        // The announcement rows belong to the same control but are parented
        // in a tab of their own, so UserControl.Dispose never reaches them:
        // they have to be taken out and disposed by hand. Skipping this
        // would leave the Race Calls tab showing the OLD page, still wired
        // to a control that had just been disposed - so the rows would look
        // fine, save nothing, and throw on the first Test.
        _voiceAnnouncementsSubPage.Controls.Remove(_voiceSettingsControl.AnnouncementsPage);
        _voiceSettingsControl.AnnouncementsPage.Dispose();

        // The announcer's voice rows the same way (22 Oct 2026): built by
        // this control but shown at the top of the Cast tab, so its Dispose
        // never reaches them. Taken back from Cast and disposed by hand, and
        // the new control's handed over below.
        var announcerVoice = _castControl?.ReleaseAnnouncerVoice();

        _voiceSettingsControl.Dispose();

        // After the control that owns them, which may still reach them as it goes.
        announcerVoice?.Dispose();

        _voiceSettingsControl = new VoiceSettingsControl(_voiceStore, _settings, _undo, _soundStore) { Dock = DockStyle.Fill, Announcer = _announcer, HeatCallerVoice = _heatCallerVoice };
        _voiceSettingsControl.StatusMessage += OnClientRawMessage;
        voiceSubPage.Controls.Add(_voiceSettingsControl);
        _voiceAnnouncementsSubPage.Controls.Add(_voiceSettingsControl.AnnouncementsPage);

        if (rivalries is not null)
        {
            _voiceSettingsControl.HostRivalries(rivalries);
        }

        _castControl?.HostAnnouncerVoice(_voiceSettingsControl);

        // Its handle now, not when the Voice tab is next opened (22 Oct 2026).
        // A soundscape is applied from Sounds, so that tab is hidden and the
        // new page gets no handle by being added - and its handle is what
        // loads the HeatCaller voices and samples (OnHandleCreated) and what
        // its Tests check before reporting (Gone). The pickers and Tests are
        // on the Cast tab now, usable without ever opening the Voice tab.
        if (IsHandleCreated)
        {
            _ = _voiceSettingsControl.Handle;
        }

        // Swapped inside its TAB page, not on pageSound - Sound Effects now
        // lives in a tab host, so removing/adding against the page would
        // have dropped it out of the tab and left it covering the whole
        // page instead.
        //
        // Its pending edit dropped first: the soundscape has just saved a
        // whole new profile, and flushing the old rows would undo it.
        _soundEffectsSubPage.Controls.Remove(_soundEffectsControl);
        _soundEffectsControl.DropPendingEdit();
        _soundEffectsControl.Dispose();
        _soundEffectsControl = new SoundEffectsControl(_soundStore, _undo) { Dock = DockStyle.Fill };
        _soundEffectsSubPage.Controls.Add(_soundEffectsControl);

        AppTheme.ApplyRecursively(voiceSubPage);
        AppTheme.ApplyRecursively(_voiceAnnouncementsSubPage);
        AppTheme.ApplyRecursively(pageSound);
    }
}
