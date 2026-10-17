namespace HeatCaller;

/// <summary>
/// ON AIR (16 Oct 2026) - the button in front of Stage that kicks a race off
/// the way a station opens a segment: the soundscape's jingle or sweeper,
/// the opening, a bumper, and then the race is staged.
///
/// The club's run of show: jingle/sweeper -> opening -> bumper -> staging.
/// Stage on its own still runs a race exactly as it always has - the staging
/// call, then the round intro if one is due - so a club that never presses
/// On Air hears nothing new.
///
/// THE OPENING IS THE ROUND INTRO, written while the jingle plays rather
/// than after it, so the model's wait is spent under the music. It is the
/// same intro Stage would have asked for - the same facts, the same written-
/// ahead line when one is ready - so Stage is told not to ask for another.
/// With no intro due (commentary off, intros kept for mains and this is a
/// heat, the model not answering) it is the jingle and then the race: no
/// bumper with nothing to come out of.
///
/// Pressing On Air again while it runs skips the rest and stages straight
/// away. Anything else that moves the race on - Stage pressed by hand, the
/// remote, the day schedule - calls it off, and staging is theirs.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly FlagButton _btnOnAir = new() { FlagColour = Color.FromArgb(200, 32, 44), FlagText = Color.White };

    /// <summary>The sequence On Air is running, or null when it is not running.</summary>
    private CancellationTokenSource? _onAirCts;

    /// <summary>On Air was pressed again while it ran: skip what is left and stage now.</summary>
    private bool _onAirStageNow;

    /// <summary>
    /// On Air has said the round intro already, so the Stage it hands over
    /// to must not ask the model for another. Taken - and cleared - by that
    /// Stage.
    /// </summary>
    private bool _openingSaidByOnAir;

    /// <summary>Wires the button. Called from BuildLayout beside Stage.</summary>
    /// <summary>The button's own caption, and what it says while the opening is still being got ready.</summary>
    private const string OnAirCaption = "On Air";
    private const string OnAirWaitingCaption = "Waiting\u2026";

    private void BuildOnAirButton()
    {
        _btnOnAir.Text = OnAirCaption;
        _btnOnAir.Click += async (_, _) => await RunOnAirAsync();

        var tip = new ToolTip();
        AppTheme.StyleToolTip(tip, keep: true);
        tip.SetToolTip(_btnOnAir,
            "Kicks the race off like a radio show: the soundscape's jingle or sweeper, the opening, a bumper, then "
            + "Stage. Press it again to skip straight to Stage. The pieces are set on Sounds -> Imaging.");
    }

    /// <summary>Takes On Air's word that the opening has been said, for the Stage it hands over to. See <see cref="_openingSaidByOnAir"/>.</summary>
    private bool TakeOpeningSaidByOnAir()
    {
        var said = _openingSaidByOnAir;
        _openingSaidByOnAir = false;
        return said;
    }

    /// <summary>
    /// On Air for a demo (16 Oct 2026): the Race Program's "Run as Demo"
    /// comes through here, so a demo opens the way a real race does - the
    /// jingle or sweeper, the opening for <paramref name="round"/>, a bumper
    /// - and then <paramref name="start"/> starts the demo, which stages the
    /// race itself, in place of On Air pressing Stage.
    ///
    /// The round is handed over rather than read from the Round box: a demo
    /// that is not allowed to fill the programme leaves the box on freeform,
    /// and the opening would then have no race to be about.
    /// </summary>
    internal Task GoOnAirThenAsync(RoundDefinition round, Action start) => RunOnAirAsync(round, start);

    private async Task RunOnAirAsync(RoundDefinition? round = null, Action? start = null)
    {
        if (_onAirCts is { } running)
        {
            // A demo asked for while On Air is already running starts the
            // demo straight away; the On Air already going is called off,
            // since it was opening some other race.
            if (start is not null)
            {
                running.Cancel();
                start();
                return;
            }

            // Pressed again: the rest of it is skipped and the race staged now.
            _onAirStageNow = true;
            running.Cancel();
            return;
        }

        if (RefuseIfPracticeRunning("go on air"))
        {
            return;
        }

        // Only between races. A race that is staged or running has had its
        // top already, and Stage's own question about abandoning it is the
        // right one to ask - so On Air leaves it to Stage. A demo already
        // running is the exception: the new demo replaces it, as it always
        // has, so the top of the new one is played.
        if (start is null && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air is for between races - this one is already {_race.State.ToString().ToLowerInvariant()}.");
            return;
        }

        var cts = new CancellationTokenSource();
        _onAirCts = cts;
        _onAirStageNow = false;

        // No sponsor read from here until this race's flag - the opening
        // is about to be written, and the quiet while it is is not a gap.
        BroadcastImaging.OnAirBegins();
        _openingSaidByOnAir = false;
        _btnOnAir.Lit = true;

        // Anything else moving the race on calls this off - see the class
        // remarks. Async, because Cancel() would run whatever is waiting on
        // the token on the thread that is changing the race.
        Action<RaceState> onRaceMoved = state =>
        {
            if (state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                try
                {
                    _ = cts.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // On Air had already finished.
                }
            }
        };

        _race.StateChanged += onRaceMoved;

        var stage = false;

        // What is on air, held so a call-off can take it away again.
        ImagingPlay? jingle = null;
        ImagingBed? bed = null;

        try
        {
            // 1. THE OPENING, WRITTEN AND RENDERED BEFORE ANYTHING PLAYS
            //    (16 Oct 2026). The voice has to HIT THE POST - start talking
            //    the instant the jingle's sung part stops - and it can only
            //    do that with the audio already in hand. Nothing is on air
            //    yet, so the wait for the model is silence before the show,
            //    never dead air in the middle of it.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - getting the opening ready.");

            // SAYS SO ON THE BUTTON (16 Oct 2026). Nothing is heard while
            // the opening is written and rendered, so the button reads
            // "Waiting..." until the show actually starts - the race
            // director can see it was pressed and is on its way.
            _btnOnAir.Text = OnAirWaitingCaption;

            var line = await WriteOpeningAsync(round, cts.Token);

            // THE ANNOUNCER SAYS WHO HE IS (16 Oct 2026), a line into the
            // opening - "Welcome back to the track! I'm Dave Marsh. Heat
            // three..." Put in here rather than asked of the model, so it is
            // there every time and in the same place. See AnnouncerIntro.
            if (line is { } written)
            {
                line = (AnnouncerIntro.IntoOpening(written.Text, _settings.RecapAnnouncerName), written.History);
            }

            AnnouncementService.PreparedSpeech? audio = null;

            if (line is { } toRender)
            {
                audio = await _announcer.PrepareReminderAsync(toRender.Text);
            }

            cts.Token.ThrowIfCancellationRequested();

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - the top of the race.");

            _btnOnAir.Text = OnAirCaption;

            // 2. The jingle, to its post.
            jingle = BroadcastImaging.Start(ImagingCue.TopOfRace);

            if (jingle is not null)
            {
                await jingle.AtPost.WaitAsync(cts.Token);
            }

            // 3. On the post: the bed comes up under the voice, the rest of
            //    the jingle crossfades into it, and the voice hits the post.
            //    The bed is done the moment the race is staged - see
            //    HandTheBedToTheStart.
            bed = ImagingBed.Start(ImagingCue.OpeningBed, _announcer, OpeningBedLevel, OpeningBedDuck);
            jingle?.CrossfadeOut();

            if (line is { } said)
            {
                if (audio is not null)
                {
                    _announcer.SpeakPrepared(audio);
                }
                else
                {
                    _announcer.SpeakReminder(said.Text);
                }

                // As Stage's own intro does: recorded as said, and its facts
                // spent, only once the line has been queued.
                _commentary?.IntroSpoken(said.Text);
                _commentary?.HistorySpoken(said.History);
                _openingSaidByOnAir = true;

                // 4. The bumper, HITTING THE POST (17 Oct 2026): fired while
                //    the opening is still being said, so the bumper's own
                //    voice comes in on the opening's last word - no gap
                //    between the two. The bed dips the moment it starts, and
                //    the staging call comes in at the bumper's out. With no
                //    bumper this simply waits for the opening to finish, as
                //    it always did. See TalkUp.
                if (await BroadcastImaging.TalkUpAsync(ImagingCue.AfterOpening, _announcer, IntroShowLimit, cts.Token) is { } bumper)
                {
                    bed?.Dip(true);

                    try
                    {
                        await bumper.AtPost.WaitAsync(cts.Token);
                    }
                    finally
                    {
                        bumper.CrossfadeOut();
                        bed?.Dip(false);
                    }
                }

                cts.Token.ThrowIfCancellationRequested();
            }

            stage = true;
        }
        catch (OperationCanceledException)
        {
            // Pressed again stages now; anything else moved the race on
            // itself and staging is not ours to do.
            stage = _onAirStageNow;
        }
        catch (Exception ex)
        {
            // Decoration. The race still gets staged.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - {ex.Message}. Staging the race.");
            stage = true;
        }
        finally
        {
            _race.StateChanged -= onRaceMoved;
            _onAirCts = null;
            _onAirStageNow = false;
            _btnOnAir.Lit = false;
            _btnOnAir.Text = OnAirCaption;
            cts.Dispose();
        }

        if (!stage || IsDisposed || Disposing)
        {
            _openingSaidByOnAir = false;
            jingle?.CrossfadeOut();
            bed?.Finish();
            return;
        }

        // A demo stages itself - see GoOnAirThenAsync.
        if (start is not null)
        {
            _openingSaidByOnAir = false;
            HandTheBedToTheStart(bed);
            start();
            return;
        }

        // Not when the race has been staged some other way in the meantime.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            _openingSaidByOnAir = false;
            bed?.Finish();
            return;
        }

        HandTheBedToTheStart(bed);
        _actStage?.Invoke(false, null);
    }

    /// <summary>The opening bed on its own - the show bed level set on the Commentary page.</summary>
    private float OpeningBedLevel =>
        _settings.ShowBedLevel is >= 0 and <= 100 ? _settings.ShowBedLevel / 100f : 0.22f;

    /// <summary>How far the opening bed drops under the voice - the show bed's own duck.</summary>
    private float OpeningBedDuck => Math.Clamp(_settings.ShowBedDuckPercent / 100f, 0.05f, 1f);

    /// <summary>
    /// The longest the opening bed may run after On Air hands over - a start
    /// that never reaches its countdown (a warm-up, a race director who is
    /// not ready) must not leave it looping under the rest of the night.
    /// </summary>
    private static readonly TimeSpan LongestOpeningBed = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Takes the opening bed away when the race is staged (16 Oct 2026,
    /// asked for from the floor: "the bed from the open should be done at
    /// staging, not later"). It used to run on under the staging call until
    /// the countdown; now the open is finished the moment Stage takes hold,
    /// and the staging call has the room. Any other move - a reset - or
    /// <see cref="LongestOpeningBed"/> ends it too.
    /// </summary>
    private void HandTheBedToTheStart(ImagingBed? bed)
    {
        if (bed is null)
        {
            return;
        }

        Action<RaceState>? watcher = null;

        void End()
        {
            if (Interlocked.Exchange(ref watcher, null) is { } w)
            {
                _race.StateChanged -= w;
            }

            bed.Finish();
        }

        // Any move at all, staging included - see the summary.
        watcher = _ => End();

        _race.StateChanged += watcher;

        _ = Task.Delay(LongestOpeningBed).ContinueWith(_ => End(), TaskScheduler.Default);
    }

    /// <summary>
    /// The opening: the round intro for the race in the Round box, written
    /// exactly as Stage writes one - the written-ahead line when it is ready
    /// and still true, otherwise the model, inside the same budget. Null when
    /// no intro is due or none came back, which On Air reads as "no opening".
    ///
    /// Read on the UI thread up to the model call, as Stage's is - see
    /// ReadIntroFacts.
    /// </summary>
    private async Task<(string Text, PackedFacts? History)?> WriteOpeningAsync(RoundDefinition? forRound, CancellationToken ct)
    {
        try
        {
            if (_commentary is null || !_commentary.IsAvailable || !_settings.CommentaryRaceIntros)
            {
                return null;
            }

            var round = forRound ?? _activeRound?.Invoke();

            if (round is null)
            {
                return null;
            }

            if (_settings.CommentaryIntrosMainsOrRivalriesOnly
                && round.Kind != RoundKind.Main
                && !_commentary.HasRivalryAmong(round.AssignedTransponders))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [imaging] No opening for {round.RoundLabel} - intros are set to mains and rivalries only, "
                    + "and this race is neither.");

                return null;
            }

            var (clubNight, form, stake, scene) = ReadIntroFacts(round);

            PackedFacts? history = null;
            string? answer;

            var ready = _writtenAhead.TakeFor(round.Id, round.AssignedTransponders, _commentary.HistorySaid, DateTime.Now);

            if (ready.Why == WrittenAhead.Why.Used && ready.Line is { Length: > 0 } already)
            {
                history = ready.Facts;
                answer = already;
            }
            else
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(RoundIntroModelBudget);

                answer = await _commentary.BuildRoundIntroAsync(
                    round,
                    budget.Token,
                    log: note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"),
                    clubNight: clubNight,
                    form: form,
                    stake: stake,
                    historyPacked: packed => history = packed,
                    scene: scene);
            }

            if (string.IsNullOrWhiteSpace(answer))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening - no intro came back in time, so On Air goes straight to the race.");
                return null;
            }

            return (answer, history);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening - {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// The announcer saying his own name (16 Oct 2026) - a line into each race's
/// opening, and at the end of the line that takes the show into the stop set.
///
/// The name is the one typed as "Announcer's name" on the Commentary page
/// (AppSettings.RecapAnnouncerName - the setting's name is older than this).
/// It is the race announcer's, NOT the recap show's: the recap has a host of
/// its own.
///
/// WRITTEN IN HERE, NOT ASKED OF THE MODEL. A model asked to "say your name
/// near the top" does it most of the time and somewhere different each time;
/// a station's presenter says it in the same place every time. Blank name:
/// nothing changes. A line that already has the name in it (the model used
/// it on its own) is left alone, so he is never introduced twice.
/// </summary>
internal static class AnnouncerIntro
{
    /// <summary>
    /// The opening with "I'm &lt;name&gt;." put in after its first sentence -
    /// "Welcome back to the track! I'm Dave Marsh. Heat three is..." A
    /// one-sentence opening gets it on the end.
    /// </summary>
    internal static string IntoOpening(string text, string? name)
    {
        var who = Who(name);

        if (who.Length == 0 || string.IsNullOrWhiteSpace(text) || Names(text, who))
        {
            return text;
        }

        var said = $"I'm {who}.";
        var cut = FirstSentenceEnds(text);

        return cut < 0
            ? $"{text.TrimEnd()} {said}"
            : $"{text[..cut].TrimEnd()} {said} {text[cut..].TrimStart()}";
    }

    /// <summary>
    /// The line into the stop set with the announcer's sign-off on the end:
    /// "This is Dave Marsh - back after the break." With no break to go to
    /// (the commercial break off, or no sponsors up) it is "This is Dave
    /// Marsh - stay with us." - never his bare name as the last words
    /// (16 Oct 2026): a name as the very end of a line is the word a voice
    /// clips, and a sign-off that stops dead on it sounds cut off, which is
    /// what the room heard. A closing phrase puts the name safely inside the
    /// line and gives the bumper something to hit.
    /// </summary>
    internal static string IntoStopSet(string line, string? name, bool breakFollows)
    {
        var who = Who(name);

        if (who.Length == 0 || string.IsNullOrWhiteSpace(line) || Names(line, who))
        {
            return line;
        }

        var signOff = breakFollows ? $"This is {who} - back after the break." : $"This is {who} - stay with us.";

        return $"{line.TrimEnd()} {signOff}";
    }

    private static string Who(string? name) => (name ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',');

    private static bool Names(string text, string who) => text.Contains(who, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the first sentence ends - just past its full stop, exclamation
    /// or question mark (and any closing quote), where the next sentence
    /// starts. -1 when the whole thing is one sentence. A stop with no space
    /// after it - "4.2 seconds" - is not the end of a sentence.
    /// </summary>
    private static int FirstSentenceEnds(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            var after = i + 1;

            while (after < text.Length && text[after] is '"' or '\'' or '”' or '’' or ')')
            {
                after++;
            }

            if (after >= text.Length)
            {
                return -1;
            }

            if (char.IsWhiteSpace(text[after]))
            {
                return after;
            }
        }

        return -1;
    }
}
