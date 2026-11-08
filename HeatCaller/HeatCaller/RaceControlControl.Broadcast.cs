namespace HeatCaller;

/// <summary>
/// The race director's hand on the broadcast: playing the last segment
/// again, and stopping the one on air.
///
/// WHY THESE EXIST
///
/// Asked for on 11 Oct 2026, and the Hold button on the announcer strip is
/// the reason it was obvious: Hold had been the only thing on the busiest
/// screen in the app that could do anything about the show, and it only
/// stops the NEXT thing. Everything already said was final, and everything
/// in progress ran to its end whatever happened in the room.
///
/// A race night has both. A recap talked over by forty people cheering, a
/// PA that cut out halfway, a club owner who missed the honours - those
/// want the line back. A segment that started as somebody picked up the
/// microphone, or over a crash being marshalled, wants stopping. Neither
/// was reachable without the developer console open.
///
/// WHY THEY ARE HERE AND NOT ON THE ANNOUNCER SCREEN
///
/// The announcer screen is where the show is RUN FROM, and it is on a
/// different monitor - often facing the other way, often not open at all.
/// Race Control is where the person with the microphone is looking. That is
/// the same argument AnnouncerStrip makes for itself at length, and these
/// are its buttons.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// What a replay calls itself while it holds the broadcast floor - see
    /// <see cref="ShowFloor"/>.
    /// </summary>
    private const string PlayingItAgain = "a segment being played again";

    /// <summary>
    /// Puts the last segment out again, with its own show round it where
    /// the club scores that segment.
    ///
    /// WHAT IS REPLAYED IS THE WORDS, NOT THE WRITING. Nothing goes to the
    /// model and nothing is spent: the line is the one the director already
    /// holds in LastSaid, so this costs a club nothing and works with the
    /// AI switched off at the wall, which is exactly when somebody is most
    /// likely to want it.
    ///
    /// NOT BUDGETED AND NOT REMEMBERED, deliberately. The budget is there
    /// to stop the ROOM hearing too much, and a line the room asked to hear
    /// again is not the budget's business; and the rotations that keep a
    /// phrasing from being used twice in a night have already counted this
    /// one. Playing it again is the race director's decision, not the
    /// director's.
    ///
    /// THROUGH THE QUEUE, like everything else. A test line that bypassed
    /// the voice queue would never raise SpeakingChanged, so the bed would
    /// not duck under it - the same trap TestShowMusicAsync's own note
    /// warns about.
    /// </summary>
    private async Task ReplayLastSegmentAsync()
    {
        if (Commentary?.Invoke() is not { } director || director.LastSaid is not { } last)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Nothing to play again - the announcer has not said a segment yet tonight.");

            return;
        }

        if (_announcer.Muted)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Not playing it again - the announcer is muted. Unmute it on the Mixer.");

            return;
        }

        var named = ShowBlock.NameFor(last.Kind);

        // The floor, so the announcer screen's director does not start a
        // segment of its own over the top of this one - the same claim the
        // recap sequence takes, and the same reason. Not failed on: a
        // replay asked for while the programme is running is the race
        // director overruling it, and the line below says so.
        var claimed = ShowFloor.TryClaim(PlayingItAgain);

        if (!claimed)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Playing {named} again over {ShowFloor.HeldBy ?? "what is already on"} - asked for from Race Control.");
        }

        try
        {
            // AN EXCHANGE GOES OUT AS AN EXCHANGE. LastSaid.Text is the
            // FLATTENED form, which carries "Eric: " in front of the
            // co-host's half for the Announcer screen's feed to show - and
            // a flattened exchange read over a PA reads the names out loud.
            // So the turns are spoken from the turns, in their own voices,
            // exactly as SpeakTurnPlainly does it.
            void Speak()
            {
                if (last.IsExchange)
                {
                    for (var at = 0; at < last.Turns.Count; at++)
                    {
                        var turn = last.Turns[at];

                        // Padded the way a live exchange is - trimmed for a
                        // turn with another hard against it. A replay that
                        // left a second of air where the live one has a beat
                        // would not be the same segment. See
                        // CommentaryExchange.ExchangeRun.
                        var run = CommentaryExchange.ExchangeRun.At(at, last.Turns.Count);

                        _announcer.SpeakReminder(
                            turn.Line,
                            null,
                            turn.FromCoHost ? _settings.CoHostVoice : null,
                            showOnScreens: true,
                            tail: run.AnotherFollows
                                ? AnnouncementService.SpeechTail.BackToBack
                                : AnnouncementService.SpeechTail.Normal,
                            lead: run.FollowsAnother
                                ? AnnouncementService.SpeechLead.FollowsOn
                                : AnnouncementService.SpeechLead.Normal,
                            onAir: named);
                    }

                    return;
                }

                _announcer.SpeakReminder(last.Text, showOnScreens: true, onAir: named);
            }

            var treatment = ShowMusic?.Invoke();

            // RunAsync answers false when there was no music to put round
            // it - and in that case it has NOT spoken, so the caller must.
            //
            // 17 Oct 2026, Bug Hunt 8, A04-5: only with the floor. Over the
            // recap's show it opened a second opener and bed summed on top
            // of the recap's - the 11 Oct fault ShowFloor is there to stop.
            // Unclaimed, it is spoken plainly and queues behind what is on.
            if (claimed
                && treatment is not null
                && treatment.Treats(last.Kind)
                && await treatment.RunAsync(Speak, last.Kind).ConfigureAwait(true))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [show] {named} played again, with its show round it - asked for from Race Control.");

                return;
            }

            Speak();

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] {named} played again - asked for from Race Control.");

            // And waited for before the finally lets go of the floor.
            // Speak() only queues, so without this the floor was free
            // before a word was said and the director could open a show of
            // its own over the replay - the fault fixed for tonight's recap
            // on 12 Oct. The music path above already waits inside RunAsync.
            if (treatment is not null)
            {
                await treatment.WaitForTheReadAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't play {named} again: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (claimed)
            {
                ShowFloor.Release(PlayingItAgain);
            }
        }
    }

    /// <summary>What a replayed moment calls itself while it holds the broadcast floor.</summary>
    private const string ReplayingTheMoment = "a moment being replayed";

    /// <summary>How much of the race a replay plays: the design's "take twenty seconds".</summary>
    internal const double ReplayMomentSeconds = 20;

    /// <summary>Least race audio worth replaying - less is a click, not a moment.</summary>
    internal const double LeastMomentSeconds = 3;

    /// <summary>The replay on air now, so Skip can ride it down. Null when none is.</summary>
    private CancellationTokenSource? _replayCut;

    /// <summary>
    /// Plays the last twenty seconds the room heard, again (22 Oct 2026 -
    /// roadmap "Item 6: replaying a call").
    ///
    /// THE ROOM'S OWN AUDIO, NOT THE WORDS. Play again re-speaks a segment;
    /// this plays back the finished mix - the call, the crowd, the music
    /// under it - from RaceReplayBuffer, which the race recording feeds. So
    /// it only has something to play while race recording is on, and it
    /// says so when it has not.
    ///
    /// TAKEN THE MOMENT THE BUTTON IS PRESSED. The race director presses it
    /// because of what just happened, so the twenty seconds are the ones
    /// before the press, copied before anything else plays - the sting
    /// marking the replay would otherwise be in it. Pressed after the
    /// flag, it is the last twenty seconds of the race, and the line says
    /// that instead.
    ///
    /// MARKED IN AND OUT with the show's own boundary sting when there is
    /// one (ShowTreatment.StingForABoundary / MarkOnceAsync - the seam
    /// built for exactly this), so the room can tell a replay from the race
    /// still going on. With the floor only, for the reason Play again
    /// takes it: two shows over each other is the 11 Oct fault.
    ///
    /// WHICH MOMENT IS STILL THE RACE DIRECTOR'S CALL. The roadmap's open
    /// question - which moments are worth an automatic replay, ranked by
    /// NightMoments.Rank - is not answered here; nothing replays unless
    /// somebody presses the button.
    /// </summary>
    private async Task ReplayTheMomentAsync()
    {
        if (_replayCut is not null)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] A replay is already on air - press Skip to stop it.");

            return;
        }

        // WHY (22 Oct 2026): the replay never looked at the mute, so pressed
        // while everything was muted it played the race mix over the PA
        // anyway. Now it does not start while muted.
        if (BroadcastImaging.Muted)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Not replaying - all sound is muted. Unmute first.");

            return;
        }

        var buffer = RaceReplayBuffer.Shared;
        var samples = buffer.Last(ReplayMomentSeconds);
        var seconds = samples.Length / (double)RaceReplayBuffer.SampleRate;

        // WHY (22 Oct 2026): a recording that stops partway now empties the
        // replay, so this also names that cause rather than only the tick box.
        if (seconds < LeastMomentSeconds)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Nothing to replay - no race audio is held. The replay comes from the "
                + "race recording, so \"Record each race, and cut each driver their own clip\" has to be ticked on Club Website - Recording, with a race running or just run. "
                + "If it was ticked, the recording may have stopped partway (its time limit, a low disk or a write error).");

            return;
        }

        // Still coming in: the moment just gone. Stopped a while ago: the
        // end of the race, which is what the room will hear.
        var finished = buffer.SinceLastAudio > TimeSpan.FromSeconds(5);
        var what = finished
            ? $"the last {seconds:0} seconds of the race"
            : $"the last {seconds:0} seconds";

        var claimed = ShowFloor.TryClaim(ReplayingTheMoment);

        if (!claimed)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Replaying {what} over {ShowFloor.HeldBy ?? "what is already on"} - asked for from Race Control.");
        }

        var cut = new CancellationTokenSource();
        _replayCut = cut;

        // WHY (22 Oct 2026): only Skip could cut a replay, so the mute -
        // which stops everything in BroadcastImaging's Live registry - left
        // up to twenty seconds of race audio playing over a safety call. The
        // replay is registered there now, and the mute rides it down exactly
        // as Skip does (the sting, the gain and the floor all follow cut).
        BroadcastImaging.Playing(cut, () =>
        {
            try
            {
                cut.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already over.
            }
        });

        try
        {
            var treatment = claimed ? ShowMusic?.Invoke() : null;
            var sting = treatment?.StingForABoundary();

            if (sting is not null)
            {
                await treatment!.MarkOnceAsync(sting.Clip, sting.Level, "sting", cut.Token, sting.Post).ConfigureAwait(true);
            }

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Replaying {what} - asked for from Race Control.");

            // Ridden down rather than cut when Skip is pressed, as every
            // other sound the show stops.
            var sound = new SmoothGainSampleProvider(RaceReplayBuffer.ToSound(samples), 1f, downSeconds: 0.3);

            // The bus takes no token here, so the wait is on Skip's token as
            // well: a skipped replay lets go of the floor straight away
            // rather than when its last twenty seconds would have ended,
            // while the gain glides what is left of it down to nothing.
            //
            // WHY (2026-11-05, Bug hunt 11: the replay leaked off-air lines):
            // RaceReplayBuffer is fed from the room's full mix, so it holds
            // every line kept from listeners at home - a name the club
            // shortens online, a penalty call, a car said by its number, a
            // driver's own clip. Played on air, the replay sent all of them
            // to the listen-along stream. It is the race's recorded call,
            // like the driver's own clip, so it stays in the room too.
            var playing = AudioBus.PlayAsync(sound, 0.9f, offAir: true);

            // Race Control's Now Playing (25 Oct 2026), until the bus lets it go.
            var playingId = NowPlaying.Started(NowPlayingKind.Piece, $"Replay of {what}");
            _ = playing.ContinueWith(_ => NowPlaying.Ended(playingId), TaskScheduler.Default);

            // Skip only rides the gain down, and the bus goes on playing the
            // silence to the end - so Now Playing lets go of it here too,
            // not twenty quiet seconds later (Ended is safe twice).
            using (cut.Token.Register(() =>
                   {
                       sound.Target = 0f;
                       NowPlaying.Ended(playingId);
                   }))
            {
                await Task.WhenAny(playing, Task.Delay(Timeout.Infinite, cut.Token)).ConfigureAwait(true);
            }

            if (playing.IsFaulted)
            {
                throw playing.Exception!.GetBaseException();
            }

            if (sting is not null && !cut.IsCancellationRequested)
            {
                await treatment!.MarkOnceAsync(sting.Clip, sting.Level, "sting", cut.Token, sting.Post).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Skipped - SkipWhatIsOnAir has said so - or muted, which the
            // mute's own log line covers (22 Oct 2026).
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't replay {what}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _replayCut = null;
            BroadcastImaging.Gone(cut);
            cut.Dispose();

            if (claimed)
            {
                ShowFloor.Release(ReplayingTheMoment);
            }

            // WHY (7 Nov 2026, bug hunt 14): the buffer is fed from the
            // room's mix, and the replay plays in the room - so while the
            // race was still recording, the replay went into the buffer
            // too, and pressing Replay Moment again replayed the replay.
            // The held audio starts again from here instead (Put is never
            // paused - a gap there would splice two moments, which the
            // buffer is built never to do). Only while it is being fed:
            // after the race nothing new comes in, and replaying the end
            // of the race twice has to keep working.
            if (!finished)
            {
                buffer.Restart();
            }
        }
    }

    /// <summary>Rides a replay on air down, for Skip. False when none was on.</summary>
    private bool StopTheReplay()
    {
        if (_replayCut is not { } cut)
        {
            return false;
        }

        try
        {
            cut.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Replay stopped from Race Control.");
        return true;
    }

    /// <summary>
    /// Opens tonight's club notes - the asides that are not about the
    /// racing, and the approval list for anything the room sent in.
    ///
    /// A WINDOW RATHER THAN A CARD ON A PAGE, and modal. It is opened
    /// between races, used for fifteen seconds and closed; a card would
    /// take permanent room on the screen the race director needs most, to
    /// show an empty list on almost every night.
    /// </summary>
    private void ShowTonightsClubNotes()
    {
        if (TonightsNotes?.Invoke() is not { } notes)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Club notes aren't set up on this PC.");

            return;
        }

        try
        {
            using var form = new ClubNotesForm(notes);

            form.ShowDialog(FindForm());

            var (toSay, toApprove, said) = notes.Counts();

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Club notes: {Plural.Of(toSay, "note")} ready to be said, "
                + $"{toApprove} waiting to be approved, {said} already said tonight.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't open the club notes: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens tonight's room vote: who the room reckons wins the race about
    /// to run, and what it said once that race has been run.
    ///
    /// A WINDOW, like the club notes beside it and for the same reasons -
    /// see <see cref="ShowTonightsClubNotes"/>.
    ///
    /// THE GRID COMES FROM THE SESSION, NOT THE PROGRAM. What the room is
    /// voting on is the cars actually staged: a program's assigned list is
    /// what the club intended, and a driver who did not turn up must not
    /// be on the ballot. The program's list is the fallback for a vote
    /// opened before anybody has crossed the loop, and for one opened
    /// between races, when the session still holds the last race's cars.
    /// </summary>
    /// <summary>The lost-lines window while it is open - one at a time (20 Oct 2026).</summary>
    private UnsaidLinesForm? _unsaidLines;

    /// <summary>
    /// Opens the list of every line the announcer didn't say this race, and
    /// why - from the lost/held count on the announcer strip (20 Oct 2026).
    /// Not modal, so Race Control works behind it during a race; clicking
    /// the count again brings the open one to the front.
    /// </summary>
    private void ShowUnsaidLines()
    {
        try
        {
            if (_unsaidLines is { IsDisposed: false } open)
            {
                if (open.WindowState == FormWindowState.Minimized)
                {
                    open.WindowState = FormWindowState.Normal;
                }

                open.Activate();
                return;
            }

            var form = new UnsaidLinesForm(_announcer.Load, () => Commentary?.Invoke()?.Account.RaceReport());
            form.FormClosed += (_, _) =>
            {
                // A window shown without ShowDialog is disposed by its own
                // Close, so only the reference is let go here.
                if (ReferenceEquals(_unsaidLines, form))
                {
                    _unsaidLines = null;
                }
            };

            _unsaidLines = form;
            form.Show(FindForm());
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [voice] Couldn't open the list of lines not said: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ShowTonightsRoomVote()
    {
        if (TonightsVote?.Invoke() is not { } vote)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] The room vote isn't set up on this PC.");

            return;
        }

        try
        {
            var round = _activeRound?.Invoke();

            using var form = new RoomVoteForm(
                vote,
                round?.Id,
                RaceLabel(round),
                round?.ClassName,
                GridToVoteOn(round),
                _settings.RoomVoteFromTheRoom
                    ? $"{WebServerService.VotePath} on this PC's address"
                    : null);

            form.ShowDialog(FindForm());

            StatusMessage?.Invoke(vote.OnRace is { Length: > 0 } onRace
                ? $"{AppTime.LogStampNow()}  [show] Room vote on {onRace}: {Plural.Of(vote.Votes, "vote")} in, "
                  + (vote.Taking ? "still open." : "closed.")
                : $"{AppTime.LogStampNow()}  [show] No room vote has been opened tonight.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't open the room vote: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// What to call the race on the ballot: its own label where the
    /// program gives one, with the class in front so a window open on
    /// "A-Main" says which A-main.
    /// </summary>
    private static string? RaceLabel(RoundDefinition? round)
    {
        if (round is null)
        {
            return null;
        }

        var label = (round.RoundLabel ?? string.Empty).Trim();
        var className = (round.ClassName ?? string.Empty).Trim();

        if (label.Length == 0)
        {
            return className.Length > 0 ? className : null;
        }

        return className.Length > 0 ? $"{className} {label}" : label;
    }

    /// <summary>
    /// The drivers on the ballot - see the note on
    /// <see cref="ShowTonightsRoomVote"/> for why the staged cars come
    /// first and the program's list is only the fallback.
    /// </summary>
    private IReadOnlyList<VoteOption> GridToVoteOn(RoundDefinition? round)
    {
        // THE NAME COMES FROM THE PROFILE, not from the standing.
        // RacerStanding carries a TransponderId and the lap figures and
        // nothing else - it is what the timing loop keeps, and a name is
        // not a timing fact. Both branches below therefore ask the profile
        // store the same way, so a ballot built from the staged cars and
        // one built from the program cannot name the same driver
        // differently.
        //
        // WHY (23 Oct 2026, bug hunt 10): ghost cars were on the ballot.
        // A ghost feeds the live standings like any car, so a vote opened
        // mid-race offered it to the phones - a choice that can never win,
        // since the saved results never hold a ghost - and one real car
        // plus a ghost passed the two-driver minimum. Both branches now
        // drop ghosts, the same way the relay ballot already does.
        var staged = _race.GetOrderedStandings()
            .Where(one => !string.IsNullOrWhiteSpace(one.TransponderId) && !Ghosts.IsGhost(one.TransponderId))
            .Select(one => new VoteOption(one.TransponderId, NameOnTheBallot(one.TransponderId)))
            .ToList();

        // ONLY WHILE A RACE IS ON. The standings are not cleared at the
        // flag, so once a race has finished they are still that race's
        // finishers until somebody stages the next one - and the ordinary
        // time to open a vote is exactly then, with the next round already
        // picked. Taken then, the A-main's ballot was the B-main's drivers.
        var running = _race.State is RaceState.Countdown or RaceState.Racing;

        if (round is null || (running && staged.Count >= 2))
        {
            return staged;
        }

        return round.AssignedTransponders
            .Where(one => !string.IsNullOrWhiteSpace(one) && !Ghosts.IsGhost(one))
            .Select(one => new VoteOption(one, NameOnTheBallot(one)))
            .ToList();
    }

    /// <summary>
    /// One driver as the ballot should name them - their profile's name, or
    /// their transponder where the club has never given them one.
    /// </summary>
    /// <remarks>
    /// The transponder is a poor name and it is the right fallback: a phone
    /// showing a number at least shows the car that is on the track, and
    /// dropping the entry instead would be a ballot missing a driver who is
    /// about to race. RoomVote folds duplicates by id, so two unnamed cars
    /// stay two options.
    ///
    /// Not DriverProfile.SpokenName, which is for the announcer's mouth:
    /// it prefers AnnouncerName, the box a club types a phonetic spelling
    /// into, and "Sham-u-el" on a phone screen reads as a fault. This is a
    /// label somebody taps.
    /// </remarks>
    private string NameOnTheBallot(string transponder) =>
        _profiles.Get(transponder)?.DriverName is { Length: > 0 } named ? named : transponder;

    /// <summary>
    /// Everything going out stops now, and the On Air bar goes back to
    /// nothing (26 Oct 2026, for Reset - asked for by Asher: "when using reset
    /// the voice should stop. everything should stop and be brought back to
    /// nothing"):
    ///   - the announcer: the line in the speakers, the sound playing, every
    ///     line and sound queued, a line still being written or voiced, the
    ///     countdown's ticks (AnnouncementService.SilenceNow);
    ///   - a replayed moment, the recap show and a show block's running
    ///     order, as Skip stops them - but at once, not after the line;
    ///   - every imaging piece and bed playing (BroadcastImaging.StopAll, as
    ///     the mute does);
    ///   - the sponsor spot on the Big Screen, a video's sound with it
    ///     (BigScreenForm.StopSponsorSpot);
    ///   - the music and the crowd, which the host owns (EverythingStopped -
    ///     26 Oct 2026, asked for by Asher: "music and crowd and sponsor spot
    ///     should be stopped with a reset as well"). The next race brings
    ///     them back up as it always does.
    /// On Air itself is called off by the reset (CancelAutoStart).
    /// </summary>
    private void StopTheBroadcast(string why)
    {
        // The producers first - the replay, the recap show (as the next race
        // being called ends it, so it hands out no held badges and queues no
        // further turn) and the show block - and only then the voice: cut the
        // voice first and a recap turn waiting on it could queue its next
        // line in between.
        StopTheReplay();
        EndRecapShow("the race was reset", nextRace: true);

        try
        {
            ShowMusic?.Invoke()?.AbandonBlock();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't stop the show: {ex.GetType().Name}: {ex.Message}");
        }

        _announcer.SilenceNow(why);

        BroadcastImaging.StopAll();

        if (_bigScreen is { IsDisposed: false } screen)
        {
            screen.StopSponsorSpot();
        }

        EverythingStopped?.Invoke();

        _onAirLive = false;
        UpdateOnAirBar();

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {why} - the announcer, the show, the imaging, the sponsor spot, the music and the crowd were all stopped.");
    }


    /// <summary>
    /// Stops the show that is on air: the bed is ridden down and whatever
    /// the running order had left is dropped.
    ///
    /// WHAT THIS DOES NOT DO, and it is worth being plain about because the
    /// button says Skip. The line the voice is PART WAY THROUGH is not cut
    /// off. Cutting it would mean clearing the voice queue, and the queue
    /// is where the checkered flag, the countdown, the green and a driver's
    /// release call all sit - every one of them marked priority precisely
    /// so that colour commentary cannot bin them. A Skip button that could
    /// swallow a release call and leave a car sitting on the line is worse
    /// than no Skip button, so this stops the SHOW and lets the sentence
    /// finish.
    ///
    /// In the hall that is the right behaviour anyway: a segment stopped
    /// mid-word sounds like a fault, where one that finishes its sentence
    /// and then hands back sounds like a programme moving on.
    ///
    /// Safe when nothing is on air - AbandonBlock does nothing then - and
    /// safe twice.
    ///
    /// AND THE RECAP SHOW (17 Oct 2026). The post-race sequence opens its
    /// show without a block, so this used to answer "none is on air" over a
    /// recap the whole room could hear. It is now ended first, by the same
    /// cancel the next race being called uses - the show ended, the music
    /// given back, the deep duck and the floor released - with the same rule
    /// as above: the line being spoken finishes. See EndRecapShow.
    /// </summary>
    private void SkipWhatIsOnAir()
    {
        // Before the show music check: a recap runs, and is worth stopping,
        // on a PC with no show music too.
        // A replayed moment first (22 Oct 2026): it is race audio, not a
        // show, so nothing below would reach it.
        var replayStopped = StopTheReplay();
        var recapStopped = EndRecapShow("it was skipped from Race Control") || replayStopped;

        if (ShowMusic?.Invoke() is not { } treatment)
        {
            if (!recapStopped)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [show] Nothing to skip - this PC has no show music set up.");
            }

            return;
        }

        try
        {
            // Only a segment block can be stopped this way. A race intro and
            // Play again open their show without one, so say so rather than
            // claim a stop that did not happen. The recap show was stopped
            // above, and has said so.
            if (!treatment.AbandonBlock())
            {
                if (recapStopped)
                {
                    return;
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [show] Skip stops a commentary segment block, and none is on air - "
                    + "whatever is playing now runs to its end.");

                return;
            }

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Show stopped from Race Control - the bed rides down and the rest of the "
                + "running order is dropped. The line being spoken finishes.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't stop the show: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
